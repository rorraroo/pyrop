using Pyrop.Lexing;

namespace Pyrop.Parsing;

public sealed class TokenCursor(IReadOnlyList<Token> tokens, bool reportsIncompleteInput = true)
{
    private int _index;

    public Token Current => tokens[_index];

    public bool IsAtEnd => Current.Kind == TokenKind.EndOfInput;

    public Token Peek(int offset) => tokens[Math.Min(_index + offset, tokens.Count - 1)];

    public Token? Behind(int offset) => _index - offset >= 0 ? tokens[_index - offset] : null;

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
        return new PyropException(ErrorStage.Parse, message, token.Position, reportsIncompleteInput && token.Kind == TokenKind.EndOfInput);
    }

    public Token ExpectWord(string expectation)
    {
        if (Current.Kind != TokenKind.Word)
        {
            throw Error($"Expected {expectation}, found {Current.Describe()}.");
        }

        return Advance();
    }

    public string ExpectIdentifier(string role, ModuleNamespace names)
    {
        var token = ExpectWord(role);
        if (token.IsWord(Keywords.From))
        {
            throw Error(
                $"A {role} must be a plain name; '{Keywords.From}' starts a qualified name, which can only name an exported function, record, choice or variant.",
                token);
        }

        if (Keywords.IsReserved(token.Text))
        {
            throw Error($"'{token.Text}' is a reserved word and cannot be used as a {role}.", token);
        }

        if (role != "function name" && names.Describe(token.Text) is { } kind)
        {
            throw Error($"'{token.Text}' is {kind} and cannot be used as a {role}.", token);
        }

        return token.Text;
    }
}
