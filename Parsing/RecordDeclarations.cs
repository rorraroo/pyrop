using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public sealed record KnownTypes(ModuleNamespace Names, IReadOnlySet<string> DeclaredHere)
{
    public static KnownTypes Of(ModuleNamespace names) => new(names, new HashSet<string>());

    public static IReadOnlySet<string> DeclaredIn(IReadOnlyList<Token> tokens)
    {
        var declared = new HashSet<string>();
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if ((tokens[index].IsWord(Keywords.Record) || tokens[index].IsWord(Keywords.Choice))
                && tokens[index + 1].Kind == TokenKind.Word
                && !Keywords.IsReserved(tokens[index + 1].Text))
            {
                declared.Add(tokens[index + 1].Text);
            }
        }

        return declared;
    }

    public bool IsTypeName(string word) =>
        !Keywords.IsReserved(word) && (DeclaredHere.Contains(word) || Names.FindType(word) is not null);

    public bool IsTypeWord(string word) => PyropTypes.ValueConstructors.ContainsKey(word) || IsTypeName(word);

    public bool IsQualifiedType(string module, string name) =>
        Names.Uses.TryGetValue(module, out var use)
        && (use.Target.Names.Records.ContainsKey(name) || use.Target.Names.Choices.ContainsKey(name));
}

public static class RecordDeclarations
{
    private static readonly HashSet<string> NamePositions =
        [Keywords.Def, Keywords.Set, Keywords.For, Keywords.Use, Keywords.From, Keywords.The, Keywords.With, Keywords.When];

    private static readonly HashSet<string> ValuePositions =
    [
        Keywords.Write, Keywords.WriteLine, Keywords.WriteError, Keywords.WriteErrorLine, Keywords.Return, Keywords.Exit,
        Keywords.If, Keywords.While, Keywords.WithStyle, Keywords.In, Keywords.Convert, Keywords.ListOf, Keywords.MapOf,
        Keywords.Attempt, Keywords.None, Keywords.IfSome, Keywords.Match, Keywords.Of, Keywords.Copy,
    ];

    public static void RejectKeywordAsName(Token keyword, TokenCursor cursor, bool startsDeclaration, KnownTypes types)
    {
        if (!startsDeclaration || FollowsWordExpectingNameOrValue(cursor, types))
        {
            throw new PyropException(
                ErrorStage.Parse, $"'{keyword.Text}' is a reserved word and cannot be used as a name.", keyword.Position);
        }
    }

    private static bool FollowsWordExpectingNameOrValue(TokenCursor cursor, KnownTypes types)
    {
        if (cursor.Behind(2) is not { Kind: TokenKind.Word } previous)
        {
            return false;
        }

        if (NamePositions.Contains(previous.Text)
            || ValuePositions.Contains(previous.Text)
            || (Builtins.All.TryGetValue(previous.Text, out var operation) && operation.Arity > 0))
        {
            return true;
        }

        if (FollowsTypeInHeader(cursor, types) is { } inHeader)
        {
            return inHeader;
        }

        var head = 2;
        var length = TypeWordLength(cursor, head, types);
        if (length == 0)
        {
            return false;
        }

        while (length > 0)
        {
            head += length;
            length = TypeWordLength(cursor, head, types);
        }

        return cursor.Behind(head) is not { Kind: TokenKind.Word } preceding
            || !(preceding.IsWord(Keywords.None) || preceding.IsWord(Keywords.Import));
    }

    private static bool? FollowsTypeInHeader(TokenCursor cursor, KnownTypes types)
    {
        var offset = 2;
        while (cursor.Behind(offset) is { Kind: TokenKind.Word } word)
        {
            if (word.IsWord(Keywords.Import))
            {
                return false;
            }

            if (word.IsWord(Keywords.Record) || word.IsWord(Keywords.Function))
            {
                return HeaderEndsInType(cursor, offset - 1, types, word.IsWord(Keywords.Function));
            }

            if (Keywords.IsReserved(word.Text)
                && !PyropTypes.ValueConstructors.ContainsKey(word.Text)
                && !word.IsWord(Keywords.From)
                && !word.IsWord(PyropType.Void.Name))
            {
                return null;
            }

            offset++;
        }

        return null;
    }

    private static bool? HeaderEndsInType(TokenCursor cursor, int offset, KnownTypes types, bool isFunction)
    {
        if (isFunction)
        {
            var returned = cursor.Behind(offset) is { } first && first.IsWord(PyropType.Void.Name)
                ? 1
                : ForwardTypeLength(cursor, offset, types);
            if (returned == 0)
            {
                return null;
            }

            offset -= returned;
        }

        offset--;
        while (offset > 1)
        {
            var length = ForwardTypeLength(cursor, offset, types);
            if (length == 0)
            {
                return null;
            }

            offset -= length;
            if (offset <= 1)
            {
                return true;
            }

            offset--;
        }

        return offset == 1 ? false : null;
    }

    private static int ForwardTypeLength(TokenCursor cursor, int offset, KnownTypes types)
    {
        if (offset <= 1 || cursor.Behind(offset) is not { Kind: TokenKind.Word } word)
        {
            return 0;
        }

        if (word.IsWord(Keywords.From))
        {
            return offset > 3
                && cursor.Behind(offset - 1) is { Kind: TokenKind.Word } module
                && cursor.Behind(offset - 2) is { Kind: TokenKind.Word } name
                && types.IsQualifiedType(module.Text, name.Text)
                    ? 3
                    : 0;
        }

        if (PyropTypes.ValueConstructors.TryGetValue(word.Text, out var constructor))
        {
            var length = 1;
            for (var argument = 0; argument < constructor.Arity; argument++)
            {
                var inner = ForwardTypeLength(cursor, offset - length, types);
                if (inner == 0)
                {
                    return 0;
                }

                length += inner;
            }

            return length;
        }

        return types.IsTypeName(word.Text) ? 1 : 0;
    }

    private static int TypeWordLength(TokenCursor cursor, int offset, KnownTypes types)
    {
        if (cursor.Behind(offset) is not { Kind: TokenKind.Word } word)
        {
            return 0;
        }

        if (cursor.Behind(offset + 2) is { } from
            && from.IsWord(Keywords.From)
            && cursor.Behind(offset + 1) is { Kind: TokenKind.Word } module
            && types.IsQualifiedType(module.Text, word.Text))
        {
            return 3;
        }

        return types.IsTypeWord(word.Text) ? 1 : 0;
    }

    public static bool CanStartRecord(TokenCursor cursor, KnownTypes types)
    {
        var name = cursor.Current;
        if (name.Kind != TokenKind.Word || Keywords.IsReserved(name.Text))
        {
            return false;
        }

        var next = cursor.Peek(1);
        var afterNext = cursor.Peek(2);
        return next.Kind == TokenKind.EndOfInput
            || (next.Kind == TokenKind.Word
                && (Keywords.IsReserved(next.Text)
                    || types.IsTypeName(next.Text)
                    || types.Names.Failures.OfName(next.Text) is not null
                    || (afterNext.Kind == TokenKind.Word && !Keywords.IsReserved(afterNext.Text))));
    }

    public static Token ReadName(TokenCursor cursor, string keyword = Keywords.Record)
    {
        var token = cursor.ExpectWord($"a {keyword} name after '{keyword}'");
        if (Keywords.IsReserved(token.Text))
        {
            throw cursor.Error($"'{token.Text}' is a reserved word and cannot be used as a {keyword} name.", token);
        }

        return token;
    }

    public static IReadOnlyList<RecordField> ReadFields(TokenCursor cursor, ModuleNamespace names, string record)
    {
        var fields = new List<RecordField>();
        while (true)
        {
            if (cursor.Current.IsWord(PyropType.Void.Name))
            {
                throw cursor.Error("A field cannot have type void.");
            }

            if (!TypeReader.StartsValueType(cursor, names))
            {
                if (fields.Count == 0
                    && cursor.Current.Kind == TokenKind.Word
                    && !Keywords.IsReserved(cursor.Current.Text)
                    && cursor.Peek(1).Kind == TokenKind.Word
                    && !Keywords.IsReserved(cursor.Peek(1).Text))
                {
                    TypeReader.ReadValueType(cursor, names, "field");
                }

                break;
            }

            var type = TypeReader.ReadValueType(cursor, names, "field");
            ExpectNameAfterRecordType(cursor, type, "field");
            var nameToken = ReadFieldName(cursor, $"a field name after the type {type.Name}");
            if (fields.Any(field => field.Name == nameToken.Text))
            {
                throw cursor.Error($"Field '{nameToken.Text}' is declared twice in record '{record}'.", nameToken);
            }

            fields.Add(new RecordField(type, nameToken.Text, nameToken.Position));
        }

        if (fields.Count == 0)
        {
            throw cursor.Error(
                $"Record '{record}' needs at least one field, a type and a name such as {Keywords.Record} {record} int count; found {cursor.Current.Describe()}.");
        }

        return fields;
    }

    public static void ExpectNameAfterRecordType(TokenCursor cursor, PyropType type, string kind)
    {
        if (type is RecordType && cursor.Current.Kind is not (TokenKind.Word or TokenKind.EndOfInput))
        {
            throw cursor.Error(
                $"Expected a {kind} name after the type {type.Name}, found {cursor.Current.Describe()}. " +
                $"No statement can start with a record name, so '{type.Name}' was read as the type of one more {kind}; " +
                "a record constructor cannot be a statement.");
        }
    }

    public static Token ReadFieldName(TokenCursor cursor, string expectation)
    {
        var token = cursor.ExpectWord(expectation);
        if (Keywords.IsReserved(token.Text))
        {
            throw cursor.Error($"'{token.Text}' is a reserved word and cannot be used as a field name.", token);
        }

        return token;
    }

    public static void CheckSelfContainment(IReadOnlyList<RecordType> records, IReadOnlyList<ChoiceType> choices)
    {
        CheckRecordChains(records);
        var pending = new HashSet<DeclaredType>([.. records, .. choices]);
        bool CanMake(PyropType type) => type is not DeclaredType declared || !pending.Contains(declared);
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var type in pending.ToList())
            {
                var makeable = type switch
                {
                    RecordType record => record.Fields.All(field => CanMake(field.Type)),
                    ChoiceType choice => choice.Variants.Any(variant => variant.Data.All(CanMake)),
                    _ => true,
                };
                if (makeable)
                {
                    pending.Remove(type);
                    progress = true;
                }
            }
        }

        if (choices.FirstOrDefault(pending.Contains) is { } unmakeable)
        {
            throw new PyropException(
                ErrorStage.Parse,
                $"Choice '{unmakeable.Name}' contains itself: every variant carries data that cannot be made without a {unmakeable.Name}, so no {unmakeable.Name} could ever be made. " +
                "Give it a variant that does not need one, such as a variant without data.",
                unmakeable.Position);
        }
    }

    private static void CheckRecordChains(IEnumerable<RecordType> records)
    {
        foreach (var record in records)
        {
            if (PathBack(record, record, [], []) is { } path)
            {
                var steps = path.Select(step => $"{step.Owner.Name} has field '{step.Field.Name}' of type {step.Field.Type.Name}").ToList();
                var described = steps.Count == 1 ? steps[0] : $"{string.Join(", ", steps.SkipLast(1))}, and {steps[^1]}";
                throw new PyropException(
                    ErrorStage.Parse,
                    $"Record '{record.Name}' contains itself: {described}, so no {record.Name} could ever be made. " +
                    $"A record can hold itself only inside a list, a map or an option, such as option {record.Name}.",
                    record.Position);
            }
        }
    }

    private static List<(RecordType Owner, RecordField Field)>? PathBack(
        RecordType target, RecordType current, List<(RecordType Owner, RecordField Field)> path, HashSet<RecordType> visited)
    {
        foreach (var field in current.Fields)
        {
            if (field.Type is not RecordType inner)
            {
                continue;
            }

            path.Add((current, field));
            if (inner == target)
            {
                return path;
            }

            if (visited.Add(inner) && PathBack(target, inner, path, visited) is { } found)
            {
                return found;
            }

            path.RemoveAt(path.Count - 1);
        }

        return null;
    }

    public static void CheckExposure(
        Module module, IEnumerable<RecordType> records, IEnumerable<ChoiceType> choices, IEnumerable<FunctionSignature> functions)
    {
        foreach (var choice in choices.Where(choice => choice.IsExported))
        {
            foreach (var variant in choice.Variants)
            {
                if (variant.Data.Select(type => Private(module, type)).FirstOrDefault(hidden => hidden is not null) is { } hidden)
                {
                    throw new PyropException(
                        ErrorStage.Parse,
                        $"Exported choice '{choice.Name}' uses the {Kind(hidden)} '{hidden.Name}' in its variant '{variant.Name}', but '{hidden.Name}' is not exported; export it too.",
                        choice.Position);
                }
            }
        }

        foreach (var record in records.Where(record => record.IsExported))
        {
            foreach (var field in record.Fields)
            {
                if (Private(module, field.Type) is { } hidden)
                {
                    throw new PyropException(
                        ErrorStage.Parse,
                        $"Exported record '{record.Name}' uses the {Kind(hidden)} '{hidden.Name}' in its field '{field.Name}', but '{hidden.Name}' is not exported; export it too.",
                        record.Position);
                }
            }
        }

        foreach (var function in functions.Where(function => function.IsExported))
        {
            var types = function.Parameters.Select(parameter => parameter.Type).Prepend(function.ReturnType);
            if (types.Select(type => Private(module, type)).FirstOrDefault(hidden => hidden is not null) is { } hidden)
            {
                throw new PyropException(
                    ErrorStage.Parse,
                    $"Exported function '{function.Name}' uses the {Kind(hidden)} '{hidden.Name}', but '{hidden.Name}' is not exported; export it too.",
                    function.Position);
            }
        }
    }

    private static string Kind(DeclaredType declared) => declared is ChoiceType ? Keywords.Choice : Keywords.Record;

    private static DeclaredType? Private(Module module, PyropType type) =>
        type.DeclaredTypes.FirstOrDefault(declared => declared.Module == module && !declared.IsExported);
}
