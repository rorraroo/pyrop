using Pyrop.Lexing;

namespace Pyrop.Tests;

public class LexerTests
{
    [Fact]
    public void ProducesWordsAndLiteralsWithPositions()
    {
        var tokens = Lexer.Tokenize("set x 42\n  add -1.5 \"a\\tb\" # comment");

        Assert.Equal(
            [TokenKind.Word, TokenKind.Word, TokenKind.Integer, TokenKind.Word, TokenKind.Float, TokenKind.String, TokenKind.EndOfInput],
            tokens.Select(token => token.Kind));
        Assert.Equal("a\tb", tokens[5].Text);
        Assert.Equal(new SourcePosition(2, 3), tokens[3].Position);
    }

    [Fact]
    public void TreatsBoolLiteralsAsWords()
    {
        Assert.Equal(TokenKind.Word, Lexer.Tokenize("true")[0].Kind);
    }

    [Theory]
    [InlineData("\"open")]
    [InlineData("\"line\nbreak\"")]
    [InlineData("\"bad \\q escape\"")]
    [InlineData("1.")]
    [InlineData("12abc")]
    [InlineData("99999999999")]
    [InlineData("add 1 + 2")]
    public void RejectsMalformedInput(string source)
    {
        var exception = Assert.Throws<PyropException>(() => Lexer.Tokenize(source));
        Assert.Equal(ErrorStage.Lex, exception.Errors[0].Stage);
    }
}
