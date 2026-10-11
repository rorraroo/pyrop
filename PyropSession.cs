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

public sealed class PyropSession(TextReader input, IProgramOutput output)
{
    private readonly Interpreter _interpreter = new(input, output);
    private readonly Dictionary<string, string> _sources = new();
    private FunctionTable _functions = FunctionTable.Empty;
    private IReadOnlyDictionary<string, PyropType> _globals = new Dictionary<string, PyropType>();
    private HashSet<string> _included = new(SourceLoader.PathComparer);
    private string? _lastInput;

    public InputStatus Probe(string source) => LargeStack.Run(() => ProbeOnCurrentThread(source));

    public void Run(string source, string? sourcePath = null) => LargeStack.Run(() => RunOnCurrentThread(source, sourcePath));

    public string? SourceText(string? sourceName) =>
        sourceName is null ? _lastInput : _sources.GetValueOrDefault(sourceName);

    private InputStatus ProbeOnCurrentThread(string source)
    {
        try
        {
            var parsed = Parse(source, null);
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

    private bool RunOnCurrentThread(string source, string? sourcePath)
    {
        if (sourcePath is null)
        {
            _lastInput = source;
        }
        else
        {
            _sources[sourcePath] = source;
        }

        var parsed = Parse(source, sourcePath);
        var check = TypeChecker.Check(parsed.Tree, _globals);
        _functions = parsed.Functions;
        _globals = check.Globals;
        _included = parsed.Included;
        try
        {
            _interpreter.Execute(parsed.Tree, check);
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            throw new PyropException(ErrorStage.Internal, exception.Message, new SourcePosition(0, 0));
        }

        return true;
    }

    private sealed record ParsedInput(SyntaxTree Tree, FunctionTable Functions, HashSet<string> Included);

    private ParsedInput Parse(string source, string? sourcePath)
    {
        var included = new HashSet<string>(_included, SourceLoader.PathComparer);
        var baseDirectory = Environment.CurrentDirectory;
        if (sourcePath is not null)
        {
            var fullPath = Path.GetFullPath(sourcePath);
            included.Add(fullPath);
            baseDirectory = Path.GetDirectoryName(fullPath)!;
        }

        var loader = new SourceLoader();
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
            return new ParsedInput(Parser.Parse(tokens, functions, includes), functions, included);
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
        _ => false,
    };
}
