namespace Pyrop.Tests;

public class TypeCheckerTests
{
    [Theory]
    [InlineData("def int a\nset a \"text\"", "Cannot set int variable")]
    [InlineData("writeline add 1 2.0", "'add' cannot take int float")]
    [InlineData("writeline convert int true", "Cannot convert bool to int")]
    [InlineData("if 1 writeline 1", "condition of 'if' must be bool")]
    [InlineData("while \"x\" break", "condition of 'while' must be bool")]
    [InlineData("writeline missing", "Undeclared variable 'missing'")]
    [InlineData("function int f return \"x\"", "must return int, found string")]
    [InlineData("function int f int x return x\nwriteline f 1.5", "Argument 'x' of 'f' must be int")]
    [InlineData("writeline later\ndef int later", "Undeclared variable 'later'")]
    [InlineData("block def int inner end\nfunction int f return inner", "Undeclared variable 'inner'")]
    public void RejectsTypeErrors(string source, string message)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void ReportsAllErrorsAndRunsNothing()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        var exception = Assert.Throws<PyropException>(() => session.Run("writeline 1\nwriteline add 1 true\nwriteline nope"));

        Assert.Equal(2, exception.Errors.Count);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void FunctionsSeeGlobalsDeclaredLaterInTheFile()
    {
        Assert.Equal("5\n", Run.Program("function int get return value\ndef int value\nset value 5\nwriteline get"));
    }

    [Theory]
    [InlineData("function int f int x if greater x 0 return 1")]
    [InlineData("function int f int x block if greater x 0 return 1 else if less x 0 return -1 end")]
    [InlineData("function int f while true break")]
    [InlineData("function int f while less 1 2 return 1")]
    public void RejectsFunctionsThatMayNotReturn(string source)
    {
        Assert.Contains("on every path", Run.Failure(source).Message);
    }

    [Theory]
    [InlineData("function int f int x if greater x 0 return 1 else if less x 0 return -1 else return 0")]
    [InlineData("function int f block writeline 1 return 2 end")]
    [InlineData("function int f while true block while true break return 1 end")]
    [InlineData("function void f writeline 1")]
    public void AcceptsFunctionsThatAlwaysReturn(string source)
    {
        Run.Program(source);
    }
}
