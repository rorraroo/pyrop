using Pyrop.Runtime;

namespace Pyrop.Tests;

public class ReservedForLaterTests
{
    private static PyropError ParseFailure(string source)
    {
        var error = Run.Failure(source);
        Assert.Equal(ErrorStage.Parse, error.Stage);
        return error;
    }

    [Theory]
    [InlineData("call")]
    [InlineData("function_type")]
    [InlineData("function_of")]
    [InlineData("at_once")]
    public void UsingTheWordSaysItIsForALaterVersion(string word)
    {
        var expected = $"'{word}' is reserved for a later version of Pyrop.";

        Assert.Equal(expected, ParseFailure($"{word} Combine how 1 2").Message);
        Assert.Equal(expected, ParseFailure($"write_line {word} Combine how 1 2").Message);
        Assert.Equal(expected, ParseFailure($"write_line attempt {word} Combine").Message);
        Assert.Equal(expected, ParseFailure($"export {word} Combine int int int").Message);
    }

    [Theory]
    [InlineData("define int call 1", "variable name")]
    [InlineData("function int call block return 1 end", "function name")]
    [InlineData("function int f int call block return call end", "parameter name")]
    [InlineData("for int call in list_of int end write_line 1", "loop variable name")]
    [InlineData("record call int x end", "record name")]
    [InlineData("record Point int call end", "field name")]
    [InlineData("choice call north end", "choice name")]
    [InlineData("choice Way call end", "variant name")]
    [InlineData("use call library", "module name")]
    public void UsingTheWordAsANameSaysWhyItIsTaken(string source, string role)
    {
        var message = ParseFailure(source).Message;

        Assert.StartsWith("'call' is reserved for a later version of Pyrop", message);
        Assert.Contains(role, message);
    }

    [Theory]
    [InlineData("define call x 1")]
    [InlineData("record R call x end")]
    public void UsingTheWordAsATypeSaysItIsForALaterVersion(string source)
    {
        Assert.Equal("'call' is reserved for a later version of Pyrop.", ParseFailure(source).Message);
    }

    [Theory]
    [InlineData("functiontype")]
    [InlineData("functionof")]
    [InlineData("atonce")]
    public void AMisspeltWordIsNotPointedAtAWordThatCannotBeUsed(string word)
    {
        Assert.DoesNotContain("did you mean", Run.Failure($"{word} 1").Message);
    }

    [Fact]
    public void OrdinaryReservedWordsKeepTheirMessage()
    {
        Assert.Equal("'add' is a reserved word and cannot be used as a variable name.", ParseFailure("define int add 1").Message);
    }
}
