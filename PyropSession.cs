using Pyrop.Checking;
using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop;

public sealed record InspectedProgram(SyntaxTree? Tree, PyropException? Error, string? Text);

public sealed record AnalyzedSource(
    string Text, IReadOnlyList<Token> Tokens, ModuleNamespace Names, IReadOnlyList<Statement> Statements, IReadOnlyList<ParseGap> Gaps, TypeFacts Facts);

public enum InputStatus
{
    Ready,
    Incomplete,
    AwaitingElse,
    AwaitingFields,
}

public sealed class PyropSession
{
    private enum ParseMode
    {
        FirstError,
        UntilFirstError,
        Recovering,
        AllErrors,
    }

    private readonly Interpreter _interpreter;
    private readonly ISourceStore _store;
    private readonly BuiltinLibraries _libraries;
    private readonly Dictionary<string, string> _sources = new();
    private ModuleNamespace? _names;
    private IReadOnlyDictionary<string, PyropType> _globals = new Dictionary<string, PyropType>();
    private Dictionary<string, LoadedModule> _modules = new(SourceLoader.PathComparer);
    private string? _lastInput;

    public PyropSession(LineInput input, IProgramOutput output, HostContext? host = null, ISourceStore? store = null)
    {
        host ??= HostContext.Isolated;
        _interpreter = new Interpreter(input, output, host);
        _libraries = host.Libraries;
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

    public string Format(string source, string sourcePath, string? sourceFullPath = null) =>
        LargeStack.Run(() => FormatOnCurrentThread(source, sourcePath, sourceFullPath));

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
        var parsed = Parse(source, sourcePath, sourceFullPath, ParseMode.AllErrors);
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
            var parsed = Parse(source, sourcePath, sourceFullPath, ParseMode.Recovering, parsesModules: false);
            var facts = TypeChecker.Describe(parsed.Program.Main);
            return new AnalyzedSource(source, tokens, parsed.Names, parsed.Program.Main.Tree.Statements, parsed.Gaps, facts);
        }
        catch (PyropException)
        {
            return null;
        }
    }

    private string FormatOnCurrentThread(string source, string sourcePath, string? sourceFullPath)
    {
        sourcePath = DisplayNameOf(sourcePath, sourceFullPath);
        _sources[sourcePath] = source;
        var parsed = Parse(source, sourcePath, sourceFullPath, parsesModules: false);
        return Formatter.Format(source, Lexer.Tokenize(source, sourcePath), parsed.Program.Main.Tree.Statements);
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
            parsed = Parse(source, sourcePath, null, ParseMode.UntilFirstError);
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

        var parsed = Parse(source, sourcePath, sourceFullPath, sourcePath is null ? ParseMode.FirstError : ParseMode.AllErrors);
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
        PyropException? ParseError = null,
        IReadOnlyList<ParseGap>? ParseGaps = null)
    {
        public IReadOnlyList<ParseGap> Gaps => ParseGaps ?? [];
    }

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

    private ParsedInput Parse(string source, string? sourcePath, string? sourceFullPath, ParseMode mode = ParseMode.FirstError, bool parsesModules = true)
    {
        var baseDirectory = Environment.CurrentDirectory;
        string? fullPath = null;
        if (sourcePath is not null)
        {
            fullPath = sourceFullPath ?? Path.GetFullPath(sourcePath);
            baseDirectory = Path.GetDirectoryName(fullPath)!;
        }

        var existing = _names ?? ModuleNamespace.For(new Module(fullPath, sourcePath ?? "input"), _libraries);
        var loader = new SourceLoader(_store);
        try
        {
            var tokens = Lexer.Tokenize(source, sourcePath);
            var scan = SignatureScanner.Scan(
                tokens, existing, new ModuleLoading(loader, baseDirectory, _modules), _globals.Keys.ToHashSet());

            PyropException? parseError = null;
            IReadOnlyList<ParseGap> gaps = [];
            SyntaxTree tree;
            switch (mode)
            {
                case ParseMode.UntilFirstError:
                    (tree, parseError) = Parser.ParseUntilError(tokens, scan.Names);
                    break;
                case ParseMode.FirstError:
                    tree = Parser.Parse(tokens, scan.Names);
                    break;
                default:
                    var report = Parser.ParseAll(tokens, scan.Names);
                    (tree, parseError, gaps) = (report.Tree, report.Error, report.Gaps);
                    break;
            }

            if (mode == ParseMode.AllErrors)
            {
                var reports = scan.Loaded.Select(module => (module, Report: Parser.ParseAll(module.File!.Tokens, module.Names, reportsIncompleteInput: false))).ToList();
                var failures = reports.Select(entry => entry.Report.Error).Prepend(parseError).OfType<PyropException>().ToList();
                if (Parser.Combined(failures) is { } combined)
                {
                    throw combined;
                }

                var checkedModules = reports.Select(entry => new ModuleTree(entry.module.Module, entry.Report.Tree, entry.module.Names)).ToList();
                return new ParsedInput(new ParsedProgram(checkedModules, new ModuleTree(scan.Names.Module, tree, scan.Names)), scan.Names, scan.Loaded, [.. loader.Files]);
            }

            var modules = parseError is null && parsesModules
                ? scan.Loaded
                    .Select(module => new ModuleTree(module.Module, Parser.Parse(module.File!.Tokens, module.Names, reportsIncompleteInput: false), module.Names))
                    .ToList()
                : [];
            var program = new ParsedProgram(modules, new ModuleTree(scan.Names.Module, tree, scan.Names));
            return new ParsedInput(program, scan.Names, scan.Loaded, [.. loader.Files], parseError, gaps);
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
        StyledStatement styled => EndsWithOpenIf(styled.Body),
        _ => false,
    };
}
