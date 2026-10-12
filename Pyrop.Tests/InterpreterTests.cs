namespace Pyrop.Tests;

public class InterpreterTests
{
    [Fact]
    public void RunsArithmeticAndOutput()
    {
        Assert.Equal("30\n3.5\nab", Run.Program("write_line add 10 20\nwrite_line divide 7.0 2.0\nwrite add \"a\" \"b\""));
    }

    [Fact]
    public void RunsRecursiveFunctions()
    {
        const string source = """
            function int factorial int n
                if less n 2 return 1
                else return multiply n factorial subtract n 1
            write_line factorial 10
            """;

        Assert.Equal("3628800\n", Run.Program(source));
    }

    [Fact]
    public void SupportsMutualRecursion()
    {
        const string source = """
            function bool isEven int n if equal n 0 return true else return isOdd subtract n 1
            function bool isOdd int n if equal n 0 return false else return isEven subtract n 1
            write_line isEven 10
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
        Assert.Equal("2\n1\n", Run.Program("def int x 1\nblock def int x add x 1 write_line x end\nwrite_line x"));
    }

    [Fact]
    public void VoidFunctionsEndWhenTheirBodyFinishes()
    {
        const string source = """
            function void greet string name
                if not equal name "" write_line add "Hi " name
            greet ""
            greet "Ann"
            """;

        Assert.Equal("Hi Ann\n", Run.Program(source));
    }

    [Fact]
    public void ReturnVoidExitsAVoidFunctionEarly()
    {
        const string source = """
            function void count int limit
            block
                def int i 0
                while true
                block
                    if equal i limit return void
                    write_line i
                    set i add i 1
                end
                write_line "unreachable"
            end
            count 2
            write_line "done"
            """;

        Assert.Equal("0\n1\ndone\n", Run.Program(source));
    }

    [Fact]
    public void ShortCircuitsLogicalOperations()
    {
        Assert.Equal("false\ntrue\n", Run.Program("write_line and false equal divide 1 0 0\nwrite_line or true equal divide 1 0 0"));
    }

    [Fact]
    public void ComputesWithTheFull64BitRange()
    {
        Assert.Equal(
            "9223372036854775807\n-9223372036854775808\n0\n-1\n",
            Run.Program(
                "write_line add 9223372036854775806 1\n" +
                "write_line subtract -9223372036854775807 1\n" +
                "write_line modulo -9223372036854775808 -1\n" +
                "write_line modulo -9223372036854775807 2"));
    }

    [Fact]
    public void ReadsInputAndConverts()
    {
        Assert.Equal("42\n", Run.Program("write_line add convert int read 1", " 41 \n"));
    }

    [Theory]
    [InlineData("write_line convert string 3.0", "3.0\n")]
    [InlineData("write_line convert float 2", "2.0\n")]
    [InlineData("write_line convert int -3.9", "-3\n")]
    [InlineData("write_line convert float \"1e3\"", "1000.0\n")]
    [InlineData("write_line convert bool \" true \"", "true\n")]
    [InlineData("write_line 0.1", "0.1\n")]
    [InlineData("write_line convert int 3000000000.0", "3000000000\n")]
    [InlineData("write_line convert int -9223372036854775808.0", "-9223372036854775808\n")]
    [InlineData("write_line convert int \"9223372036854775807\"", "9223372036854775807\n")]
    [InlineData("write_line convert float 9007199254740993", "9007199254740992.0\n")]
    public void ConvertsBetweenTypes(string source, string expected)
    {
        Assert.Equal(expected, Run.Program(source));
    }

    [Theory]
    [InlineData("write_line divide 1 0", "Division by zero")]
    [InlineData("write_line divide 1.0 0.0", "Division by zero")]
    [InlineData("write_line add 9223372036854775807 1", "Integer overflow")]
    [InlineData("write_line subtract -9223372036854775808 1", "Integer overflow")]
    [InlineData("write_line multiply 4294967296 4294967296", "Integer overflow")]
    [InlineData("write_line divide -9223372036854775808 -1", "Integer overflow")]
    [InlineData("write_line modulo 1 0", "Division by zero")]
    [InlineData("write_line multiply convert float \"1e308\" 10.0", "out of range")]
    [InlineData("write_line convert int \"abc\"", "Cannot convert \"abc\" to int")]
    [InlineData("write_line convert int \"9223372036854775808\"", "Cannot convert \"9223372036854775808\" to int")]
    [InlineData("write_line convert int 9223372036854775807.0", "out of range for int")]
    [InlineData("write_line convert int -9223372036854777856.0", "out of range for int")]
    [InlineData("write_line read", "end of input")]
    [InlineData("function int down int n return down add n 1\nwrite_line down 0", "Call depth limit")]
    [InlineData("function int get return late\nwrite_line get\ndef int late 1", "does not exist yet")]
    [InlineData("function int get return late\ndef int late get", "does not exist yet")]
    public void ReportsRuntimeErrors(string source, string message)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains(message, error.Message);
    }
}
