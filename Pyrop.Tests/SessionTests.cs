namespace Pyrop.Tests;

public class SessionTests
{
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public SessionTests()
    {
        _session = new PyropSession(new StringReader(""), _output);
    }

    [Fact]
    public void StateAccumulatesAcrossInputs()
    {
        _session.Run("def int x 2");
        _session.Run("function int double int n return multiply n 2");
        _session.Run("writeline double x");

        Assert.Equal("4\n", _output.ToString());
    }

    [Fact]
    public void FailedInputsLeaveNoTrace()
    {
        Assert.Throws<PyropException>(() => _session.Run("function int f return 1\nwriteline add 1 true"));

        _session.Run("function int f return 2\nwriteline f");

        Assert.Equal("2\n", _output.ToString());
    }

    [Fact]
    public void GlobalsCanOnlyBeRedeclaredWithTheSameType()
    {
        _session.Run("def int x 1");
        _session.Run("def int x add x 4");
        _session.Run("writeline x");

        Assert.Equal("5\n", _output.ToString());
        var exception = Assert.Throws<PyropException>(() => _session.Run("def string x \"a\""));
        Assert.Contains("cannot be redeclared", exception.Errors[0].Message);
    }

    [Fact]
    public void FunctionNamesCannotReuseGlobals()
    {
        _session.Run("def int x 1");

        var exception = Assert.Throws<PyropException>(() => _session.Run("function int x return 1"));
        Assert.Contains("already a global variable", exception.Errors[0].Message);
    }

    [Theory]
    [InlineData("block writeline 1", InputStatus.Incomplete)]
    [InlineData("writeline add 1", InputStatus.Incomplete)]
    [InlineData("if true writeline 1", InputStatus.AwaitingElse)]
    [InlineData("if true writeline 1 else if false writeline 2", InputStatus.AwaitingElse)]
    [InlineData("if true writeline 1 else writeline 2", InputStatus.Ready)]
    [InlineData("writeline 1", InputStatus.Ready)]
    [InlineData("writeline add 1 true", InputStatus.Ready)]
    [InlineData("end", InputStatus.Ready)]
    public void ProbesWhetherInputIsComplete(string source, InputStatus expected)
    {
        Assert.Equal(expected, _session.Probe(source));
    }
}
