using Pyrop.Bundling;
using Pyrop.Cli;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class AcceptanceTests : IDisposable
{
    private static readonly string Samples = FindSamples();

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-logcheck-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Script => Path.Combine(Samples, "logcheck", "logcheck.prp");

    private static string ExampleLog => Path.Combine(Samples, "logcheck", "example.log");

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static (string Output, int ExitCode) Run(string[] arguments) =>
        RunSource(File.ReadAllText(Script), Script, null, null, arguments);

    private static (string Output, int ExitCode) RunSource(
        string source, string path, string? fullPath, Parsing.ISourceStore? store, string[] arguments)
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output, new HostContext(arguments, _ => null), store);
        var exitCode = session.Run(source, path, fullPath) ?? 0;
        return (output.ToString(), exitCode);
    }

    [Fact]
    public void ReportsTheErrorsInALog()
    {
        var (output, exitCode) = Run([ExampleLog, PathOf("report.txt")]);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"8 lines, 2 errors, 2 warnings\n<danger>Errors found; see {PathOf("report.txt")}.\n</danger>",
            output);
        Assert.Equal(
            $"Errors in {ExampleLog}\n\n" +
            "    5: ERROR 2026-10-12 09:02:11 Payment gateway timed out\n" +
            "    7: error 2026-10-12 09:02:20 Payment gateway refused the request\n" +
            "\n8 lines, 2 errors, 2 warnings\n",
            File.ReadAllText(PathOf("report.txt")));
    }

    [Fact]
    public void ExitsWithZeroForACleanLog()
    {
        File.WriteAllText(PathOf("clean.log"), "INFO started\r\n\r\nWARN slow\r\n");

        var (output, exitCode) = Run([PathOf("clean.log"), PathOf("report.txt")]);

        Assert.Equal(0, exitCode);
        Assert.Equal("2 lines, 0 errors, 1 warning\n<success>No errors.\n</success>", output);
    }

    [Fact]
    public void ExplainsItsUsage()
    {
        Assert.Equal(("usage: logcheck <log file> [report file]\n", 2), Run([]));
    }

    [Fact]
    public void ReportsAMissingLog()
    {
        Assert.Equal(("<danger>No such file: missing.log\n</danger>", 3), Run(["missing.log"]));
    }

    [Fact]
    public void ShipsAsAnExecutableThatBehavesTheSame()
    {
        var baseExecutable = PathOf("pyrop-base");
        File.WriteAllBytes(baseExecutable, "base"u8.ToArray());
        var executable = PathOf(OperatingSystem.IsWindows() ? "logcheck.exe" : "logcheck");

        Assert.Equal(0, BuildCommand.Build(Script, executable, baseExecutable));

        var bundle = Bundle.ReadFrom(executable)!;
        Assert.Equal(["lines.prp", "logcheck.prp"], bundle.Files.Select(file => file.Name));
        var (output, exitCode) = RunSource(
            bundle.Entry.Text,
            bundle.EntryName,
            BundleSourceStore.FullPathOf(bundle.EntryName),
            new BundleSourceStore(bundle),
            [ExampleLog, PathOf("built-report.txt")]);
        Assert.Equal(1, exitCode);
        Assert.StartsWith("8 lines, 2 errors, 2 warnings\n", output);
        Assert.True(File.Exists(PathOf("built-report.txt")));
    }

    private static string FindSamples()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var samples = Path.Combine(directory.FullName, "samples");
            if (File.Exists(Path.Combine(samples, "logcheck", "logcheck.prp")))
            {
                return samples;
            }
        }

        throw new InvalidOperationException("The samples directory was not found above the test output.");
    }
}
