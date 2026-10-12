namespace Pyrop.Tests;

public class TypeCheckerTests
{
    [Theory]
    [InlineData("def int a 0\nset a \"text\"", "Cannot set int variable")]
    [InlineData("def int a \"text\"", "Cannot initialize int variable 'a' with a string value")]
    [InlineData("def int a add a 1", "Undeclared variable 'a'")]
    [InlineData("write_line add 1 2.0", "'add' cannot take int float")]
    [InlineData("write_line convert int true", "Cannot convert bool to int")]
    [InlineData("if 1 write_line 1", "condition of 'if' must be bool")]
    [InlineData("while \"x\" break", "condition of 'while' must be bool")]
    [InlineData("write_line missing", "Undeclared variable 'missing'")]
    [InlineData("function int f return \"x\"", "must return int, found string")]
    [InlineData("function int f int x return x\nwrite_line f 1.5", "Argument 'x' of 'f' must be int")]
    [InlineData("write_line later\ndef int later 1", "Undeclared variable 'later'")]
    [InlineData("block def int inner 1 end\nfunction int f return inner", "Undeclared variable 'inner'")]
    [InlineData("write_line argumentcount", "did you mean 'argument_count'?")]
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

        var exception = Assert.Throws<PyropException>(() => session.Run("write_line 1\nwrite_line add 1 true\nwrite_line nope"));

        Assert.Equal(2, exception.Errors.Count);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void FunctionsSeeGlobalsDeclaredLaterInTheFile()
    {
        Assert.Equal("5\n", Run.Program("function int get return value\ndef int value 5\nwrite_line get"));
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
    [InlineData("function int f block write_line 1 return 2 end")]
    [InlineData("function int f while true block while true break return 1 end")]
    [InlineData("function void f write_line 1")]
    public void AcceptsFunctionsThatAlwaysReturn(string source)
    {
        Run.Program(source);
    }
}
