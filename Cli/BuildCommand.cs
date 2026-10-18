using System.Globalization;
using System.Reflection;
using Pyrop.Bundling;
using Pyrop.Parsing;
using Spectre.Console;

namespace Pyrop.Cli;

public static class BuildCommand
{
    public static bool IsStandalone =>
        typeof(BuildCommand).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute is { Key: "PyropStandalone", Value: "true" })
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, "Pyrop.dll"));

    public static int Run(IReadOnlyList<string> arguments)
    {
        var (script, output) = arguments switch
        {
            [var path] => (path, null),
            [var path, "-o" or "--output", var target] => (path, target),
            _ => ((string?)null, (string?)null),
        };

        if (script is null || script.StartsWith('-'))
        {
            Terminal.Errors.MarkupLine("Usage: [bold]pyrop --build[/] [grey]<file.prp> [[-o <output>]][/]");
            return 2;
        }

        if (!IsStandalone || Environment.ProcessPath is not { } baseExecutable)
        {
            return Fail(
                "pyrop --build needs the standalone pyrop, a self-contained single-file build that copies itself into the program. " +
                "This pyrop runs on an installed .NET. Create it by running dotnet publish in the Pyrop repository.");
        }

        return Build(script, output, baseExecutable);
    }

    public static int Build(string script, string? output, string baseExecutable)
    {
        if (!File.Exists(script))
        {
            return Fail($"File '{script}' was not found.");
        }

        if (output is not null && (Path.EndsInDirectorySeparator(output) || Directory.Exists(output)))
        {
            return Fail($"'{output}' is a directory; -o takes the path of the executable to write.");
        }

        var scriptFullPath = Path.GetFullPath(script);
        string source;
        try
        {
            source = File.ReadAllText(scriptFullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Cannot read '{script}': {exception.Message}");
        }

        var session = CheckingSession(null);
        IReadOnlyList<SourceFile> modules;
        try
        {
            modules = session.Check(source, script);
        }
        catch (PyropException exception)
        {
            ErrorRenderer.Render(exception, script, session.SourceText);
            return 1;
        }

        Bundle? bundle = null;
        (PyropException Error, PyropSession Session, string EntryName)? firstFailure = null;
        try
        {
            foreach (var candidate in Bundle.Candidates(scriptFullPath, source, modules))
            {
                var bundled = CheckingSession(new BundleSourceStore(candidate));
                try
                {
                    bundled.Check(candidate.Entry.Text, candidate.EntryName, BundleSourceStore.FullPathOf(candidate.EntryName));
                    bundle = candidate;
                    break;
                }
                catch (PyropException exception)
                {
                    firstFailure ??= (exception, bundled, candidate.EntryName);
                }
            }
        }
        catch (BuildException exception)
        {
            return Fail(exception.Message);
        }

        if (bundle is null)
        {
            Fail("The program cannot be bundled: a use path does not resolve to one of the program's files inside the executable. Absolute use paths cannot be bundled; use paths relative to the using file.");
            if (firstFailure is var (error, failedSession, entryName))
            {
                ErrorRenderer.Render(error, entryName, failedSession.SourceText);
            }

            return 1;
        }

        var outputPath = OutputPath(script, output);
        if (Problem(outputPath, baseExecutable, modules.Select(file => file.FullPath).Append(scriptFullPath)) is { } problem)
        {
            return Fail(problem);
        }

        try
        {
            bundle.WriteExecutable(baseExecutable, outputPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Cannot write '{outputPath}': {exception.Message}");
        }

        var files = bundle.Files.Count == 1 ? "1 file" : $"{bundle.Files.Count} files";
        var size = new FileInfo(outputPath).Length / (1024.0 * 1024.0);
        AnsiConsole.MarkupLine($"Built [bold]{Markup.Escape(Path.GetRelativePath(Environment.CurrentDirectory, outputPath))}[/] from {files} ({size.ToString("0.0", CultureInfo.InvariantCulture)} MB).");
        return 0;
    }

    private static string OutputPath(string script, string? output)
    {
        var path = Path.GetFullPath(output ?? Path.GetFileNameWithoutExtension(script));
        return OperatingSystem.IsWindows() && Path.GetExtension(path).Length == 0 ? path + ".exe" : path;
    }

    private static string? Problem(string outputPath, string baseExecutable, IEnumerable<string> sources)
    {
        var comparer = SourceLoader.PathComparer;
        if (comparer.Equals(outputPath, Path.GetFullPath(baseExecutable)))
        {
            return $"'{outputPath}' is the running pyrop and cannot be replaced by a build.";
        }

        if (sources.Any(source => comparer.Equals(source, outputPath)))
        {
            return $"'{outputPath}' is one of the program's source files and cannot be replaced by a build.";
        }

        if (Directory.Exists(outputPath))
        {
            return $"'{outputPath}' is a directory.";
        }

        var directory = Path.GetDirectoryName(outputPath)!;
        return Directory.Exists(directory) ? null : $"Directory '{directory}' does not exist.";
    }

    private static PyropSession CheckingSession(ISourceStore? store) =>
        new(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true), store: store);

    private static int Fail(string message)
    {
        Terminal.Errors.MarkupLine($"[red bold]Error[/] {Markup.Escape(message)}");
        return 1;
    }
}
