using Pyrop.Runtime;

namespace Pyrop.Tests;

public class ProcessTests
{
    private static HostContext Host(string[] arguments, Dictionary<string, string>? environment = null) =>
        new(arguments, name => environment?.GetValueOrDefault(name));

    private const string Program = "use program library\nimport from program arguments environment end\n";

    [Fact]
    public void ReadsCommandLineArguments()
    {
        var host = Host(["report.txt", "--verbose"]);

        Assert.Equal(
            "2\nreport.txt\n--verbose\n",
            Run.Program(Program + "write_line length_of arguments\nwrite_line get arguments 0\nwrite_line get arguments 1", host: host));
    }

    [Fact]
    public void TreatsAShebangLineAsAComment()
    {
        Assert.Equal("1\n", Run.Program("#!/usr/bin/env pyrop\nwrite_line 1"));
    }

    [Fact]
    public void HasNoArgumentsByDefault()
    {
        Assert.Equal("0\n", Run.Program(Program + "write_line length_of arguments"));
    }

    [Theory]
    [InlineData("write_line get arguments 1", "List index 1 is out of range for a list of 1 item, numbered from 0.")]
    [InlineData("write_line get arguments -1", "List index -1 is out of range")]
    public void RejectsArgumentIndexesOutOfRange(string source, string message)
    {
        var exception = Assert.Throws<PyropException>(() => Run.Program(Program + source, host: Host(["only"])));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void ReadsEnvironmentVariablesAndTreatsUnsetAsEmpty()
    {
        var host = Host([], new Dictionary<string, string> { ["HOME"] = "/home/ann" });

        Assert.Equal(
            "/home/ann\nunset\n",
            Run.Program(
                Program + "write_line environment \"HOME\"\nif equal environment \"MISSING\" \"\" write_line \"unset\"",
                host: host));
    }

    [Fact]
    public void RejectsAnEmptyEnvironmentVariableName()
    {
        var error = Run.Failure(Program + "write_line environment \"\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains("cannot be empty", error.Message);
    }

    [Theory]
    [InlineData("write_line environment \"A=B\"")]
    [InlineData("write_line environment \"=C:\"")]
    [InlineData("write_line environment \"A\\nB\"")]
    public void RejectsInvalidEnvironmentVariableNames(string source)
    {
        var error = Run.Failure(Program + source, host: Host([], new Dictionary<string, string> { ["=C:"] = "C:\\" }));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains("is not a valid environment variable name", error.Message);
    }

    [Fact]
    public void ALineReadAheadByInputEndedStaysAvailableToTheHost()
    {
        var input = new LineInput(new StringReader("next line\n"));
        var output = new StringOutput();
        var session = new PyropSession(input, output);

        session.Run("write_line input_ended");

        Assert.Equal("false\n", output.ToString());
        Assert.Equal("next line", input.ReadLine());
        Assert.Null(input.ReadLine());
    }

    [Theory]
    [InlineData("﻿first\n﻿second\n", "first", "﻿second")]
    [InlineData("﻿\nnext\n", "", "next")]
    public void AByteOrderMarkIsSkippedOnlyAtTheStartOfInput(string text, string first, string second)
    {
        var input = new LineInput(new StringReader(text));

        Assert.False(input.HasEnded());
        Assert.Equal(first, input.ReadLine());
        Assert.Equal(second, input.ReadLine());
        Assert.Null(input.ReadLine());
    }

    [Fact]
    public void PipedInputStartingWithAByteOrderMarkRuns()
    {
        Assert.Equal("ok\n", Run.Program("write_line read", "﻿ok\n"));
    }

    [Theory]
    [InlineData("a\nb\n", "a\nb\n")]
    [InlineData("a\r\nb", "a\nb\n")]
    [InlineData("", "")]
    [InlineData("\n", "\n")]
    public void ReadsInputUntilItEnds(string input, string expected)
    {
        Assert.Equal(expected, Run.Program("while not input_ended write_line read", input));
    }

    [Fact]
    public void CheckingForTheEndDoesNotConsumeALine()
    {
        Assert.Equal("false\nfalse\nfirst\ntrue\n", Run.Program(
            "write_line input_ended\nwrite_line input_ended\nwrite_line read\nwrite_line input_ended",
            "first\n"));
    }

    [Fact]
    public void ExitEndsTheProgramWithItsCode()
    {
        Assert.Equal(("before\n", 3), Run.Execute("write_line \"before\"\nexit 3\nwrite_line \"after\""));
    }

    [Fact]
    public void FinishingNormallyHasNoExitCode()
    {
        Assert.Equal(("done\n", null), Run.Execute("write_line \"done\""));
    }

    [Fact]
    public void ExitUnwindsFromInsideAnExpression()
    {
        const string source = """
            function int check int value
            block
                if less value 0 exit 2
                return value
            end

            write_line "start"
            write_line add 1 check -5
            write_line "unreachable"
            """;

        Assert.Equal(("start\n", 2), Run.Execute(source));
    }

    [Fact]
    public void ExitUnwindsFromLoopsAndVoidFunctions()
    {
        const string source = """
            function void stop
            block
                while true
                block
                    write "x"
                    exit 0
                end
            end

            stop
            write_line "unreachable"
            """;

        Assert.Equal(("x", 0), Run.Execute(source));
    }

    [Fact]
    public void ExitCountsAsLeavingAFunctionOnThatPath()
    {
        const string source = """
            function int parse_or_quit string text
            block
                if equal text "" exit 1
                else return convert int text
            end

            write_line parse_or_quit "42"
            """;

        Assert.Equal("42\n", Run.Program(source));
    }

    [Theory]
    [InlineData("exit 256", "Exit code 256 is out of range; it must be between 0 and 255.")]
    [InlineData("exit -1", "Exit code -1 is out of range")]
    public void RejectsExitCodesOutOfRange(string source, string message)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void RequiresAnIntExitCode()
    {
        var error = Run.Failure("exit \"failed\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains("The exit code must be int, found string.", error.Message);
    }

    [Fact]
    public void ExitWithoutACodeIsIncompleteInput()
    {
        var exception = Assert.Throws<PyropException>(() => Run.Program("exit"));

        Assert.True(exception.IsIncompleteInput);
    }

    [Theory]
    [InlineData("def int exit 1")]
    [InlineData("def string library \"\"")]
    public void ReservesTheNewNames(string source)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains("reserved word", error.Message);
    }

    [Theory]
    [InlineData("argument_count")]
    [InlineData("argument_at")]
    [InlineData("read_environment")]
    [InlineData("arguments")]
    [InlineData("environment")]
    public void ProcessNamesOutsideTheCoreAreFreeVariableNames(string name)
    {
        Assert.Equal("x\n", Run.Program($"def string {name} \"x\"\nwrite_line {name}"));
    }

    [Fact]
    public void ExitInALaterSessionInputKeepsEarlierState()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        Assert.Null(session.Run("def int count 5"));
        Assert.Equal(4, session.Run("write_line count\nexit 4"));
        Assert.Equal("5\n", output.ToString());
    }
}
