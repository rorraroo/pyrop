using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class RecordDeclarations
{
    private static readonly HashSet<string> NamePositions =
        [Keywords.Def, Keywords.Set, Keywords.For, Keywords.Use, Keywords.From, Keywords.GetField, Keywords.WithField];

    public static bool IsTypeName(string name) => name.Length > 0 && name[0] is >= 'A' and <= 'Z';

    private static readonly HashSet<string> ValuePositions =
    [
        Keywords.Write, Keywords.WriteLine, Keywords.WriteError, Keywords.WriteErrorLine, Keywords.Return, Keywords.Exit,
        Keywords.If, Keywords.While, Keywords.WithStyle, Keywords.In, Keywords.Convert, Keywords.Empty, Keywords.ListOf,
        Keywords.Attempt, Keywords.None, Keywords.IfSome,
    ];

    private static readonly HashSet<string> TypedNamePositions = [Keywords.Def, Keywords.For, Keywords.IfSome, Keywords.Convert, Keywords.ListOf];

    public static void RejectKeywordAsName(Token keyword, TokenCursor cursor, bool startsDeclaration)
    {
        if (!startsDeclaration || FollowsWordExpectingNameOrValue(cursor))
        {
            throw new PyropException(
                ErrorStage.Parse, $"'{keyword.Text}' is a reserved word and cannot be used as a name.", keyword.Position);
        }
    }

    private static bool FollowsWordExpectingNameOrValue(TokenCursor cursor)
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

        if (!IsTypeWord(previous.Text))
        {
            return false;
        }

        var head = 2;
        while (true)
        {
            if (IsTypeName(cursor.Behind(head)!.Text) && cursor.Behind(head + 2) is { } from && from.IsWord(Keywords.From))
            {
                head += 2;
            }

            if (cursor.Behind(head + 1) is not { Kind: TokenKind.Word } earlier || !IsTypeWord(earlier.Text))
            {
                break;
            }

            head++;
        }

        if (cursor.Behind(head + 1) is { Kind: TokenKind.Word } preceding)
        {
            if (TypedNamePositions.Contains(preceding.Text))
            {
                return true;
            }

            if (preceding.IsWord(Keywords.Empty) || preceding.IsWord(Keywords.None))
            {
                return false;
            }
        }

        return !IsTypeName(previous.Text);
    }

    private static bool IsTypeWord(string word) =>
        PyropTypes.ValueConstructors.ContainsKey(word) || (IsTypeName(word) && !Keywords.IsReserved(word));

    public static bool CanStartRecord(TokenCursor cursor)
    {
        var name = cursor.Current;
        if (name.Kind != TokenKind.Word || Keywords.IsReserved(name.Text))
        {
            return false;
        }

        var next = cursor.Peek(1);
        return IsTypeName(name.Text)
            || (next.Kind == TokenKind.Word
                && (PyropTypes.ValueConstructors.ContainsKey(next.Text) || IsTypeName(next.Text) || next.IsWord(PyropType.Void.Name)));
    }

    public static Token ReadName(TokenCursor cursor, string keyword = Keywords.Record, string example = "Point")
    {
        var token = cursor.ExpectWord($"a {keyword} name after '{keyword}'");
        if (Keywords.IsReserved(token.Text))
        {
            throw cursor.Error($"'{token.Text}' is a reserved word and cannot be used as a {keyword} name.", token);
        }

        if (!IsTypeName(token.Text))
        {
            throw cursor.Error(
                $"A {keyword} name must start with a capital letter A to Z, so that types stand out: {keyword} {example} …, found '{token.Text}'.",
                token);
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

    public static void CheckSelfContainment(IEnumerable<RecordType> records)
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

    public static void CheckExposure(Module module, IEnumerable<RecordType> records, IEnumerable<FunctionSignature> functions)
    {
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
