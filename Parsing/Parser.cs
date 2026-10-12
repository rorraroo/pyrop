using System.Globalization;
using Pyrop.Lexing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Parsing;

public sealed class Parser
{
    private static readonly IReadOnlySet<string> NoParameters = new HashSet<string>();

    private readonly FunctionTable _functions;
    private readonly IncludeContext? _includes;
    private TokenCursor _cursor;
    private string? _directory;
    private FunctionSignature? _currentFunction;
    private int _loopDepth;
    private int _nesting;

    private Parser(IReadOnlyList<Token> tokens, FunctionTable functions, IncludeContext? includes)
    {
        _cursor = new TokenCursor(tokens);
        _functions = functions;
        _includes = includes;
        _directory = includes?.BaseDirectory;
    }

    public static SyntaxTree Parse(IReadOnlyList<Token> tokens, FunctionTable functions, IncludeContext? includes = null) =>
        new Parser(tokens, functions, includes).ParseProgram(new HashSet<string>());

    private SyntaxTree ParseProgram(HashSet<string> declared)
    {
        var statements = new List<Statement>();
        while (!_cursor.IsAtEnd)
        {
            statements.Add(ParseListedStatement(declared, NoParameters));
        }

        return new SyntaxTree(statements);
    }

    private Statement ParseListedStatement(HashSet<string> declared, IReadOnlySet<string> parameters)
    {
        if (_cursor.Current.IsWord(Keywords.Def))
        {
            return ParseDef(declared, parameters);
        }

        if (_cursor.Current.IsWord(Keywords.Include) && _nesting == 0 && _currentFunction is null)
        {
            return ParseInclude(declared);
        }

        return ParseStatement();
    }

    private Statement ParseInclude(HashSet<string> globals)
    {
        var start = _cursor.Advance();
        var pathToken = _cursor.Current;
        if (pathToken.Kind != TokenKind.String)
        {
            throw _cursor.Error($"'include' takes a string literal path, found {pathToken.Describe()}.");
        }

        _cursor.Advance();
        if (_includes is null || _directory is null)
        {
            throw _cursor.Error("'include' is not available here.", start);
        }

        var file = _includes.Loader.Load(pathToken.Text, _directory, pathToken.Position);
        if (!_includes.Included.Add(file.FullPath))
        {
            return new IncludeStatement(pathToken.Text, null, start.Position);
        }

        var outerCursor = _cursor;
        var outerDirectory = _directory;
        _cursor = new TokenCursor(file.Tokens, reportsIncompleteInput: false);
        _directory = file.Directory;
        try
        {
            return new IncludeStatement(pathToken.Text, ParseProgram(globals), start.Position);
        }
        finally
        {
            _cursor = outerCursor;
            _directory = outerDirectory;
        }
    }

    private Statement ParseBody(string owner, IReadOnlySet<string>? parameters = null)
    {
        if (_cursor.Current.IsWord(Keywords.Def))
        {
            throw _cursor.Error($"A def cannot be the body of {owner}; wrap it in block … end.");
        }

        _nesting++;
        try
        {
            return _cursor.Current.IsWord(Keywords.Block)
                ? ParseBlock(parameters ?? NoParameters)
                : ParseStatement();
        }
        finally
        {
            _nesting--;
        }
    }

    private Statement ParseStatement()
    {
        var token = _cursor.Current;
        if (token.Kind == TokenKind.EndOfInput)
        {
            throw _cursor.Error("Expected a statement, found end of input.");
        }

        if (token.Kind != TokenKind.Word)
        {
            throw StrayValue(token);
        }

        return token.Text switch
        {
            Keywords.Def => throw _cursor.Error("A def cannot be used here; wrap it in block … end."),
            Keywords.Include => throw _cursor.Error("'include' is only allowed at the top level."),
            Keywords.Set => ParseSet(),
            Keywords.If => ParseIf(),
            Keywords.While => ParseWhile(),
            Keywords.Break => ParseLoopControl(isBreak: true),
            Keywords.Continue => ParseLoopControl(isBreak: false),
            Keywords.Function => ParseFunction(),
            Keywords.Return => ParseReturn(),
            Keywords.Write => ParseWrite(appendNewLine: false),
            Keywords.WriteLine => ParseWrite(appendNewLine: true),
            Keywords.Exit => ParseExit(),
            Keywords.Block => ParseNestedBlock(),
            Keywords.End => throw _cursor.Error("'end' without a matching 'block'."),
            Keywords.Else => throw _cursor.Error("'else' without a matching 'if'."),
            _ when Builtins.All.ContainsKey(token.Text) || _functions.Contains(token.Text) =>
                new CallStatement(ParseCall(allowVoid: true), token.Position),
            _ when Keywords.IsReserved(token.Text) => throw _cursor.Error($"'{token.Text}' cannot start a statement."),
            _ when token.Kind == TokenKind.Word && Keywords.UnderscoredSpelling(token.Text) is { } spelling =>
                throw _cursor.Error(Keywords.UnderscoreHint(token.Text, spelling)),
            _ => throw StrayValue(token),
        };
    }

    private PyropException StrayValue(Token token) =>
        _cursor.Error(
            $"{token.Describe()} cannot stand alone as a statement. " +
            "This usually means the expression before it was given more arguments than its operation takes.",
            token);

    private Statement ParseDef(HashSet<string> declared, IReadOnlySet<string> parameters)
    {
        var start = _cursor.Advance();
        var type = TypeReader.ReadValueType(_cursor, "variable");
        var nameToken = _cursor.Current;
        var name = _cursor.ExpectIdentifier("variable name", _functions);
        if (parameters.Contains(name))
        {
            throw _cursor.Error($"Variable '{name}' would shadow the parameter of the same name.", nameToken);
        }

        if (!declared.Add(name))
        {
            throw _cursor.Error($"Variable '{name}' is already declared in this block.", nameToken);
        }

        if (_cursor.IsAtEnd || (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text)))
        {
            throw _cursor.Error($"Variable '{name}' needs an initial value: def {type.Name} {name} <expression>.");
        }

        return new DefStatement(type, name, ParseExpression(), start.Position);
    }

    private Statement ParseSet()
    {
        var start = _cursor.Advance();
        var name = _cursor.ExpectIdentifier("variable name", _functions);
        return new SetStatement(name, ParseExpression(), start.Position);
    }

    private Statement ParseIf()
    {
        var start = _cursor.Advance();
        var condition = ParseExpression();
        var then = ParseBody("an if");
        Statement? otherwise = null;
        if (_cursor.Current.IsWord(Keywords.Else))
        {
            _cursor.Advance();
            otherwise = ParseBody("an else");
        }

        return new IfStatement(condition, then, otherwise, start.Position);
    }

    private Statement ParseWhile()
    {
        var start = _cursor.Advance();
        var condition = ParseExpression();
        _loopDepth++;
        try
        {
            return new WhileStatement(condition, ParseBody("a while loop"), start.Position);
        }
        finally
        {
            _loopDepth--;
        }
    }

    private Statement ParseLoopControl(bool isBreak)
    {
        var token = _cursor.Advance();
        if (_loopDepth == 0)
        {
            throw _cursor.Error($"'{token.Text}' is only allowed inside a while loop.", token);
        }

        return isBreak ? new BreakStatement(token.Position) : new ContinueStatement(token.Position);
    }

    private Statement ParseFunction()
    {
        var start = _cursor.Advance();
        if (_nesting > 0 || _currentFunction is not null)
        {
            throw _cursor.Error("Functions can only be defined at the top level.", start);
        }

        var header = SignatureScanner.ReadHeader(_cursor, _functions);
        var signature = _functions.Find(header.Name)
            ?? throw new InvalidOperationException($"Function '{header.Name}' was not registered by the signature pre-pass.");

        var outerLoopDepth = _loopDepth;
        _currentFunction = signature;
        _loopDepth = 0;
        try
        {
            var parameters = signature.Parameters.Select(parameter => parameter.Name).ToHashSet();
            return new FunctionStatement(signature, ParseBody("a function", parameters), start.Position);
        }
        finally
        {
            _currentFunction = null;
            _loopDepth = outerLoopDepth;
        }
    }

    private Statement ParseReturn()
    {
        var start = _cursor.Advance();
        if (_currentFunction is null)
        {
            throw _cursor.Error("'return' is only allowed inside a function.", start);
        }

        var voidName = PyropType.Void.Name;
        if (_currentFunction.ReturnType == PyropType.Void)
        {
            if (!_cursor.Current.IsWord(voidName))
            {
                throw _cursor.Error(
                    $"Void function '{_currentFunction.Name}' must use 'return void', found {_cursor.Current.Describe()}.");
            }

            _cursor.Advance();
            return new ReturnStatement(null, start.Position);
        }

        if (_cursor.Current.IsWord(voidName))
        {
            throw _cursor.Error(
                $"'return void' is only allowed in void functions; '{_currentFunction.Name}' returns {_currentFunction.ReturnType.Name}.");
        }

        return new ReturnStatement(ParseExpression(), start.Position);
    }

    private Statement ParseWrite(bool appendNewLine)
    {
        var start = _cursor.Advance();
        return new WriteStatement(ParseExpression(), appendNewLine, start.Position);
    }

    private Statement ParseExit()
    {
        var start = _cursor.Advance();
        return new ExitStatement(ParseExpression(), start.Position);
    }

    private Statement ParseNestedBlock()
    {
        _nesting++;
        try
        {
            return ParseBlock(NoParameters);
        }
        finally
        {
            _nesting--;
        }
    }

    private BlockStatement ParseBlock(IReadOnlySet<string> parameters)
    {
        var start = _cursor.Advance();
        var declared = new HashSet<string>();
        var statements = new List<Statement>();
        while (!_cursor.Current.IsWord(Keywords.End))
        {
            if (_cursor.IsAtEnd)
            {
                throw _cursor.Error($"Expected 'end' to close the block started at {start.Position}.");
            }

            statements.Add(ParseListedStatement(declared, parameters));
        }

        _cursor.Advance();
        return new BlockStatement(statements, start.Position);
    }

    private Expression ParseExpression()
    {
        var token = _cursor.Current;
        switch (token.Kind)
        {
            case TokenKind.EndOfInput:
                throw _cursor.Error("Expected an expression, found end of input.");
            case TokenKind.Integer:
                _cursor.Advance();
                return new LiteralExpression(new IntValue(long.Parse(token.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)), token.Position);
            case TokenKind.Float:
                _cursor.Advance();
                return new LiteralExpression(new FloatValue(double.Parse(token.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)), token.Position);
            case TokenKind.String:
                _cursor.Advance();
                return new LiteralExpression(new StringValue(token.Text), token.Position);
        }

        switch (token.Text)
        {
            case Keywords.True:
                _cursor.Advance();
                return new LiteralExpression(BoolValue.True, token.Position);
            case Keywords.False:
                _cursor.Advance();
                return new LiteralExpression(BoolValue.False, token.Position);
            case Keywords.Convert:
                _cursor.Advance();
                var target = TypeReader.ReadValueType(_cursor, "conversion target");
                return new ConvertExpression(target, ParseExpression(), token.Position);
        }

        if (Builtins.All.ContainsKey(token.Text) || _functions.Contains(token.Text))
        {
            return ParseCall(allowVoid: false);
        }

        if (Keywords.IsReserved(token.Text))
        {
            throw _cursor.Error($"Expected an expression, found keyword '{token.Text}'.");
        }

        _cursor.Advance();
        return new VariableExpression(token.Text, token.Position);
    }

    private Expression ParseCall(bool allowVoid)
    {
        var token = _cursor.Advance();
        if (Builtins.All.TryGetValue(token.Text, out var operation))
        {
            if (operation.ReturnsVoid && !allowVoid)
            {
                throw _cursor.Error($"Operation '{operation.Name}' returns void and cannot be used as a value.", token);
            }

            return new BuiltinCallExpression(operation, ParseArguments(operation.Arity), token.Position);
        }

        var function = _functions.Find(token.Text)!;
        if (function.ReturnType == PyropType.Void && !allowVoid)
        {
            throw _cursor.Error($"Function '{function.Name}' returns void and cannot be used as a value.", token);
        }

        return new FunctionCallExpression(function, ParseArguments(function.Arity), token.Position);
    }

    private IReadOnlyList<Expression> ParseArguments(int count)
    {
        var arguments = new Expression[count];
        for (var index = 0; index < count; index++)
        {
            arguments[index] = ParseExpression();
        }

        return arguments;
    }
}
