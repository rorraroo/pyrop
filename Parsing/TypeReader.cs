using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class TypeReader
{
    private const string TypeArgument = "type argument";

    public static bool StartsValueType(TokenCursor cursor, ModuleNamespace names)
    {
        var token = cursor.Current;
        return token.Kind == TokenKind.Word
            && (PyropTypes.ValueConstructors.ContainsKey(token.Text)
                || names.FindType(token.Text) is not null
                || names.Failures.OfName(token.Text) is not null
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
                    : LooksLikeTypeBeforeName(token, cursor) || (owner == TypeArgument && !Keywords.IsReserved(token.Text))
                    ? $"'{token.Text}' is not a type; no record or choice named '{token.Text}' is declared or imported in this file."
                    : $"Expected a type ({string.Join(", ", PyropTypes.ValueConstructors.Keys)}, a record or choice name), found {token.Describe()}.",
                token);
        }

        var arguments = new PyropType[constructor.Arity];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = constructor == PyropTypes.Map && index == 0
                ? ReadKeyType(cursor, names)
                : ReadValueType(cursor, names, TypeArgument);
        }

        return new ConstructedType(constructor, arguments);
    }

    public static PyropType ReadKeyType(TokenCursor cursor, ModuleNamespace names)
    {
        var start = cursor.Current;
        var type = ReadValueType(cursor, names, TypeArgument);
        if (type is ChoiceType { HasData: true } choice)
        {
            throw cursor.Error(
                $"The choice '{choice.Name}' cannot be a map key, because some of its variants carry data; only a choice whose variants carry none can.",
                start);
        }

        if (!PyropTypes.IsMapKey(type))
        {
            throw cursor.Error($"A map key must be {PyropTypes.DescribeMapKeyTypes()}, found {type.Name}.", start);
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
            if (names.Failures.OfName(token.Text) is not null || LooksLikeTypeBeforeName(token, cursor))
            {
                throw names.Failures.OfName(token.Text) ?? cursor.Error(
                    $"'{token.Text}' is not a type; no record or choice named '{token.Text}' is declared or imported in this file.", token);
            }

            var types = string.Join(", ", PyropTypes.ValueConstructors.Keys);
            throw cursor.Error($"Expected a return type ({types}, a record or choice name, or {PyropType.Void.Name}), found {token.Describe()}.", token);
        }

        return ReadValueType(cursor, names, "return type");
    }

    private static bool LooksLikeTypeBeforeName(Token token, TokenCursor cursor) =>
        !Keywords.IsReserved(token.Text) && cursor.Current.Kind == TokenKind.Word && !Keywords.IsReserved(cursor.Current.Text);
}
