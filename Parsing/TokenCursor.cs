using Pyrop.Lexing;

namespace Pyrop.Parsing;

public sealed class TokenCursor(IReadOnlyList<Token> tokens)
{
    private int _index;

    public Token Current => tokens[_index];

    public bool IsAtEnd => Current.Kind == TokenKind.EndOfInput;

    public Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _index++;
        }

        return token;
    }

    public void MoveTo(int index) => _index = index;

    public int Index => _index;

    public PyropException Error(string message, Token? at = null)
    {
        var token = at ?? Current;
        return new PyropException(ErrorStage.Parse, message, token.Position, token.Kind == TokenKind.EndOfInput);
    }

    public Token ExpectWord(string expectation)
    {
        if (Current.Kind != TokenKind.Word)
        {
            throw Error($"Expected {expectation}, found {Current.Describe()}.");
        }

        return Advance();
    }

    public string ExpectIdentifier(string role, FunctionTable functions)
    {
        var token = ExpectWord(role);
        if (Keywords.IsReserved(token.Text))
        {
            throw Error($"'{token.Text}' is a reserved word and cannot be used as a {role}.", token);
        }

        if (role != "function name" && functions.Contains(token.Text))
        {
            throw Error($"'{token.Text}' is a function and cannot be used as a {role}.", token);
        }

        return token.Text;
    }
}
