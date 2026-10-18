using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class TypeReader
{
    public static bool StartsValueType(TokenCursor cursor, ModuleNamespace names)
    {
        var token = cursor.Current;
        return token.Kind == TokenKind.Word
            && (PyropTypes.ValueConstructors.ContainsKey(token.Text)
                || names.FindType(token.Text) is not null
                || (RecordDeclarations.IsTypeName(token.Text) && names.Failures.OfName(token.Text) is not null)
                || QualifiedNames.NamesType(names, cursor));
    }

    public static PyropType ReadValueType(TokenCursor cursor, ModuleNamespace names, string owner)
    {
        if (cursor.Current.IsWord(Keywords.From))
        {
            return QualifiedNames.ResolveType(names, QualifiedNames.Read(cursor), cursor);
        }

        var token = cursor.ExpectWord("a type");
        if (token.IsWord(PyropType.Void.Name))
        {
            throw cursor.Error($"A {owner} cannot have type void.", token);
        }

        if (names.FindType(token.Text) is { } declared)
        {
            return declared;
        }

        if (!PyropTypes.ValueConstructors.TryGetValue(token.Text, out var constructor))
        {
            throw names.Failures.OfName(token.Text) ?? cursor.Error(
                names.FindVariant(token.Text) is { } variant
                    ? $"'{token.Text}' is a variant of the choice '{variant.Choice.Name}', not a type."
                    : RecordDeclarations.IsTypeName(token.Text)
                    ? $"'{token.Text}' is not a type; no record or choice named '{token.Text}' is declared or imported in this file."
                    : $"Expected a type ({string.Join(", ", PyropTypes.ValueConstructors.Keys)}, a record or choice name), found {token.Describe()}.",
                token);
        }

        var arguments = new PyropType[constructor.Arity];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argumentToken = cursor.Current;
            arguments[index] = ReadValueType(cursor, names, "type argument");
            if (constructor == PyropTypes.Map && index == 0 && !PyropTypes.IsMapKey(arguments[index]))
            {
                throw cursor.Error(
                    $"A map key must be {PyropTypes.DescribeMapKeyTypes()}, found {arguments[index].Name}.", argumentToken);
            }
        }

        return new ConstructedType(constructor, arguments);
    }

    public static PyropType ReadCollectionType(TokenCursor cursor, ModuleNamespace names, string owner)
    {
        var start = cursor.Current;
        var type = ReadValueType(cursor, names, "collection");
        if (!PyropTypes.IsCollection(type))
        {
            throw cursor.Error(
                $"'{owner}' takes a list or map type, such as {owner} list string or {owner} map string int, found {type.Name}.", start);
        }

        return type;
    }

    public static PyropType ReadReturnType(TokenCursor cursor, ModuleNamespace names)
    {
        if (cursor.Current.IsWord(PyropType.Void.Name))
        {
            cursor.Advance();
            return PyropType.Void;
        }

        if (!StartsValueType(cursor, names) && !cursor.Current.IsWord(Keywords.From))
        {
            var token = cursor.ExpectWord("a return type");
            if (RecordDeclarations.IsTypeName(token.Text))
            {
                throw names.Failures.OfName(token.Text) ?? cursor.Error(
                    $"'{token.Text}' is not a type; no record or choice named '{token.Text}' is declared or imported in this file.", token);
            }

            var types = string.Join(", ", PyropTypes.ValueConstructors.Keys);
            throw cursor.Error($"Expected a return type ({types}, a record or choice name, or {PyropType.Void.Name}), found {token.Describe()}.", token);
        }

        return ReadValueType(cursor, names, "return type");
    }
}
