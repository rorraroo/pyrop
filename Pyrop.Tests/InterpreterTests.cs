namespace Pyrop.Tests;

public class InterpreterTests
{
    [Fact]
    public void RunsArithmeticAndOutput()
    {
        Assert.Equal("30\n3.5\nab", Run.Program("writeline add 10 20\nwriteline divide 7.0 2.0\nwrite add \"a\" \"b\""));
    }

    [Fact]
    public void RunsRecursiveFunctions()
    {
        const string source = """
            function int factorial int n
                if less n 2 return 1
                else return multiply n factorial subtract n 1
            writeline factorial 10
            """;

        Assert.Equal("3628800\n", Run.Program(source));
    }

    [Fact]
    public void SupportsMutualRecursion()
    {
        const string source = """
            function bool isEven int n if equal n 0 return true else return isOdd subtract n 1
            function bool isOdd int n if equal n 0 return false else return isEven subtract n 1
            writeline isEven 10
            """;

        Assert.Equal("true\n", Run.Program(source));
    }

    [Fact]
    public void LoopsWithBreakAndContinue()
    {
        const string source = """
            def int i 0
            while true block
                set i add i 1
                if equal modulo i 2 0 continue
                if greater i 7 break
                write i
            end
            """;

        Assert.Equal("1357", Run.Program(source));
    }

    [Fact]
    public void BlocksCreateFreshScopesPerIteration()
    {
        const string source = """
            def int i 0
            while less i 2 block
                def int x i
                write x
                set i add i 1
            end
            """;

        Assert.Equal("01", Run.Program(source));
    }

    [Fact]
    public void InitializerSeesTheOuterVariableItShadows()
    {
        Assert.Equal("2\n1\n", Run.Program("def int x 1\nblock def int x add x 1 writeline x end\nwriteline x"));
    }

    [Fact]
    public void VoidFunctionsEndWhenTheirBodyFinishes()
    {
        const string source = """
            function void greet string name
                if not equal name "" writeline add "Hi " name
            greet ""
            greet "Ann"
            """;

        Assert.Equal("Hi Ann\n", Run.Program(source));
    }

    [Fact]
    public void ShortCircuitsLogicalOperations()
    {
        Assert.Equal("false\ntrue\n", Run.Program("writeline and false equal divide 1 0 0\nwriteline or true equal divide 1 0 0"));
    }

    [Fact]
    public void ReadsInputAndConverts()
    {
        Assert.Equal("42\n", Run.Program("writeline add convert int read 1", " 41 \n"));
    }

    [Theory]
    [InlineData("writeline convert string 3.0", "3.0\n")]
    [InlineData("writeline convert float 2", "2.0\n")]
    [InlineData("writeline convert int -3.9", "-3\n")]
    [InlineData("writeline convert float \"1e3\"", "1000.0\n")]
    [InlineData("writeline convert bool \" true \"", "true\n")]
    [InlineData("writeline 0.1", "0.1\n")]
    public void ConvertsBetweenTypes(string source, string expected)
    {
        Assert.Equal(expected, Run.Program(source));
    }

    [Theory]
    [InlineData("writeline divide 1 0", "Division by zero")]
    [InlineData("writeline divide 1.0 0.0", "Division by zero")]
    [InlineData("writeline add 2147483647 1", "Integer overflow")]
    [InlineData("writeline multiply convert float \"1e308\" 10.0", "out of range")]
    [InlineData("writeline convert int \"abc\"", "Cannot convert \"abc\" to int")]
    [InlineData("writeline convert int 3000000000.0", "out of range for int")]
    [InlineData("writeline read", "end of input")]
    [InlineData("function int down int n return down add n 1\nwriteline down 0", "Call depth limit")]
    [InlineData("function int get return late\nwriteline get\ndef int late 1", "does not exist yet")]
    [InlineData("function int get return late\ndef int late get", "does not exist yet")]
    public void ReportsRuntimeErrors(string source, string message)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains(message, error.Message);
    }
}
