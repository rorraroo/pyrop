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
    private FunctionTable _functions = FunctionTable.Empty;
    private IReadOnlyDictionary<string, PyropType> _globals = new Dictionary<string, PyropType>();

    public InputStatus Probe(string source) => LargeStack.Run(() => ProbeOnCurrentThread(source));

    public void Run(string source) => LargeStack.Run(() => RunOnCurrentThread(source));

    private InputStatus ProbeOnCurrentThread(string source)
    {
        try
        {
            var (tree, _) = Parse(source);
            return tree.Statements.Count > 0 && EndsWithOpenIf(tree.Statements[^1])
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

    private bool RunOnCurrentThread(string source)
    {
        var (tree, functions) = Parse(source);
        var check = TypeChecker.Check(tree, _globals);
        _functions = functions;
        _globals = check.Globals;
        try
        {
            _interpreter.Execute(tree, check);
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            throw new PyropException(ErrorStage.Internal, exception.Message, new SourcePosition(0, 0));
        }

        return true;
    }

    private (SyntaxTree Tree, FunctionTable Functions) Parse(string source)
    {
        var tokens = Lexer.Tokenize(source);
        var signatures = SignatureScanner.Scan(tokens, _functions);
        foreach (var signature in signatures.Where(signature => _globals.ContainsKey(signature.Name)))
        {
            throw new PyropException(
                ErrorStage.Parse,
                $"'{signature.Name}' is already a global variable and cannot be used as a function name.",
                signature.Position);
        }

        var functions = _functions.With(signatures);
        return (Parser.Parse(tokens, functions), functions);
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
