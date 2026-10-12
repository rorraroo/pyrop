using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class TypeReader
{
    public static bool StartsValueType(Token token) =>
        token.Kind == TokenKind.Word && PyropTypes.ValueConstructors.ContainsKey(token.Text);

    public static PyropType ReadValueType(TokenCursor cursor, string owner)
    {
        var token = cursor.ExpectWord("a type");
        if (token.IsWord(PyropType.Void.Name))
        {
            throw cursor.Error($"A {owner} cannot have type void.", token);
        }

        if (!PyropTypes.ValueConstructors.TryGetValue(token.Text, out var constructor))
        {
            throw cursor.Error($"Expected a type ({PyropTypes.DescribeValueTypes("or")}), found {token.Describe()}.", token);
        }

        var arguments = new PyropType[constructor.Arity];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = ReadValueType(cursor, "type argument");
        }

        return new ConstructedType(constructor, arguments);
    }

    public static PyropType ReadReturnType(TokenCursor cursor)
    {
        if (cursor.Current.IsWord(PyropType.Void.Name))
        {
            cursor.Advance();
            return PyropType.Void;
        }

        if (!StartsValueType(cursor.Current))
        {
            var token = cursor.ExpectWord("a return type");
            var names = string.Join(", ", PyropTypes.ValueTypes.Select(type => type.Name));
            throw cursor.Error($"Expected a return type ({names} or {PyropType.Void.Name}), found {token.Describe()}.", token);
        }

        return ReadValueType(cursor, "return type");
    }
}
