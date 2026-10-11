namespace Pyrop.Lexing;

public enum TokenKind
{
    Integer,
    Float,
    String,
    Word,
    EndOfInput,
}

public sealed record Token(TokenKind Kind, string Text, SourcePosition Position)
{
    public bool IsWord(string text) => Kind == TokenKind.Word && Text == text;

    public string Describe() => Kind switch
    {
        TokenKind.EndOfInput => "end of input",
        TokenKind.String => $"string \"{Text}\"",
        _ => $"'{Text}'",
    };
}
