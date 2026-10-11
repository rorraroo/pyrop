using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class SignatureScanner
{
    public static IReadOnlyList<FunctionSignature> Scan(IReadOnlyList<Token> tokens, FunctionTable existing)
    {
        var cursor = new TokenCursor(tokens);
        var signatures = new List<FunctionSignature>();
        var names = new HashSet<string>();
        while (!cursor.IsAtEnd)
        {
            if (!cursor.Current.IsWord(Keywords.Function))
            {
                cursor.Advance();
                continue;
            }

            cursor.Advance();
            var signature = ReadHeader(cursor, existing);
            if (existing.Contains(signature.Name) || !names.Add(signature.Name))
            {
                throw new PyropException(ErrorStage.Parse, $"Function '{signature.Name}' is already defined.", signature.Position);
            }

            signatures.Add(signature);
        }

        return signatures;
    }

    public static FunctionSignature ReadHeader(TokenCursor cursor, FunctionTable functions)
    {
        var returnTypeToken = cursor.ExpectWord("a return type");
        var returnType = PyropTypes.FromName(returnTypeToken.Text)
            ?? throw cursor.Error($"Expected a return type (int, float, string, bool or void), found {returnTypeToken.Describe()}.", returnTypeToken);

        var nameToken = cursor.Current;
        var name = cursor.ExpectIdentifier("function name", functions);

        var parameters = new List<Parameter>();
        while (true)
        {
            if (cursor.Current.IsWord("void"))
            {
                throw cursor.Error("A parameter cannot have type void.");
            }

            if (cursor.Current.Kind != TokenKind.Word || Keywords.ValueType(cursor.Current.Text) is not { } parameterType)
            {
                break;
            }

            cursor.Advance();
            var parameterToken = cursor.Current;
            var parameterName = cursor.ExpectIdentifier("parameter name", functions);
            if (parameterName == name)
            {
                throw cursor.Error($"Parameter '{parameterName}' has the same name as its function.", parameterToken);
            }

            if (parameters.Any(parameter => parameter.Name == parameterName))
            {
                throw cursor.Error($"Parameter '{parameterName}' is declared twice.", parameterToken);
            }

            parameters.Add(new Parameter(parameterType, parameterName, parameterToken.Position));
        }

        return new FunctionSignature(returnType, name, parameters, nameToken.Position);
    }
}
