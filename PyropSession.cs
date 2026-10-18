using Pyrop.Checking;
using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop;

public sealed record InspectedProgram(SyntaxTree? Tree, PyropException? Error, string? Text);

public sealed record AnalyzedSource(IReadOnlyList<Token> Tokens, ModuleNamespace Names, IReadOnlyList<Statement> Statements, SourcePosition? StoppedAt);

public enum InputStatus
{
    Ready,
    Incomplete,
    AwaitingElse,
    AwaitingFields,
}

public sealed class PyropSession
{
    private readonly Interpreter _interpreter;
    private readonly ISourceStore _store;
    private readonly Dictionary<string, string> _sources = new();
    private ModuleNamespace? _names;
    private IReadOnlyDictionary<string, PyropType> _globals = new Dictionary<string, PyropType>();
    private Dictionary<string, LoadedModule> _modules = new(SourceLoader.PathComparer);
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

    internal int NestingLimit
    {
        get => _interpreter.NestingLimit;
        set => _interpreter.NestingLimit = value;
    }

    internal int CollectionLimit
    {
        get => _interpreter.CollectionLimit;
        set => _interpreter.CollectionLimit = value;
    }

    public InputStatus Probe(string source) => LargeStack.Run(() => ProbeOnCurrentThread(source));

    public int? Run(string source, string? sourcePath = null, string? sourceFullPath = null) =>
        LargeStack.Run(() => RunOnCurrentThread(source, sourcePath, sourceFullPath));

    public IReadOnlyList<SourceFile> Check(string source, string sourcePath, string? sourceFullPath = null) =>
        LargeStack.Run(() => CheckOnCurrentThread(source, sourcePath, sourceFullPath));

    public InspectedProgram Inspect(string source, string sourcePath) => LargeStack.Run(() => InspectOnCurrentThread(source, sourcePath));

    public AnalyzedSource? Analyze(string source, string sourcePath, string? sourceFullPath = null) =>
        LargeStack.Run(() => AnalyzeOnCurrentThread(source, sourcePath, sourceFullPath));

    public string? SourceText(string? sourceName) =>
        sourceName is null ? _lastInput : _sources.GetValueOrDefault(sourceName);

    private InputStatus ProbeOnCurrentThread(string source)
    {
        try
        {
            var statements = Parse(source, null, null).Program.Main.Tree.Statements;
            return statements.Count == 0 ? InputStatus.Ready
                : statements[^1] is RecordStatement ? InputStatus.AwaitingFields
                : EndsWithOpenIf(statements[^1]) ? InputStatus.AwaitingElse
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
        sourcePath = DisplayNameOf(sourcePath, sourceFullPath);
        _sources[sourcePath] = source;
        var parsed = Parse(source, sourcePath, sourceFullPath);
        TypeChecker.Check(parsed.Program, _globals);
        return parsed.Files;
    }

    private AnalyzedSource? AnalyzeOnCurrentThread(string source, string sourcePath, string? sourceFullPath)
    {
        sourcePath = DisplayNameOf(sourcePath, sourceFullPath);
        _sources[sourcePath] = source;
        try
        {
            var tokens = Lexer.Tokenize(source, sourcePath);
            var parsed = Parse(source, sourcePath, sourceFullPath, untilError: true, parsesModules: false);
            return new AnalyzedSource(tokens, parsed.Names, parsed.Program.Main.Tree.Statements, parsed.ParseError?.Errors[0].Position);
        }
        catch (PyropException)
        {
            return null;
        }
    }

    private static InspectedProgram Inspected(SyntaxTree tree, ModuleNamespace names, PyropException? error) =>
        new(tree, error, TreePrinter.Print(tree, names));

    private InspectedProgram InspectOnCurrentThread(string source, string sourcePath)
    {
        sourcePath = DisplayNameOf(sourcePath, null);
        _sources[sourcePath] = source;
        ParsedInput parsed;
        try
        {
            parsed = Parse(source, sourcePath, null, untilError: true);
        }
        catch (PyropException exception)
        {
            return new InspectedProgram(null, exception, null);
        }

        var tree = parsed.Program.Main.Tree;
        if (parsed.ParseError is { } parseError)
        {
            return Inspected(tree, parsed.Names, parseError);
        }

        try
        {
            TypeChecker.Check(parsed.Program, _globals);
            return Inspected(tree, parsed.Names, null);
        }
        catch (PyropException exception)
        {
            return Inspected(tree, parsed.Names, exception);
        }
    }

    private int? RunOnCurrentThread(string source, string? sourcePath, string? sourceFullPath)
    {
        if (sourcePath is null)
        {
            _lastInput = source;
        }
        else
        {
            sourcePath = DisplayNameOf(sourcePath, sourceFullPath);
            _sources[sourcePath] = source;
        }

        var parsed = Parse(source, sourcePath, sourceFullPath);
        var check = TypeChecker.Check(parsed.Program, _globals);
        _names = parsed.Names;
        _globals = check.Globals;
        foreach (var module in parsed.Loaded)
        {
            _modules[module.Module.FullPath!] = module;
        }

        try
        {
            return _interpreter.Execute(parsed.Program, check);
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            throw new PyropException(ErrorStage.Internal, exception.Message, new SourcePosition(0, 0));
        }
    }

    private sealed record ParsedInput(
        ParsedProgram Program,
        ModuleNamespace Names,
        IReadOnlyList<LoadedModule> Loaded,
        IReadOnlyList<SourceFile> Files,
        PyropException? ParseError = null);

    private string DisplayNameOf(string sourcePath, string? sourceFullPath)
    {
        try
        {
            return _store.DisplayNameOf(sourceFullPath ?? Path.GetFullPath(sourcePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return sourcePath;
        }
    }

    private ParsedInput Parse(string source, string? sourcePath, string? sourceFullPath, bool untilError = false, bool parsesModules = true)
    {
        var baseDirectory = Environment.CurrentDirectory;
        string? fullPath = null;
        if (sourcePath is not null)
        {
            fullPath = sourceFullPath ?? Path.GetFullPath(sourcePath);
            baseDirectory = Path.GetDirectoryName(fullPath)!;
        }

        var existing = _names ?? ModuleNamespace.For(new Module(fullPath, sourcePath ?? "input"));
        var loader = new SourceLoader(_store);
        try
        {
            var tokens = Lexer.Tokenize(source, sourcePath);
            var scan = SignatureScanner.Scan(
                tokens, existing, new ModuleLoading(loader, baseDirectory, _modules), _globals.Keys.ToHashSet());

            PyropException? parseError = null;
            SyntaxTree tree;
            if (untilError)
            {
                (tree, parseError) = Parser.ParseUntilError(tokens, scan.Names);
            }
            else
            {
                tree = Parser.Parse(tokens, scan.Names);
            }

            var modules = parseError is null && parsesModules
                ? scan.Loaded
                    .Select(module => new ModuleTree(module.Module, Parser.Parse(module.File.Tokens, module.Names, reportsIncompleteInput: false)))
                    .ToList()
                : [];
            var program = new ParsedProgram(modules, new ModuleTree(scan.Names.Module, tree));
            return new ParsedInput(program, scan.Names, scan.Loaded, [.. loader.Files], parseError);
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
        IfSomeStatement { Else: null } => true,
        IfSomeStatement { Else: { } otherwise } => EndsWithOpenIf(otherwise),
        WhileStatement loop => EndsWithOpenIf(loop.Body),
        ForStatement loop => EndsWithOpenIf(loop.Body),
        FunctionStatement function => EndsWithOpenIf(function.Body),
        WithStyleStatement styled => EndsWithOpenIf(styled.Body),
        _ => false,
    };
}
