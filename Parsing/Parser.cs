using System.Globalization;
using Pyrop.Lexing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Parsing;

public sealed class Parser
{
    private static readonly IReadOnlySet<string> NoDeclarations = new HashSet<string>();
    private static readonly OuterNames NoOuterNames = new(new HashSet<string>(), "parameter", GuardsBoundNames: false);

    private readonly ModuleNamespace _names;
    private readonly TokenCursor _cursor;
    private FunctionSignature? _currentFunction;
    private int _loopDepth;
    private int _nesting;

    private Parser(IReadOnlyList<Token> tokens, ModuleNamespace names, bool reportsIncompleteInput)
    {
        _cursor = new TokenCursor(tokens, reportsIncompleteInput);
        _names = names;
    }

    public static SyntaxTree Parse(IReadOnlyList<Token> tokens, ModuleNamespace names, bool reportsIncompleteInput = true)
    {
        var statements = new List<Statement>();
        new Parser(tokens, names, reportsIncompleteInput).ParseInto(statements);
        return new SyntaxTree(statements);
    }

    public static (SyntaxTree Tree, PyropException? Error) ParseUntilError(IReadOnlyList<Token> tokens, ModuleNamespace names)
    {
        var statements = new List<Statement>();
        try
        {
            new Parser(tokens, names, reportsIncompleteInput: true).ParseInto(statements);
            return (new SyntaxTree(statements), null);
        }
        catch (PyropException exception)
        {
            return (new SyntaxTree(statements), exception);
        }
    }

    private void ParseInto(List<Statement> statements)
    {
        var declared = new HashSet<string>();
        while (!_cursor.IsAtEnd)
        {
            statements.Add(ParseListedStatement(declared, NoOuterNames));
        }
    }

    private bool AtTopLevel => _nesting == 0 && _currentFunction is null;

    private Statement ParseListedStatement(HashSet<string> declared, OuterNames outer)
    {
        var token = _cursor.Current;
        if (token.IsWord(Keywords.Def))
        {
            return ParseDef(declared, outer);
        }

        if (token.IsWord(Keywords.For))
        {
            return ParseFor(declared, outer);
        }

        if (token.IsWord(Keywords.IfSome))
        {
            return ParseIfSome(declared, outer);
        }

        if (AtTopLevel && token.Kind == TokenKind.Word)
        {
            switch (token.Text)
            {
                case Keywords.Use:
                    return ParseUse();
                case Keywords.Import:
                    return ParseImport();
                case Keywords.Export:
                    return ParseExport();
                case Keywords.Record:
                    return ParseRecord();
                case Keywords.Choice:
                    return ParseChoice();
            }
        }

        return ParseStatement();
    }

    private void ThrowIfFailed(Token statement)
    {
        if (_names.Failures.OfStatement(statement) is { } failure)
        {
            throw failure;
        }
    }

    private Statement ParseUse()
    {
        var start = _cursor.Advance();
        ThrowIfFailed(start);
        var nameToken = _cursor.ExpectWord($"a module name after '{Keywords.Use}'");
        if (Keywords.IsReserved(nameToken.Text))
        {
            throw _cursor.Error($"'{nameToken.Text}' is a reserved word and cannot be used as a module name.", nameToken);
        }

        var pathToken = _cursor.Current;
        if (pathToken.Kind != TokenKind.String)
        {
            throw _cursor.Error(
                $"'{Keywords.Use}' takes a module name and then a string literal path: {Keywords.Use} {nameToken.Text} \"<path>\", found {pathToken.Describe()}.");
        }

        _cursor.Advance();
        if (!_names.Uses.ContainsKey(nameToken.Text))
        {
            throw new InvalidOperationException($"Module '{nameToken.Text}' was not registered by the signature pre-pass.");
        }

        return new UseStatement(nameToken.Text, pathToken.Text, start.Position);
    }

    private Statement ParseImport()
    {
        var start = _cursor.Advance();
        ThrowIfFailed(start);
        if (!_cursor.Current.IsWord(Keywords.From))
        {
            throw _cursor.Error(
                $"'{Keywords.Import}' takes a module and the names to import: {Keywords.Import} {Keywords.From} <module> <name>… {Keywords.End}, found {_cursor.Current.Describe()}.");
        }

        var from = _cursor.Advance();
        var module = _cursor.ExpectWord($"a module name after '{Keywords.From}'");
        var names = new List<string>();
        while (!_cursor.Current.IsWord(Keywords.End))
        {
            if (_cursor.IsAtEnd)
            {
                throw _cursor.Error($"Expected '{Keywords.End}' to close the {Keywords.Import} started at {start.Position}.");
            }

            if (_cursor.Current.Kind != TokenKind.Word || Keywords.IsReserved(_cursor.Current.Text))
            {
                throw _cursor.Error(
                    $"Expected a name to import or '{Keywords.End}' to close the {Keywords.Import} started at {start.Position}, found {_cursor.Current.Describe()}.");
            }

            var name = _cursor.Advance();
            QualifiedNames.Resolve(_names, new QualifiedName(from, module, name), _cursor);
            names.Add(name.Text);
        }

        if (names.Count == 0)
        {
            throw _cursor.Error(
                $"'{Keywords.Import}' needs at least one name before '{Keywords.End}', such as {Keywords.Import} {Keywords.From} {module.Text} <name> {Keywords.End}.");
        }

        _cursor.Advance();
        return new ImportStatement(module.Text, names, start.Position);
    }

    private Statement ParseExport()
    {
        _cursor.Advance();
        if (_cursor.Current.IsWord(Keywords.Record))
        {
            return ParseRecord();
        }

        if (_cursor.Current.IsWord(Keywords.Choice))
        {
            return ParseChoice();
        }

        if (!_cursor.Current.IsWord(Keywords.Function))
        {
            throw _cursor.Error(
                $"'{Keywords.Export}' must be followed by a function, record or choice definition, found {_cursor.Current.Describe()}.");
        }

        return ParseFunction();
    }

    private Statement ParseRecord()
    {
        var start = _cursor.Advance();
        var name = RecordDeclarations.ReadName(_cursor).Text;
        RecordDeclarations.ReadFields(_cursor, _names, name);
        var record = _names.Records.GetValueOrDefault(name)
            ?? throw new InvalidOperationException($"Record '{name}' was not registered by the signature pre-pass.");
        return new RecordStatement(record, start.Position);
    }

    private Statement ParseChoice()
    {
        var start = _cursor.Advance();
        var name = ChoiceDeclarations.ReadName(_cursor).Text;
        ChoiceDeclarations.ReadVariants(_cursor, start, name);
        var choice = _names.Choices.GetValueOrDefault(name)
            ?? throw new InvalidOperationException($"Choice '{name}' was not registered by the signature pre-pass.");
        return new ChoiceStatement(choice, start.Position);
    }

    private Statement ParseBody(string owner, OuterNames? outer = null)
    {
        if (_cursor.Current.IsWord(Keywords.Def))
        {
            throw _cursor.Error($"A def cannot be the body of {owner}; wrap it in block … end.");
        }

        _nesting++;
        try
        {
            if (_cursor.Current.IsWord(Keywords.Block))
            {
                return ParseBlock(outer ?? NoOuterNames);
            }

            if (outer is { GuardsBoundNames: true } && _cursor.Current.IsWord(Keywords.For))
            {
                return ParseFor(NoDeclarations, outer);
            }

            return outer is { GuardsBoundNames: true } && _cursor.Current.IsWord(Keywords.IfSome)
                ? ParseIfSome(NoDeclarations, outer)
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
            Keywords.Use or Keywords.Import or Keywords.Export =>
                throw _cursor.Error($"'{token.Text}' is only allowed at the top level."),
            Keywords.Record => throw _cursor.Error("Records can only be declared at the top level."),
            Keywords.Choice => throw _cursor.Error("Choices can only be declared at the top level."),
            Keywords.Set => ParseSet(),
            Keywords.If => ParseIf(),
            Keywords.IfSome => ParseIfSome(NoDeclarations, NoOuterNames),
            Keywords.Attempt => throw _cursor.Error(
                $"'{Keywords.Attempt}' cannot be a statement: its result tells whether the operation succeeded. " +
                $"Use it as a value, as in {Keywords.If} not {Keywords.Attempt} write_file \"save.txt\" text …."),
            Keywords.While => ParseWhile(),
            Keywords.For => ParseFor(NoDeclarations, NoOuterNames),
            Keywords.In => throw _cursor.Error($"'{Keywords.In}' without a matching '{Keywords.For}'."),
            Keywords.Break => ParseLoopControl(isBreak: true),
            Keywords.Continue => ParseLoopControl(isBreak: false),
            Keywords.Function => ParseFunction(),
            Keywords.Return => ParseReturn(),
            Keywords.Write => ParseWrite(appendNewLine: false, toStandardError: false),
            Keywords.WriteLine => ParseWrite(appendNewLine: true, toStandardError: false),
            Keywords.WriteError => ParseWrite(appendNewLine: false, toStandardError: true),
            Keywords.WriteErrorLine => ParseWrite(appendNewLine: true, toStandardError: true),
            Keywords.Exit => ParseExit(),
            Keywords.WithStyle => ParseWithStyle(),
            Keywords.Block => ParseNestedBlock(),
            Keywords.End => throw _cursor.Error("'end' without a matching 'block'."),
            Keywords.Else => throw _cursor.Error($"'else' without a matching 'if' or '{Keywords.IfSome}'."),
            Keywords.From => new CallStatement(ParseQualified(allowVoid: true, asStatement: true), token.Position),
            _ when Builtins.All.ContainsKey(token.Text) || _names.FindCallable(token.Text) is not null =>
                new CallStatement(ParseCall(allowVoid: true), token.Position),
            _ when _names.FindRecord(token.Text) is not null => throw ConstructorStatement(token, token.Text),
            _ when _names.FindChoice(token.Text) is { } choice => throw ChoiceStartingStatement(token, choice),
            _ when _names.Failures.OfName(token.Text) is { } failure => throw failure,
            _ when _names.Uses.ContainsKey(token.Text) => throw ModuleUsedAsValue(token),
            _ when Keywords.RetiredHint(token.Text) is { } retired => throw _cursor.Error(retired),
            _ when Keywords.IsReserved(token.Text) => throw _cursor.Error($"'{token.Text}' cannot start a statement."),
            _ when token.Kind == TokenKind.Word && Keywords.UnderscoredSpelling(token.Text) is { } spelling =>
                throw _cursor.Error(Keywords.UnderscoreHint(token.Text, spelling)),
            _ => throw StrayValue(token),
        };
    }

    private PyropException ModuleUsedAsValue(Token token) =>
        _cursor.Error(
            $"'{token.Text}' is a module; name one of its exported functions with {Keywords.From} {token.Text} <name>.",
            token);

    private PyropException ConstructorStatement(Token token, string written) =>
        _cursor.Error(
            $"A record constructor cannot be a statement: '{written} …' would make a record only to throw it away, " +
            "and no statement may start with a type name, so that parameter and field lists know where they end. " +
            $"Use the new record as a value, as in {Keywords.Def} {written} <name> {written} ….",
            token);

    private PyropException ChoiceStartingStatement(Token token, ChoiceType choice, string? module = null) =>
        _cursor.Error(
            $"A statement cannot start with the choice '{choice.Name}': a choice is a type, and no statement may start with a type name. " +
            $"Use one of its variants as a value, as in {Keywords.Def} {Written(module, choice.Name)} <name> {Written(module, choice.Variants[0].Name)}.",
            token);

    private PyropException ChoiceAsValue(Token token, ChoiceType choice, string? module = null) =>
        _cursor.Error(
            $"'{choice.Name}' is a choice, not a value; name one of its variants, such as {Written(module, choice.Variants[0].Name)}.", token);

    private static string Written(string? module, string name) => module is null ? name : $"{Keywords.From} {module} {name}";

    private PyropException StrayValue(Token token, string? described = null) =>
        _cursor.Error(
            $"{described ?? token.Describe()} cannot stand alone as a statement. " +
            "This usually means the expression before it was given more arguments than its operation takes.",
            token);

    private Statement ParseDef(HashSet<string> declared, OuterNames outer)
    {
        var start = _cursor.Advance();
        var type = TypeReader.ReadValueType(_cursor, _names, "variable");
        var nameToken = _cursor.Current;
        var name = _cursor.ExpectIdentifier("variable name", _names);
        if (outer.Names.Contains(name))
        {
            throw _cursor.Error($"Variable '{name}' would shadow the {outer.Kind} of the same name.", nameToken);
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
        var name = _cursor.ExpectIdentifier("variable name", _names);
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

    private Statement ParseIfSome(IReadOnlySet<string> declared, OuterNames outer)
    {
        var start = _cursor.Advance();
        if (_cursor.Current.IsWord(PyropType.Void.Name))
        {
            throw _cursor.Error(
                $"'{Keywords.IfSome}' takes the type of the value it unwraps, such as {Keywords.IfSome} int age …; void has no values.");
        }

        var type = TypeReader.ReadValueType(_cursor, _names, "value");
        var nameToken = _cursor.Current;
        var name = _cursor.ExpectIdentifier("value name", _names);
        if (declared.Contains(name))
        {
            throw _cursor.Error(
                $"The {Keywords.IfSome} value '{name}' would shadow the variable of the same name declared earlier in this block.",
                nameToken);
        }

        if (outer.GuardsBoundNames && outer.Names.Contains(name))
        {
            throw _cursor.Error($"The {Keywords.IfSome} value '{name}' would shadow the {outer.Kind} of the same name.", nameToken);
        }

        var option = ParseExpression();
        var then = ParseBody($"an {Keywords.IfSome}", new OuterNames(new HashSet<string> { name }, $"{Keywords.IfSome} value", GuardsBoundNames: false));
        Statement? otherwise = null;
        if (_cursor.Current.IsWord(Keywords.Else))
        {
            _cursor.Advance();
            otherwise = ParseBody("an else");
        }

        return new IfSomeStatement(type, name, option, then, otherwise, start.Position);
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

    private Statement ParseFor(IReadOnlySet<string> declared, OuterNames outer)
    {
        var start = _cursor.Advance();
        var type = TypeReader.ReadValueType(_cursor, _names, "loop variable");
        var variableToken = _cursor.Current;
        var variable = _cursor.ExpectIdentifier("loop variable name", _names);
        if (declared.Contains(variable))
        {
            throw _cursor.Error(
                $"Loop variable '{variable}' would shadow the variable of the same name declared earlier in this block.",
                variableToken);
        }

        if (outer.GuardsBoundNames && outer.Names.Contains(variable))
        {
            throw _cursor.Error($"Loop variable '{variable}' would shadow the {outer.Kind} of the same name.", variableToken);
        }

        if (!_cursor.Current.IsWord(Keywords.In))
        {
            throw _cursor.Error(
                $"Expected '{Keywords.In}' after the loop variable: {Keywords.For} {type.Name} {variable} {Keywords.In} <list> <statement>, found {_cursor.Current.Describe()}.");
        }

        _cursor.Advance();
        var list = ParseExpression();
        _loopDepth++;
        try
        {
            var body = ParseBody("a for loop", new OuterNames(new HashSet<string> { variable }, "loop variable", GuardsBoundNames: false));
            return new ForStatement(type, variable, list, body, start.Position);
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
            throw _cursor.Error($"'{token.Text}' is only allowed inside a while or for loop.", token);
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

        var header = SignatureScanner.ReadHeader(_cursor, _names, isExported: false);
        var signature = _names.Functions.Find(header.Name)
            ?? throw new InvalidOperationException($"Function '{header.Name}' was not registered by the signature pre-pass.");

        var outerLoopDepth = _loopDepth;
        _currentFunction = signature;
        _loopDepth = 0;
        try
        {
            var parameters = signature.Parameters.Select(parameter => parameter.Name).ToHashSet();
            return new FunctionStatement(signature, ParseBody("a function", new OuterNames(parameters, "parameter", GuardsBoundNames: true)), start.Position);
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

    private Statement ParseWrite(bool appendNewLine, bool toStandardError)
    {
        var start = _cursor.Advance();
        return new WriteStatement(ParseExpression(), appendNewLine, toStandardError, start.Position);
    }

    private Statement ParseExit()
    {
        var start = _cursor.Advance();
        return new ExitStatement(ParseExpression(), start.Position);
    }

    private Statement ParseWithStyle()
    {
        var start = _cursor.Advance();
        var style = ParseExpression();
        return new WithStyleStatement(style, ParseBody("a with_style"), start.Position);
    }

    private Statement ParseNestedBlock()
    {
        _nesting++;
        try
        {
            return ParseBlock(NoOuterNames);
        }
        finally
        {
            _nesting--;
        }
    }

    private BlockStatement ParseBlock(OuterNames outer)
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

            statements.Add(ParseListedStatement(declared, outer));
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
                var target = TypeReader.ReadValueType(_cursor, _names, "conversion target");
                return new ConvertExpression(target, ParseExpression(), token.Position);
            case Keywords.ListOf:
                return ParseListOf();
            case Keywords.MapOf:
                return ParseMapOf();
            case Keywords.Attempt:
                return ParseAttempt();
            case Keywords.None:
                _cursor.Advance();
                if (_cursor.Current.IsWord(PyropType.Void.Name))
                {
                    throw _cursor.Error($"'{Keywords.None}' takes the type of the missing value, such as {Keywords.None} int; void has no values.");
                }

                return new NoneExpression(TypeReader.ReadValueType(_cursor, _names, "missing value"), token.Position);
            case Keywords.GetField:
                _cursor.Advance();
                var read = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.GetField}'");
                return new GetFieldExpression(read.Text, read.Position, ParseExpression(), token.Position);
            case Keywords.WithField:
                _cursor.Advance();
                var changed = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.WithField}'");
                var record = ParseExpression();
                return new WithFieldExpression(changed.Text, changed.Position, record, ParseExpression(), token.Position);
        }

        if (token.IsWord(Keywords.From))
        {
            return ParseQualified(allowVoid: false, asStatement: false);
        }

        if (Builtins.All.ContainsKey(token.Text) || _names.FindCallable(token.Text) is not null)
        {
            return ParseCall(allowVoid: false);
        }

        if (_names.FindRecord(token.Text) is { } constructed)
        {
            _cursor.Advance();
            return ParseConstruct(constructed, token, null);
        }

        if (_names.FindVariant(token.Text) is { } variant)
        {
            _cursor.Advance();
            return new VariantExpression(variant, token.Position);
        }

        if (_names.FindChoice(token.Text) is { } choice)
        {
            throw ChoiceAsValue(token, choice);
        }

        if (Keywords.RetiredHint(token.Text) is { } retired)
        {
            throw _cursor.Error(retired);
        }

        if (Keywords.IsReserved(token.Text))
        {
            throw _cursor.Error($"Expected an expression, found keyword '{token.Text}'.");
        }

        if (_names.Failures.OfName(token.Text) is { } failure)
        {
            throw failure;
        }

        if (_names.Uses.ContainsKey(token.Text))
        {
            throw ModuleUsedAsValue(token);
        }

        _cursor.Advance();
        return new VariableExpression(token.Text, token.Position);
    }

    private Expression ParseAttempt()
    {
        var start = _cursor.Advance();
        var operand = _cursor.Current;
        if (operand.IsWord(Keywords.Convert))
        {
            return new AttemptExpression(ParseExpression(), start.Position);
        }

        if (operand.Kind == TokenKind.Word && Builtins.All.ContainsKey(operand.Text))
        {
            return new AttemptExpression(ParseCall(allowVoid: true), start.Position);
        }

        if (operand.Kind == TokenKind.Word && Keywords.RetiredHint(operand.Text) is { } retired)
        {
            throw _cursor.Error(retired);
        }

        var found = operand.Kind == TokenKind.Word && _names.FindCallable(operand.Text) is not null
            ? $"the function '{operand.Text}'; a function's own failures are runtime errors, so that a bug in it is never mistaken for bad input"
            : operand.IsWord(Keywords.From)
            ? $"the qualified name starting at '{Keywords.From}'; only built-in operations can follow '{Keywords.Attempt}'"
            : operand.Describe();
        throw _cursor.Error(
            $"'{Keywords.Attempt}' takes a built-in operation or a convert, such as {Keywords.Attempt} {Keywords.Convert} int read " +
            $"or {Keywords.Attempt} read_file \"notes.txt\"; found {found}.",
            operand);
    }

    private Expression ParseListOf()
    {
        var start = _cursor.Advance();
        var elementType = TypeReader.ReadValueType(_cursor, _names, "list item");
        var items = new List<Expression>();
        while (!AtItemsEnd(start, "a list item"))
        {
            items.Add(ParseExpression());
        }

        _cursor.Advance();
        return new ListOfExpression(elementType, items, start.Position);
    }

    private Expression ParseMapOf()
    {
        var start = _cursor.Advance();
        var keyType = TypeReader.ReadKeyType(_cursor, _names);
        var valueType = TypeReader.ReadValueType(_cursor, _names, "map value");
        var entries = new List<MapEntry>();
        while (!AtItemsEnd(start, "a key"))
        {
            var key = ParseExpression();
            if (_cursor.Current.IsWord(Keywords.End))
            {
                throw _cursor.Error(
                    $"The last key of the {Keywords.MapOf} started at {start.Position} has no value; every key is followed by its value.");
            }

            if (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text))
            {
                throw _cursor.Error(
                    $"Expected the value of the last key of the {Keywords.MapOf} started at {start.Position}, found '{_cursor.Current.Text}'.");
            }

            entries.Add(new MapEntry(key, ParseExpression()));
        }

        _cursor.Advance();
        return new MapOfExpression(keyType, valueType, entries, start.Position);
    }

    private bool AtItemsEnd(Token start, string expected)
    {
        if (_cursor.Current.IsWord(Keywords.End))
        {
            return true;
        }

        if (_cursor.IsAtEnd)
        {
            throw _cursor.Error($"Expected '{Keywords.End}' to close the {start.Text} started at {start.Position}.");
        }

        if (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text))
        {
            throw _cursor.Error(
                $"Expected {expected} or '{Keywords.End}' to close the {start.Text} started at {start.Position}, found '{_cursor.Current.Text}'.");
        }

        return false;
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

        return ParseFunctionCall(_names.FindCallable(token.Text)!, token, null, allowVoid);
    }

    private Expression ParseQualified(bool allowVoid, bool asStatement)
    {
        var qualified = QualifiedNames.Read(_cursor);
        return QualifiedNames.Resolve(_names, qualified, _cursor) switch
        {
            RecordType when asStatement => throw ConstructorStatement(qualified.From, qualified.ToString()),
            RecordType record => ParseConstruct(record, qualified.From, qualified.Module.Text),
            ChoiceType choice when asStatement => throw ChoiceStartingStatement(qualified.From, choice, qualified.Module.Text),
            ChoiceType choice => throw ChoiceAsValue(qualified.From, choice, qualified.Module.Text),
            ChoiceVariant when asStatement => throw StrayValue(qualified.From, $"'{qualified}'"),
            ChoiceVariant variant => new VariantExpression(variant, qualified.From.Position, qualified.Module.Text),
            FunctionSignature function => ParseFunctionCall(function, qualified.From, qualified.Module.Text, allowVoid),
            var other => throw new InvalidOperationException($"Unknown qualified target {other.GetType().Name}."),
        };
    }

    private Expression ParseConstruct(RecordType record, Token start, string? module) =>
        new ConstructExpression(record, ParseArguments(record.Fields.Count), start.Position, module);

    private Expression ParseFunctionCall(FunctionSignature function, Token start, string? module, bool allowVoid)
    {
        if (function.ReturnType == PyropType.Void && !allowVoid)
        {
            throw _cursor.Error($"Function '{function.Name}' returns void and cannot be used as a value.", start);
        }

        return new FunctionCallExpression(function, ParseArguments(function.Arity), start.Position, module);
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

    private sealed record OuterNames(IReadOnlySet<string> Names, string Kind, bool GuardsBoundNames);
}
