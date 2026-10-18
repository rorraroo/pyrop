using System.Diagnostics;
using Pyrop.Cli;

namespace Pyrop.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-cli-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Script(string source)
    {
        var path = Path.Combine(_directory, "script.prp");
        File.WriteAllText(path, source);
        return path;
    }

    private static (string Output, string Errors, int ExitCode) Pyrop(params string[] arguments) => PyropWithInput("", arguments);

    private static (string Output, string Errors, int ExitCode) PyropWithInput(string input, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(Terminal).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("NO_COLOR");
        using var process = Process.Start(start)!;
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (output, errors.Result, process.ExitCode);
    }

    [Theory]
    [InlineData("if true write_line 1\n", "1\n")]
    [InlineData("if true write_line 1", "1\n")]
    [InlineData("write_line 7 record Point int x\n", "7\n")]
    [InlineData("write_line add 1\n", "")]
    public void TheReplRunsAnInputThatOnlyWaitsWhenInputEnds(string input, string expected)
    {
        var (output, errors, exitCode) = PyropWithInput(input, "--plain");

        Assert.Equal("", errors);
        Assert.Equal(0, exitCode);
        Assert.EndsWith($"  ...> {expected}\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ErrorsGoToStandardErrorAndLeaveTheOutputUntouched()
    {
        var (output, errors, exitCode) = Pyrop(Script("write \"abc\"\nwrite_line divide 1 0"));

        Assert.Equal("abc", output);
        Assert.StartsWith("Runtime error ", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void RedirectedOutputIsPlain()
    {
        var (output, _, exitCode) = Pyrop(Script("with_style \"danger\" write_line \"x\"\nwrite_line confirm \"ok\""));

        Assert.Equal("x\nok [y/n] ", output);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void PlainComesBeforeTheScriptAndLaterArgumentsBelongToTheScript()
    {
        var (output, errors, exitCode) = Pyrop("--plain", Script("write_line argument_at 0"), "--plain");

        Assert.Equal("--plain\n", output);
        Assert.Equal("", errors);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AMissingScriptIsReportedOnStandardError()
    {
        var (output, errors, exitCode) = Pyrop(Path.Combine(_directory, "missing.prp"));

        Assert.Equal("", output);
        Assert.Contains("was not found", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void UsageErrorsGoToStandardError()
    {
        var (output, errors, exitCode) = Pyrop("--bogus");

        Assert.Equal("", output);
        Assert.StartsWith("Usage: pyrop", errors);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void BuildNeedsTheStandalonePyrop()
    {
        var (output, errors, exitCode) = Pyrop("--build", Script("write_line 1"));

        Assert.Equal("", output);
        Assert.Contains("pyrop --build needs the standalone pyrop", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void TreePrintsTheStructureAndThenTheError()
    {
        var (output, errors, exitCode) = Pyrop("--tree", Script("write_line add 1 2\nset x multiply 2 3 4"));

        Assert.Equal("write_line (add 1 2)\nset x (multiply 2 3)\n", output);
        Assert.StartsWith("Parse error ", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void TreeDoesNotRunTheProgram()
    {
        var (output, errors, exitCode) = Pyrop("-t", Script("write_line \"ran\"\nexit 3"));

        Assert.Equal("write_line \"ran\"\nexit 3\n", output);
        Assert.Equal("", errors);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AScriptNamedBuildRuns()
    {
        var path = Path.Combine(_directory, "build");
        File.WriteAllText(path, "write_line argument_at 0");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(Terminal).Assembly.Location);
        start.ArgumentList.Add("build");
        start.ArgumentList.Add("x.prp");
        start.Environment.Remove("NO_COLOR");

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal("x.prp\n", output);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void ProgramErrorTextGoesToStandardErrorWithoutStyles()
    {
        var (output, errors, exitCode) = Pyrop(Script("write_line \"report\"\nwith_style \"danger\" write_error_line \"careful\"\nwrite_error \"x\""));

        Assert.Equal("report\n", output);
        Assert.Equal("careful\nx", errors);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void PyropsMessagesFollowProgramErrorTextOnANewLine()
    {
        var (output, errors, exitCode) = Pyrop(Script("write \"partial\"\nwrite_error \"warn\"\nwrite_line divide 1 0"));

        Assert.Equal("partial", output);
        Assert.StartsWith("warn\nRuntime error ", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void FinishedErrorLinesAreNotFollowedByABlankLine()
    {
        var (_, errors, _) = Pyrop(Script("write_error_line \"warn\"\nexit divide 1 0"));

        Assert.StartsWith("warn\nRuntime error ", errors);
    }

    [Fact]
    public void RedirectedErrorsAreNotWrapped()
    {
        var (_, errors, _) = Pyrop(Script("with_style add 1 \"a\" write \"x\""));

        Assert.Contains("It accepts: int int, float float, string string.", errors.Split('\n')[0]);
    }

    [Fact]
    public void ChooseWithPipedInputUsesTheNumberedForm()
    {
        var (output, errors, exitCode) = PyropWithInput(
            "x\n2\n", Script("with_style \"info\" write_line choose \"Where?\" list_of string \"Hall\" \"Cellar\" end"));

        Assert.Equal("", errors);
        Assert.Equal("Where?\n1. Hall\n2. Cellar\nChoose a number from 1 to 2: Please choose a number from 1 to 2.\nChoose a number from 1 to 2: Cellar\n", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ChooseAtTheEndOfPipedInputIsARuntimeError()
    {
        var (output, errors, exitCode) = Pyrop(Script("write_line choose \"Where?\" list_of string \"Hall\" end"));

        Assert.Equal("Where?\n1. Hall\nChoose number 1: ", output);
        Assert.StartsWith("Runtime error ", errors);
        Assert.Contains("'choose' reached the end of input.", errors);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void RedirectedWriteTablePadsColumns()
    {
        var (output, errors, exitCode) = Pyrop(
            Script("with_style \"danger\" write_table list_of string \"Item\" \"Count\" end list_of list string list_of string \"lamp\" \"12\" end end"));

        Assert.Equal("", errors);
        Assert.Equal("Item  Count\nlamp  12\n", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void TheLanguageServerAnswersOnStandardOutputOnly()
    {
        static string Framed(string body) => $"Content-Length: {body.Length}\r\n\r\n{body}";
        var input =
            Framed("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""") +
            Framed("""{"jsonrpc":"2.0","id":2,"method":"shutdown"}""") +
            Framed("""{"jsonrpc":"2.0","method":"exit"}""");

        var (output, errors, exitCode) = PyropWithInput(input, "--lsp");

        Assert.Equal("", errors);
        Assert.StartsWith("Content-Length: ", output);
        Assert.Contains("\"serverInfo\":{\"name\":\"pyrop\"", output);
        Assert.EndsWith("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":null}", output);
        Assert.Equal(0, exitCode);
    }
}
