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

    private static (string Output, string Errors, int ExitCode) Pyrop(params string[] arguments)
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
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (output, errors.Result, process.ExitCode);
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
    public void RedirectedErrorsAreNotWrapped()
    {
        var (_, errors, _) = Pyrop(Script("with_style add 1 \"a\" write \"x\""));

        Assert.Contains("It accepts: int int, float float, string string.", errors.Split('\n')[0]);
    }
}
