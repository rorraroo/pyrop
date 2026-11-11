namespace Pyrop.Tests;

public sealed class StricterFormsTests
{
    [Theory]
    [InlineData("define list int bag list_of int end\npush bag 2", "push")]
    [InlineData("define int x 1\nset x add x 1 add x 2", "add")]
    [InlineData("length_of \"abc\"", "length_of")]
    [InlineData("use math library\nfrom math power 2 3", "power")]
    [InlineData("use time library\nimport from time now end\nnow", "now")]
    public void ThrowingAwayAResultThatHasNoEffectIsATypeError(string source, string operation)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.StartsWith($"'{operation}' only gives a result, and here the result is thrown away, so this line does nothing.", error.Message);
    }

    [Fact]
    public void AResultOfAnOperationWithAnEffectMayBeThrownAway()
    {
        const string source = """
            use random library
            import from random roll seed end
            read
            ask "Name? "
            seed 1
            roll 1 6
            write_line read
            """;

        Assert.Equal("Name? \nkept\n", Run.Program(source, "skipped\nname\nkept\n"));
    }

    [Fact]
    public void AFunctionResultMayBeThrownAway()
    {
        Assert.Equal("ran\n", Run.Program("function int noisy block\n    write_line \"ran\"\n    return 1\nend\nnoisy"));
    }

    [Theory]
    [InlineData("function int f block\n    return 1\n    write_line 2\nend", 3, 5, "return", 2)]
    [InlineData("while true block\n    break\n    write_line 1\nend", 3, 5, "break", 2)]
    [InlineData("for int i in list_of int 1 end block\n    continue\n    write_line i\nend", 3, 5, "continue", 2)]
    public void CodeAfterLeavingABlockIsAParseError(string source, int line, int column, string word, int wordLine)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        Assert.Equal(
            $"This code can never run, because the '{word}' on line {wordLine} leaves the block before it. Remove it, or move it above the '{word}'.",
            error.Message);
    }

    [Theory]
    [InlineData("record Point int x int y end\nfunction Point moved Point p block\n    return copy p with x 1\nend\nfunction int other int a block\n    return a\nend", "Functions can only be defined at the top level.")]
    [InlineData("function int f int a block\n    return a\nfunction int g return 1", "Functions can only be defined at the top level.")]
    [InlineData("function int f int a block\n    return add a 1 2\nend", "'2' cannot stand alone as a statement.")]
    public void AMistakeAfterLeavingABlockIsReportedAsItself(string source, string message)
    {
        Assert.StartsWith(message, Run.Failure(source).Message);
    }

    [Fact]
    public void AWronglyTypedOperationAloneIsReportedOnce()
    {
        var error = Assert.Single(Run.Failures("length_of 5"));

        Assert.StartsWith("'length_of' cannot take int.", error.Message);
    }

    [Fact]
    public void LeavingAtTheEndOfABlockIsAllowed()
    {
        Assert.Equal("1\n", Run.Program("function int f block\n    write_line 1\n    return 1\nend\nf"));
    }

    [Theory]
    [InlineData("write_line add 5-3")]
    [InlineData("define int x 1\nwrite_line add x-3 1")]
    [InlineData("write_line add \"a\"-3 1")]
    public void AMinusSignJoinedToWhatComesBeforeIsAnError(string source)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Lex, error.Stage);
        Assert.StartsWith("A minus sign joined to what comes before it is not a negative number", error.Message);
    }

    [Fact]
    public void ANegativeNumberAfterASpaceOrALineBreakIsFine()
    {
        Assert.Equal("2\n-3\n", Run.Program("write_line add 5 -3\nwrite_line\n-3"));
    }

    [Theory]
    [InlineData("/a", "/")]
    [InlineData("/a/b", "/a")]
    [InlineData("C:\\\\a", "C:\\")]
    [InlineData("C:/a/b.txt", "C:/a")]
    [InlineData("a/b", "a")]
    [InlineData("b", "")]
    [InlineData("/", "/")]
    public void FolderOfKeepsTheRoot(string path, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program($"use files library\nwrite_line from files folder_of \"{path}\""));
    }

    [Theory]
    [InlineData("26-01-02")]
    [InlineData("02026-01-02")]
    public void AYearIsReadWithFourDigits(string text)
    {
        var error = Run.Failure($"use time library\nwrite_line from time parse_moment \"{text}\" \"{{year}}-{{month}}-{{day}}\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{text}\" does not fit the layout \"{{year}}-{{month}}-{{day}}\"{(text.Length == 8 ? ": a year is written with four digits." : ".")}", error.Message);
    }

    [Fact]
    public void ACopyWithoutItsEndIsIncompleteInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run("record Point int x int y end\ndefine Point p Point 1 2");

        Assert.Equal(InputStatus.Incomplete, session.Probe("set p copy p with x 3"));
        Assert.Equal(InputStatus.Ready, session.Probe("set p copy p with x 3 end"));
    }
}
