using Pyrop.Checking;
using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop;

public enum InputStatus
{
    Ready,
    Incomplete,
    AwaitingElse,
}

public sealed class PyropSession
{
    private readonly Interpreter _interpreter;
    private readonly ISourceStore _store;
    private readonly Dictionary<string, string> _sources = new();
    private FunctionTable _functions = FunctionTable.Empty;
    private IReadOnlyDictionary<string, PyropType> _globals = new Dictionary<string, PyropType>();
    private HashSet<string> _included = new(SourceLoader.PathComparer);
    private string? _lastInput;

    public PyropSession(LineInput input, IProgramOutput output, HostContext? host = null, ISourceStore? store = null)
    {
        _interpreter = new Interpreter(input, output, host ?? HostContext.Isolated);
        _store = store ?? FileSourceStore.Instance;
    }

    public PyropSession(TextReader input, IProgramOutput output, HostContext? host = null, ISourceStore? store = null)
        : this(new LineInput(input), output, host, store)
    {
    }

    public InputStatus Probe(string source) => LargeStack.Run(() => ProbeOnCurrentThread(source));

    public int? Run(string source, string? sourcePath = null, string? sourceFullPath = null) =>
        LargeStack.Run(() => RunOnCurrentThread(source, sourcePath, sourceFullPath));

    public IReadOnlyList<SourceFile> Check(string source, string sourcePath, string? sourceFullPath = null) =>
        LargeStack.Run(() => CheckOnCurrentThread(source, sourcePath, sourceFullPath));

    public string? SourceText(string? sourceName) =>
        sourceName is null ? _lastInput : _sources.GetValueOrDefault(sourceName);

    private InputStatus ProbeOnCurrentThread(string source)
    {
        try
        {
            var parsed = Parse(source, null, null);
            return parsed.Tree.Statements.Count > 0 && EndsWithOpenIf(parsed.Tree.Statements[^1])
                ? InputStatus.AwaitingElse
                : InputStatus.Ready;
        }
        catch (PyropException exception) when (exception.IsIncompleteInput)
        {
            return InputStatus.Incomplete;
        }
        catch (PyropException)
        {
            return InputStatus.Ready;
        }
    }

    private IReadOnlyList<SourceFile> CheckOnCurrentThread(string source, string sourcePath, string? sourceFullPath)
    {
        _sources[sourcePath] = source;
        var parsed = Parse(source, sourcePath, sourceFullPath);
        TypeChecker.Check(parsed.Tree, _globals);
        return parsed.Files;
    }

    private int? RunOnCurrentThread(string source, string? sourcePath, string? sourceFullPath)
    {
        if (sourcePath is null)
        {
            _lastInput = source;
        }
        else
        {
            _sources[sourcePath] = source;
        }

        var parsed = Parse(source, sourcePath, sourceFullPath);
        var check = TypeChecker.Check(parsed.Tree, _globals);
        _functions = parsed.Functions;
        _globals = check.Globals;
        _included = parsed.Included;
        try
        {
            return _interpreter.Execute(parsed.Tree, check);
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            throw new PyropException(ErrorStage.Internal, exception.Message, new SourcePosition(0, 0));
        }
    }

    private sealed record ParsedInput(SyntaxTree Tree, FunctionTable Functions, HashSet<string> Included, IReadOnlyList<SourceFile> Files);

    private ParsedInput Parse(string source, string? sourcePath, string? sourceFullPath)
    {
        var included = new HashSet<string>(_included, SourceLoader.PathComparer);
        var baseDirectory = Environment.CurrentDirectory;
        if (sourcePath is not null)
        {
            var fullPath = sourceFullPath ?? Path.GetFullPath(sourcePath);
            included.Add(fullPath);
            baseDirectory = Path.GetDirectoryName(fullPath)!;
        }

        var loader = new SourceLoader(_store);
        var includes = new IncludeContext(loader, baseDirectory, included);
        try
        {
            var tokens = Lexer.Tokenize(source, sourcePath);
            var signatures = SignatureScanner.Scan(tokens, _functions, includes);
            foreach (var signature in signatures.Where(signature => _globals.ContainsKey(signature.Name)))
            {
                throw new PyropException(
                    ErrorStage.Parse,
                    $"'{signature.Name}' is already a global variable and cannot be used as a function name.",
                    signature.Position);
            }

            var functions = _functions.With(signatures);
            return new ParsedInput(Parser.Parse(tokens, functions, includes), functions, included, [.. loader.Files]);
        }
        finally
        {
            foreach (var (name, text) in loader.Texts)
            {
                _sources[name] = text;
            }
        }
    }

    private static bool EndsWithOpenIf(Statement statement) => statement switch
    {
        IfStatement { Else: null } => true,
        IfStatement { Else: { } otherwise } => EndsWithOpenIf(otherwise),
        WhileStatement loop => EndsWithOpenIf(loop.Body),
        FunctionStatement function => EndsWithOpenIf(function.Body),
        WithStyleStatement styled => EndsWithOpenIf(styled.Body),
        _ => false,
    };
}
