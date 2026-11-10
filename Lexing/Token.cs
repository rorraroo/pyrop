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
    private const int LongestQuotedText = 60;

    public bool IsWord(string text) => Kind == TokenKind.Word && Text == text;

    public string Describe() => Kind switch
    {
        TokenKind.EndOfInput => "end of input",
        TokenKind.String => $"string \"{Shortened(Text)}\"",
        _ => $"'{Shortened(Text)}'",
    };

    public static string Shortened(string text) =>
        text.Length <= LongestQuotedText ? text : $"{text[..LongestQuotedText]}…";
}
