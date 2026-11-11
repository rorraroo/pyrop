using System.Globalization;
using Pyrop.Lexing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Parsing;

public sealed record ParseGap(SourcePosition From, SourcePosition To);

public sealed record ParseReport(SyntaxTree Tree, PyropException? Error, IReadOnlyList<ParseGap> Gaps);

public sealed class Parser
{
    public const int MaxNesting = 1000;

    private static readonly IReadOnlySet<string> NoDeclarations = new HashSet<string>();
    private static readonly OuterNames NoOuterNames = new(new HashSet<string>(), "parameter", GuardsBoundNames: false);

    private readonly ModuleNamespace _names;
    private readonly TokenCursor _cursor;
    private FunctionSignature? _currentFunction;
    private int _loopDepth;
    private int _nesting;
    private int _expressionDepth;
    private Token? _openBlock;
    private readonly HashSet<string> _boundNames = [];

    private Parser(IReadOnlyList<Token> tokens, ModuleNamespace names, bool reportsIncompleteInput, IEnumerable<string>? globals)
    {
        _cursor = new TokenCursor(tokens, reportsIncompleteInput);
        _names = names;
        _boundNames.UnionWith(globals ?? []);
    }

    public static SyntaxTree Parse(IReadOnlyList<Token> tokens, ModuleNamespace names, bool reportsIncompleteInput = true, IEnumerable<string>? globals = null)
    {
        var statements = new List<Statement>();
        new Parser(tokens, names, reportsIncompleteInput, globals).ParseInto(statements);
        return new SyntaxTree(statements);
    }

    public static (SyntaxTree Tree, PyropException? Error) ParseUntilError(IReadOnlyList<Token> tokens, ModuleNamespace names, IEnumerable<string>? globals = null)
    {
        var statements = new List<Statement>();
        try
        {
            new Parser(tokens, names, reportsIncompleteInput: true, globals).ParseInto(statements);
            return (new SyntaxTree(statements), null);
        }
        catch (PyropException exception)
        {
            return (new SyntaxTree(statements), exception);
        }
    }

    public static ParseReport ParseAll(IReadOnlyList<Token> tokens, ModuleNamespace names, bool reportsIncompleteInput = true, IEnumerable<string>? globals = null)
    {
        var parser = new Parser(tokens, names, reportsIncompleteInput, globals);
        var statements = new List<Statement>();
        var failures = new List<PyropException>();
        var gaps = new List<ParseGap>();
        var declared = new HashSet<string>();
        while (!parser._cursor.IsAtEnd)
        {
            var start = parser._cursor.Index;
            var first = parser._cursor.Current;
            try
            {
                statements.Add(parser.ParseListedStatement(declared, NoOuterNames));
            }
            catch (PyropException failure)
            {
                if (failures.Count == 0 || !IsCloser(first))
                {
                    failures.Add(failure);
                }

                parser.ResumeAfter(start);
                gaps.Add(new ParseGap(first.Position, parser._cursor.Current.Position));
            }
        }

        return new ParseReport(new SyntaxTree(statements), Combined(failures), gaps);
    }

    public static PyropException? Combined(IReadOnlyList<PyropException> failures) =>
        failures.Count == 0 ? null : new PyropException(failures.SelectMany(failure => failure.Errors).Distinct().ToList(), failures[0].IsIncompleteInput);

    private static bool IsCloser(Token token) => token.IsWord(Keywords.End) || token.IsWord(Keywords.Else) || token.IsWord(Keywords.When);

    private static bool ResumesAt(Token token, Token failed) =>
        token.Kind == TokenKind.Word
        && (Keywords.StartsDefinition(token.Text)
            || ((failed.IsWord(Keywords.Use) || failed.IsWord(Keywords.Import)) && (token.IsWord(Keywords.Use) || token.IsWord(Keywords.Import))));

    private void ResumeAfter(int start)
    {
        _cursor.MoveTo(start);
        var first = _cursor.Advance();
        if (first.IsWord(Keywords.Export))
        {
            _cursor.Advance();
        }

        while (!_cursor.IsAtEnd && !ResumesAt(_cursor.Current, first))
        {
            _cursor.Advance();
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
        if (token.IsWord(Keywords.Define))
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

        if (token.IsWord(Keywords.Match))
        {
            return ParseMatch(declared, outer);
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
            throw _cursor.Error(Keywords.NotAName(nameToken.Text, "module name"), nameToken);
        }

        var pathToken = _cursor.Current;
        if (pathToken.Kind != TokenKind.String && !pathToken.IsWord(Keywords.Library))
        {
            throw _cursor.Error(
                $"'{Keywords.Use}' takes a module name and then a string literal path or the word {Keywords.Library}: {Keywords.Use} {nameToken.Text} \"<path>\" or {Keywords.Use} {nameToken.Text} {Keywords.Library}, found {pathToken.Describe()}.");
        }

        _cursor.Advance();
        if (!_names.Uses.ContainsKey(nameToken.Text))
        {
            throw new InvalidOperationException($"Module '{nameToken.Text}' was not registered by the signature pre-pass.");
        }

        return new UseStatement(nameToken.Text, pathToken.Kind == TokenKind.String ? pathToken.Text : null, start.Position);
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
                throw _cursor.Error($"Expected '{Keywords.End}' to close the {Keywords.Import} started at {start.Position.Spoken}.");
            }

            if (_cursor.Current.Kind != TokenKind.Word || Keywords.IsReserved(_cursor.Current.Text))
            {
                throw _cursor.Error(
                    $"Expected a name to import or '{Keywords.End}' to close the {Keywords.Import} started at {start.Position.Spoken}, found {_cursor.Current.Describe()}.");
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
            if (_cursor.Current.Kind == TokenKind.Word && (Keywords.IsReservedForLater(_cursor.Current.Text) || IsUnknownWord(_cursor.Current)) && Keywords.UnavailableHint(_cursor.Current.Text) is { } unavailable)
            {
                throw _cursor.Error(unavailable);
            }

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
        ChoiceDeclarations.ReadVariants(_cursor, start, name, _names);
        var choice = _names.Choices.GetValueOrDefault(name)
            ?? throw new InvalidOperationException($"Choice '{name}' was not registered by the signature pre-pass.");
        return new ChoiceStatement(choice, start.Position);
    }

    private Statement ParseBody(string owner, OuterNames? outer = null)
    {
        if (_cursor.Current.IsWord(Keywords.Define))
        {
            throw _cursor.Error($"A {Keywords.Define} cannot be the body of {owner}; wrap it in block … end.");
        }

        RequireRoomToNest(_nesting);
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

            if (outer is { GuardsBoundNames: true } && _cursor.Current.IsWord(Keywords.Match))
            {
                return ParseMatch(NoDeclarations, outer);
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

    private void RequireRoomToNest(int depth)
    {
        if (depth >= MaxNesting)
        {
            throw _cursor.Error($"This code is nested more than {MaxNesting} levels deep; move part of it into a function or a variable.");
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
            Keywords.Define => throw _cursor.Error($"A {Keywords.Define} cannot be used here; wrap it in block … end."),
            Keywords.Use or Keywords.Import or Keywords.Export =>
                throw _cursor.Error($"'{token.Text}' is only allowed at the top level."),
            Keywords.Record => throw _cursor.Error("Records can only be declared at the top level."),
            Keywords.Choice => throw _cursor.Error("Choices can only be declared at the top level."),
            Keywords.Set => ParseSet(),
            Keywords.If => ParseIf(),
            Keywords.IfSome => ParseIfSome(NoDeclarations, NoOuterNames),
            Keywords.Match => ParseMatch(NoDeclarations, NoOuterNames),
            Keywords.When => throw _cursor.Error($"'{Keywords.When}' without a matching '{Keywords.Match}'."),
            Keywords.With => throw _cursor.Error(
                $"'{Keywords.With}' cannot start a statement; it belongs to a {Keywords.Copy}, which changes several fields with one '{Keywords.With}' each: " +
                $"{Keywords.Copy} <record> {Keywords.With} <field> <value> {Keywords.With} <field> <value> {Keywords.End}."),
            Keywords.Attempt => throw _cursor.Error(
                $"'{Keywords.Attempt}' cannot be a statement: its result tells whether the operation succeeded. " +
                $"Use it as a value, as in {Keywords.If} not {Keywords.Attempt} {Keywords.From} files save \"save.txt\" text …."),
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
            Keywords.Styled => ParseStyled(),
            Keywords.Block => ParseNestedBlock(),
            Keywords.End => throw _cursor.Error("'end' without a matching 'block'."),
            Keywords.Else => throw ElseWithoutIf(),
            Keywords.True or Keywords.False => throw StrayValue(token),
            Keywords.From => new CallStatement(ParseQualified(allowVoid: true, asStatement: true), token.Position),
            _ when _names.FindOperation(token.Text) is not null || _names.FindCallable(token.Text) is not null =>
                new CallStatement(ParseCall(allowVoid: true), token.Position),
            _ when _names.FindRecord(token.Text) is not null => throw ConstructorStatement(token, token.Text),
            _ when _names.FindChoice(token.Text) is { } choice => throw ChoiceStartingStatement(token, choice),
            _ when _names.FindVariant(token.Text) is { Arity: > 0 } variant => throw VariantStatement(token, variant.Name),
            _ when _names.Failures.OfName(token.Text) is { } failure => throw failure,
            _ when (Keywords.IsReservedForLater(token.Text) || IsUnknownWord(token)) && Keywords.UnavailableHint(token.Text) is { } unavailable => throw _cursor.Error(unavailable),
            _ when Keywords.IsReserved(token.Text) => throw _cursor.Error($"'{token.Text}' cannot start a statement."),
            _ when token.Kind == TokenKind.Word && _names.LibraryHint(token.Text) is { } hint => throw _cursor.Error(hint),
            _ when token.Kind == TokenKind.Word && Keywords.UnderscoredSpelling(token.Text) is { } spelling =>
                throw _cursor.Error(Keywords.UnderscoreHint(token.Text, spelling)),
            _ when IsUnknownWord(token) => throw UnknownStatement(token),
            _ => throw StrayValue(token),
        };
    }

    private PyropException ElseWithoutIf() =>
        _cursor.Error(
            _openBlock is { } block
                ? $"'else' without a matching 'if' or '{Keywords.IfSome}' in the block started at {block.Position.Spoken}. If the 'if' is outside that block, close the block with '{Keywords.End}' first."
                : $"'else' without a matching 'if' or '{Keywords.IfSome}'. An if takes one statement before its else; put several in {Keywords.Block} … {Keywords.End}.");

    private bool IsUnknownWord(Token token) =>
        token.Kind == TokenKind.Word
        && !Keywords.IsReserved(token.Text)
        && !_boundNames.Contains(token.Text)
        && !_names.Uses.ContainsKey(token.Text)
        && _names.FindOperation(token.Text) is null
        && _names.FindCallable(token.Text) is null
        && _names.FindRecord(token.Text) is null
        && _names.FindChoice(token.Text) is null
        && _names.FindVariant(token.Text) is null
        && !_cursor.AppearsAfter(token.Text, NamesAType);

    private bool NamesAType(Token token) =>
        token.Kind == TokenKind.Word && (PyropTypes.ValueConstructors.ContainsKey(token.Text) || _names.FindType(token.Text) is not null);

    private string DidYouMean(string word, IEnumerable<string> candidates) =>
        Suggestions.Closest(word, candidates) is { } closest ? $" Did you mean '{closest}'?" : "";

    private IEnumerable<string> KnownWords =>
        Keywords.LanguageWords
            .Where(word => word is not (Keywords.In or Keywords.Of or Keywords.With or Keywords.The or Keywords.Library))
            .Concat(Builtins.All.Keys)
            .Concat(_names.Functions.Signatures.Select(function => function.Name))
            .Concat(_names.Imports.Keys);

    private PyropException UnknownStatement(Token token)
    {
        var habit = token.Text switch
        {
            "print" or "println" or "echo" or "puts" => $" To show a value, use {Keywords.WriteLine}.",
            "elif" or "elsif" or "elseif" => $" An {Keywords.Else} takes one statement, so write {Keywords.Else} {Keywords.If}.",
            "then" or "do" => " Nothing comes between a condition and its statement; remove this word.",
            "var" or "let" or "const" => $" A variable is declared with {Keywords.Define} <type> <name> <value>.",
            "func" or "fn" or "fun" or "sub" => $" A function is declared with {Keywords.Function} <return type> <name>, then its parameters and body.",
            _ => DidYouMean(token.Text, KnownWords),
        };
        return _cursor.Error($"'{Token.Shortened(token.Text)}' is not a statement, operation or function.{habit}", token);
    }

    private PyropException UntypedParameter(Token token) =>
        _cursor.Error(
            Suggestions.Closest(token.Text, KnownWords) is not null
                ? UnknownStatement(token).Errors[0].Message
                : $"Parameter '{Token.Shortened(token.Text)}' needs a type, as in int {Token.Shortened(token.Text)}.",
            token);

    private PyropException ConstructorStatement(Token token, string written) =>
        _cursor.Error(
            $"A record constructor cannot be a statement: '{written} …' would make a record only to throw it away, " +
            "and no statement may start with a type name, so that a parameter list knows where it ends. " +
            $"Use the new record as a value, as in {Keywords.Define} {written} <name> {written} ….",
            token);

    private PyropException ChoiceStartingStatement(Token token, ChoiceType choice, string? module = null) =>
        _cursor.Error(
            $"A statement cannot start with the choice '{choice.Name}': a choice is a type, and no statement may start with a type name. " +
            $"Use one of its variants as a value, as in {Keywords.Define} {Written(module, choice.Name)} <name> {Written(module, choice.Variants[0].Example)}.",
            token);

    private PyropException ChoiceAsValue(Token token, ChoiceType choice, string? module = null) =>
        _cursor.Error(
            $"'{choice.Name}' is a choice, not a value; name one of its variants, such as {Written(module, choice.Variants[0].Example)}.", token);

    private PyropException VariantStatement(Token token, string written) =>
        _cursor.Error(
            $"A variant that carries data cannot be a statement: '{written} …' would make a value only to throw it away. " +
            $"Use it as a value, as in {Keywords.Set} <name> {written} ….",
            token);

    private static string Written(string? module, string name) => module is null ? name : $"{Keywords.From} {module} {name}";

    private PyropException StrayValue(Token token, string? described = null)
    {
        if (described is null
            && ReferenceEquals(token, _cursor.Current)
            && _cursor.Behind(1) is { } previous
            && previous.Position.Line == token.Position.Line
            && _cursor.Behind(3)?.IsWord(Keywords.From) != true
            && IsUnknownWord(previous))
        {
            return (_names.LibraryHint(previous.Text) ?? Keywords.UnavailableHint(previous.Text)) is { } hint
                ? _cursor.Error(hint, previous)
                : _cursor.Error(
                    $"'{Token.Shortened(previous.Text)}' is not an operation or function, so {token.Describe()} after it cannot be one of its values.{DidYouMean(previous.Text, KnownWords)}",
                    previous);
        }

        return StrayValueAfterExpression(token, described);
    }

    private PyropException StrayValueAfterExpression(Token token, string? described) =>
        _cursor.Error(
            $"{described ?? token.Describe()} cannot stand alone as a statement. " +
            "This usually means the expression before it was given more arguments than its operation takes." +
            (token.Kind == TokenKind.Word && _names.Uses.ContainsKey(token.Text)
                ? $" If '{token.Text}' was meant as the module, name one of its exported functions with {Keywords.From} {token.Text} <name>."
                : "") +
            LibraryHintBefore(token),
            token);

    private string LibraryHintBefore(Token token) =>
        ReferenceEquals(token, _cursor.Current)
        && _cursor.Behind(1) is { Kind: TokenKind.Word } previous
        && _names.LibraryHint(previous.Text) is { } hint
            ? $" If '{previous.Text}' was meant as {_names.Libraries.KindOf(previous.Text)}: {hint}"
            : "";

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

        _boundNames.Add(name);

        if (_cursor.IsAtEnd || (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text)))
        {
            throw _cursor.Error($"Variable '{name}' needs an initial value: {Keywords.Define} {type.Name} {name} <expression>.");
        }

        return new DefStatement(type, name, ParseExpression(), start.Position);
    }

    private Statement ParseSet()
    {
        var start = _cursor.Advance();
        if (_cursor.Current.IsWord(Keywords.The))
        {
            throw _cursor.Error(
                $"A record does not change, so its field cannot be set. Make a new one instead: {Keywords.Set} <name> {Keywords.Copy} <name> {Keywords.With} <field> <value> {Keywords.End}.");
        }

        var name = _cursor.ExpectIdentifier("variable name", _names);
        return new SetStatement(name, ParseValueFor(start), start.Position);
    }

    private bool EndsValues(Token token) =>
        token.Kind == TokenKind.EndOfInput || (token.Kind == TokenKind.Word && Keywords.IsStatement(token.Text));

    private static string WhatComesInstead(Token found) =>
        found.Kind == TokenKind.EndOfInput ? "the input ends there"
        : found.IsWord(Keywords.End) || found.IsWord(Keywords.Else) || found.IsWord(Keywords.When) ? $"{found.Describe()} on line {found.Position.Line} comes next"
        : $"{found.Describe()} on line {found.Position.Line} begins a new statement";

    private Expression ParseValueFor(Token owner)
    {
        if (EndsValues(_cursor.Current))
        {
            throw _cursor.ErrorEndingEarly($"'{owner.Text}' needs a value, but {WhatComesInstead(_cursor.Current)}.", owner);
        }

        return ParseExpression();
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

        _boundNames.Add(name);
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

    private Statement ParseMatch(IReadOnlySet<string> declared, OuterNames outer)
    {
        var start = _cursor.Advance();
        var value = ParseExpression();
        var branches = new List<MatchBranch>();
        while (!_cursor.Current.IsWord(Keywords.End))
        {
            if (_cursor.IsAtEnd)
            {
                throw _cursor.Error($"Expected '{Keywords.End}' to close the {Keywords.Match} started at {start.Position.Spoken}.");
            }

            if (!_cursor.Current.IsWord(Keywords.When))
            {
                throw _cursor.Error(
                    $"Expected '{Keywords.When}' or '{Keywords.End}' to close the {Keywords.Match} started at {start.Position.Spoken}, found {_cursor.Current.Describe()}.");
            }

            branches.Add(ParseWhen(declared, outer));
        }

        if (branches.Count == 0)
        {
            throw _cursor.Error(
                $"'{Keywords.Match}' needs a branch for each variant before '{Keywords.End}', such as {Keywords.When} <variant> <statement>.");
        }

        _cursor.Advance();
        return new MatchStatement(value, branches, start.Position);
    }

    private MatchBranch ParseWhen(IReadOnlySet<string> declared, OuterNames outer)
    {
        var start = _cursor.Advance();
        var (variant, module) = ReadMatchedVariant();
        var names = new List<string>();
        for (var index = 0; index < variant.Arity; index++)
        {
            var nameToken = _cursor.Current;
            if (nameToken.Kind != TokenKind.Word || Keywords.IsStatement(nameToken.Text))
            {
                var data = variant.Arity == 1 ? "a value" : $"{variant.Arity} values";
                throw _cursor.Error(
                    $"The variant '{variant.Name}' carries {data} ({string.Join(" ", variant.Data.Select(type => type.Name))}), " +
                    $"so '{Keywords.When} {variant.Name}' needs {(variant.Arity == 1 ? "a name for it" : "a name for each")}, " +
                    $"as in {Keywords.When} {variant.Name} {string.Join(" ", Enumerable.Range(1, variant.Arity).Select(number => variant.Arity == 1 ? "value" : $"value{number}"))} …; found {nameToken.Describe()}.");
            }

            var name = _cursor.ExpectIdentifier($"{Keywords.When} value name", _names);
            if (names.Contains(name))
            {
                throw _cursor.Error($"The {Keywords.When} value '{name}' is named twice.", nameToken);
            }

            if (declared.Contains(name))
            {
                throw _cursor.Error(
                    $"The {Keywords.When} value '{name}' would shadow the variable of the same name declared earlier in this block.",
                    nameToken);
            }

            if (outer.GuardsBoundNames && outer.Names.Contains(name))
            {
                throw _cursor.Error($"The {Keywords.When} value '{name}' would shadow the {outer.Kind} of the same name.", nameToken);
            }

            names.Add(name);
            _boundNames.Add(name);
        }

        var body = ParseBody(
            $"a {Keywords.When}", new OuterNames(names.ToHashSet(), $"{Keywords.When} value", GuardsBoundNames: false));
        return new MatchBranch(variant, names, body, start.Position, module);
    }

    private (ChoiceVariant Variant, string? Module) ReadMatchedVariant()
    {
        var token = _cursor.Current;
        if (token.IsWord(Keywords.From))
        {
            var qualified = QualifiedNames.Read(_cursor);
            return QualifiedNames.Resolve(_names, qualified, _cursor) is ChoiceVariant found
                ? (found, qualified.Module.Text)
                : throw _cursor.Error($"Expected a variant after '{Keywords.When}', found '{qualified}'.", qualified.From);
        }

        if (token.Kind == TokenKind.Word && _names.FindVariant(token.Text) is { } variant)
        {
            _cursor.Advance();
            return (variant, null);
        }

        if (token.Kind == TokenKind.Word && _names.Failures.OfName(token.Text) is { } failure)
        {
            throw failure;
        }

        throw _cursor.Error(
            token.Kind == TokenKind.Word && _names.FindChoice(token.Text) is { } choice
                ? $"'{choice.Name}' is a choice; '{Keywords.When}' names one of its variants, such as {Keywords.When} {choice.Variants[0].Example}."
                : token.Kind == TokenKind.Word && _names.LibraryHint(token.Text) is { } hint ? hint
                : $"Expected a variant after '{Keywords.When}', found {token.Describe()}.{(token.Kind == TokenKind.Word ? DidYouMean(token.Text, _names.Variants.Keys) : "")}");
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

        _boundNames.Add(variable);

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
            throw _cursor.Error(
                "Functions can only be defined at the top level." +
                (_openBlock is { } block ? $" The block started at {block.Position.Spoken} may be missing its '{Keywords.End}'." : ""),
                start);
        }

        var failure = _names.Failures.OfStatement(start);
        FunctionSignature header;
        try
        {
            header = SignatureScanner.ReadHeader(_cursor, _names, isExported: false);
        }
        catch (PyropException) when (failure is not null)
        {
            throw failure;
        }

        var signature = failure is not null
            ? header
            : _names.Functions.Find(header.Name)
                ?? throw new InvalidOperationException($"Function '{header.Name}' was not registered by the signature pre-pass.");

        var outerLoopDepth = _loopDepth;
        _currentFunction = signature;
        _loopDepth = 0;
        try
        {
            var parameters = signature.Parameters.Select(parameter => parameter.Name).ToHashSet();
            _boundNames.UnionWith(parameters);
            if (_cursor.Behind(1)?.Position.Line == _cursor.Current.Position.Line && IsUnknownWord(_cursor.Current))
            {
                throw UntypedParameter(_cursor.Current);
            }

            var body = ParseBody("a function", new OuterNames(parameters, "parameter", GuardsBoundNames: true));
            return failure is null ? new FunctionStatement(signature, body, start.Position) : throw failure;
        }
        catch (PyropException error) when (failure is not null && !error.IsIncompleteInput && !ReferenceEquals(error, failure))
        {
            throw failure;
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

        return new ReturnStatement(ParseValueFor(start), start.Position);
    }

    private Statement ParseWrite(bool appendNewLine, bool toStandardError)
    {
        var start = _cursor.Advance();
        return new WriteStatement(ParseValueFor(start), appendNewLine, toStandardError, start.Position);
    }

    private Statement ParseExit()
    {
        var start = _cursor.Advance();
        return new ExitStatement(ParseValueFor(start), start.Position);
    }

    private Statement ParseStyled()
    {
        var start = _cursor.Advance();
        var style = ParseExpression();
        return new StyledStatement(style, ParseBody("a styled"), start.Position);
    }

    private Statement ParseNestedBlock()
    {
        RequireRoomToNest(_nesting);
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
        var enclosing = _openBlock;
        _openBlock = start;
        try
        {
            while (!_cursor.Current.IsWord(Keywords.End))
            {
                if (_cursor.IsAtEnd)
                {
                    throw _cursor.Error($"Expected 'end' to close the block started at {start.Position.Spoken}.");
                }

                var statement = ParseListedStatement(declared, outer);
                statements.Add(statement);
                if (statement is ReturnStatement or BreakStatement or ContinueStatement
                    && !_cursor.Current.IsWord(Keywords.End)
                    && !_cursor.IsAtEnd)
                {
                    var word = statement switch
                    {
                        ReturnStatement => Keywords.Return,
                        BreakStatement => Keywords.Break,
                        _ => Keywords.Continue,
                    };
                    var unreachable = _cursor.Current;
                    ParseListedStatement(declared, outer);
                    throw _cursor.Error(
                        $"This code can never run, because the '{word}' on line {statement.Position.Line} leaves the block before it. " +
                        $"Remove it, or move it above the '{word}'.",
                        unreachable);
                }
            }
        }
        finally
        {
            _openBlock = enclosing;
        }

        _cursor.Advance();
        return new BlockStatement(statements, start.Position);
    }

    private Expression ParseExpression()
    {
        RequireRoomToNest(_expressionDepth);
        _expressionDepth++;
        try
        {
            return ReadExpression();
        }
        finally
        {
            _expressionDepth--;
        }
    }

    private Expression ReadExpression()
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
            case Keywords.VariantsOf:
                _cursor.Advance();
                return new VariantsOfExpression(TypeReader.ReadValueType(_cursor, _names, "listed choice"), token.Position);
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
            case Keywords.The:
                _cursor.Advance();
                var read = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.The}'");
                ExpectFiller(Keywords.Of, $"after the field name: {Keywords.The} {read.Text} {Keywords.Of} <record>");
                return new GetFieldExpression(read.Text, read.Position, ParseExpression(), token.Position);
            case Keywords.Copy:
                return ParseCopy();
        }

        if (token.IsWord(Keywords.From))
        {
            return ParseQualified(allowVoid: false, asStatement: false);
        }

        if (_names.FindOperation(token.Text) is not null || _names.FindCallable(token.Text) is not null)
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
            return new VariantExpression(variant, ParseArguments(variant.Arity, variant.Name, token), token.Position);
        }

        if (_names.FindChoice(token.Text) is { } choice)
        {
            throw ChoiceAsValue(token, choice);
        }

        if ((Keywords.IsReservedForLater(token.Text) || IsUnknownWord(token)) && Keywords.UnavailableHint(token.Text) is { } unavailable)
        {
            throw _cursor.Error(unavailable);
        }

        if (Keywords.IsReserved(token.Text))
        {
            throw _cursor.Error($"Expected an expression, found keyword '{token.Text}'.");
        }

        if (_names.Failures.OfName(token.Text) is { } failure)
        {
            throw failure;
        }

        _cursor.Advance();
        return new VariableExpression(token.Text, token.Position);
    }

    private Expression ParseCopy()
    {
        var start = _cursor.Advance();
        var record = ParseExpression();
        ExpectFiller(Keywords.With, $"after the record: {Keywords.Copy} <record> {Keywords.With} <field> <value> {Keywords.End}");
        var changed = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.With}'");
        var named = new HashSet<string> { changed.Text };
        Expression copy = new WithFieldExpression(changed.Text, changed.Position, record, ParseExpression(), start.Position);
        var depthBeforeChain = _expressionDepth;
        try
        {
            while (!_cursor.Current.IsWord(Keywords.End))
            {
                if (!_cursor.Current.IsWord(Keywords.With))
                {
                    throw _cursor.Error(
                        $"Expected '{Keywords.With}' or '{Keywords.End}' to close the {Keywords.Copy} started at {start.Position.Spoken}, found {_cursor.Current.Describe()}.");
                }

                RequireRoomToNest(_expressionDepth++);
                _cursor.Advance();
                var next = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.With}'");
                if (!named.Add(next.Text))
                {
                    throw _cursor.Error($"Field '{next.Text}' is changed twice in the {Keywords.Copy} started at {start.Position.Spoken}.", next);
                }

                copy = new WithFieldExpression(next.Text, next.Position, copy, ParseExpression(), start.Position, Chained: true);
            }
        }
        finally
        {
            _expressionDepth = depthBeforeChain;
        }

        _cursor.Advance();
        return copy;
    }

    private void ExpectFiller(string word, string where)
    {
        if (!_cursor.Current.IsWord(word))
        {
            throw _cursor.Error($"Expected '{word}' {where}, found {_cursor.Current.Describe()}.");
        }

        _cursor.Advance();
    }

    private Expression ParseAttempt()
    {
        var start = _cursor.Advance();
        var operand = _cursor.Current;
        if (operand.IsWord(Keywords.Convert))
        {
            return new AttemptExpression(ParseExpression(), start.Position);
        }

        if (operand.Kind == TokenKind.Word && _names.FindOperation(operand.Text) is not null)
        {
            return new AttemptExpression(ParseCall(allowVoid: true), start.Position);
        }

        if (operand.IsWord(Keywords.From))
        {
            var qualifiedStart = _cursor.Index;
            if (QualifiedNames.Resolve(_names, QualifiedNames.Read(_cursor), _cursor) is BuiltinOperation)
            {
                _cursor.MoveTo(qualifiedStart);
                return new AttemptExpression(ParseQualified(allowVoid: true, asStatement: false), start.Position);
            }

            _cursor.MoveTo(qualifiedStart);
        }

        if (operand.Kind == TokenKind.Word && (Keywords.IsReservedForLater(operand.Text) || IsUnknownWord(operand)) && Keywords.UnavailableHint(operand.Text) is { } unavailable)
        {
            throw _cursor.Error(unavailable);
        }

        var qualifiedFunction = operand.IsWord(Keywords.From) && _names.Uses.TryGetValue(_cursor.Peek(1).Text, out var use)
            && use.Target.Names.Functions.Find(_cursor.Peek(2).Text) is not null;
        var hint = operand.Kind == TokenKind.Word && _names.FindCallable(operand.Text) is null && _names.LibraryHint(operand.Text) is { } found
            ? $" {found}"
            : "";
        var described = operand.Kind == TokenKind.Word && _names.FindCallable(operand.Text) is not null
            ? $"the function '{operand.Text}'; a function's own failures are runtime errors, so that a bug in it is never mistaken for bad input"
            : qualifiedFunction
            ? $"the function '{_cursor.Peek(2).Text}' of module '{_cursor.Peek(1).Text}'; a function's own failures are runtime errors, so that a bug in it is never mistaken for bad input"
            : operand.IsWord(Keywords.From)
            ? $"the qualified name starting at '{Keywords.From}'; only built-in operations, including a library's, can follow '{Keywords.Attempt}'"
            : operand.Describe();
        throw _cursor.Error(
            $"'{Keywords.Attempt}' takes a built-in operation or a convert, such as {Keywords.Attempt} {Keywords.Convert} int read " +
            $"or {Keywords.Attempt} {Keywords.From} files load \"notes.txt\"; found {described}.{hint}",
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
                    $"The last key of the {Keywords.MapOf} started at {start.Position.Spoken} has no value; every key is followed by its value.");
            }

            if (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text))
            {
                throw _cursor.Error(
                    $"Expected the value of the last key of the {Keywords.MapOf} started at {start.Position.Spoken}, found '{_cursor.Current.Text}'.");
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
            throw _cursor.Error($"Expected '{Keywords.End}' to close the {start.Text} started at {start.Position.Spoken}.");
        }

        if (_cursor.Current.Kind == TokenKind.Word && Keywords.IsStatement(_cursor.Current.Text))
        {
            throw _cursor.Error(
                $"Expected {expected} or '{Keywords.End}' to close the {start.Text} started at {start.Position.Spoken}, found '{_cursor.Current.Text}'.");
        }

        return false;
    }

    private Expression ParseCall(bool allowVoid)
    {
        var token = _cursor.Advance();
        if (_names.FindOperation(token.Text) is { } operation)
        {
            return ParseOperationCall(operation, token, null, allowVoid);
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
            ChoiceVariant { Arity: > 0 } when asStatement => throw VariantStatement(qualified.From, qualified.ToString()),
            ChoiceVariant when asStatement => throw StrayValue(qualified.From, $"'{qualified}'"),
            ChoiceVariant variant => new VariantExpression(
                variant, ParseArguments(variant.Arity, qualified.ToString(), qualified.From), qualified.From.Position, qualified.Module.Text),
            FunctionSignature function => ParseFunctionCall(function, qualified.From, qualified.Module.Text, allowVoid),
            BuiltinOperation operation => ParseOperationCall(operation, qualified.From, qualified.Module.Text, allowVoid),
            var other => throw new InvalidOperationException($"Unknown qualified target {other.GetType().Name}."),
        };
    }

    private Expression ParseOperationCall(BuiltinOperation operation, Token start, string? module, bool allowVoid)
    {
        if (operation.ReturnsVoid && !allowVoid)
        {
            throw _cursor.Error($"Operation '{operation.Name}' returns void and cannot be used as a value.", start);
        }

        return new BuiltinCallExpression(operation, ParseArguments(operation.Arity, operation.Name, start), start.Position, module);
    }

    private Expression ParseConstruct(RecordType record, Token start, string? module) =>
        _cursor.Current.IsWord(Keywords.With)
            ? ParseLabelledConstruct(record, start, module)
            : new ConstructExpression(record, ParseArguments(record.Fields.Count, record.Name, start), start.Position, module);

    private Expression ParseLabelledConstruct(RecordType record, Token start, string? module)
    {
        var labels = new List<FieldLabel>();
        var values = new List<Expression>();
        while (labels.Count < record.Fields.Count)
        {
            if (!_cursor.Current.IsWord(Keywords.With))
            {
                throw MissingLabels(record, labels);
            }

            _cursor.Advance();
            var name = RecordDeclarations.ReadFieldName(_cursor, $"a field name after '{Keywords.With}'");
            var index = record.Fields.ToList().FindIndex(field => field.Name == name.Text);
            if (index < 0)
            {
                throw _cursor.Error($"'{record.Name}' has no field '{name.Text}'; its fields are {string.Join(", ", record.Fields.Select(field => field.Name))}.", name);
            }

            if (labels.Any(label => label.FieldIndex == index))
            {
                throw _cursor.Error($"Field '{name.Text}' of '{record.Name}' is given twice.", name);
            }

            labels.Add(new FieldLabel(name.Text, index, name.Position));
            values.Add(ParseExpression());
        }

        return new ConstructExpression(record, values, start.Position, module, labels);
    }

    private PyropException MissingLabels(RecordType record, List<FieldLabel> labels)
    {
        var missing = record.Fields.Where((_, index) => labels.All(label => label.FieldIndex != index)).Select(field => field.Name);
        return _cursor.Error(
            $"'{record.Name}' still needs {string.Join(", ", missing)}: a labelled constructor gives every field with {Keywords.With} <field> <value>, found {_cursor.Current.Describe()}.");
    }

    private Expression ParseFunctionCall(FunctionSignature function, Token start, string? module, bool allowVoid)
    {
        if (function.ReturnType == PyropType.Void && !allowVoid)
        {
            throw _cursor.Error($"Function '{function.Name}' returns void and cannot be used as a value.", start);
        }

        return new FunctionCallExpression(function, ParseArguments(function.Arity, function.Name, start), start.Position, module);
    }

    private IReadOnlyList<Expression> ParseArguments(int count, string name, Token start)
    {
        var arguments = new Expression[count];
        for (var index = 0; index < count; index++)
        {
            RequireArgument(name, count, index, start);
            arguments[index] = ParseExpression();
        }

        return arguments;
    }

    private void RequireArgument(string name, int count, int given, Token start)
    {
        var found = _cursor.Current;
        if (!EndsValues(found) && (found.Position.Line == start.Position.Line || !IsVoidCall(found)))
        {
            return;
        }

        var needed = count == 1 ? "1 value" : $"{count} values";
        var has = given == 0 ? "none" : $"only {given}";
        throw _cursor.ErrorEndingEarly(
            $"'{name}' needs {needed} but has {has}: it starts at {start.Position.Spoken}, and {WhatComesInstead(found)}.", start);
    }

    private bool IsVoidCall(Token token) =>
        token.Kind == TokenKind.Word
        && (_names.FindOperation(token.Text) is { ReturnsVoid: true } || _names.FindCallable(token.Text)?.ReturnType == PyropType.Void);

    private sealed record OuterNames(IReadOnlySet<string> Names, string Kind, bool GuardsBoundNames);
}
