using System.Text;
using Pyrop.Bundling;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Spectre.Console;

namespace Pyrop.Cli;

public static class ScriptRunner
{
    public static int Run(string path, IReadOnlyList<string> arguments) =>
        ReadScript(path) is { } source ? Execute(source, path, null, null, arguments) : 1;

    public static string? ReadScript(string path)
    {
        if (!File.Exists(path))
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] {Markup.Escape(MissingScript(path))}");
            return null;
        }

        try
        {
            return FileSourceStore.ReadText(path);
        }
        catch (DecoderFallbackException)
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] '{Markup.Escape(path)}' is not UTF-8 text.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] Cannot read '{Markup.Escape(path)}': {Markup.Escape(exception.Message)}");
            return null;
        }
    }

    public static string MissingScript(string path) =>
        Directory.Exists(path) ? $"'{path}' is a folder, not a script." : $"File '{path}' was not found.";

    public static int RunBundle(Bundle bundle, IReadOnlyList<string> arguments) =>
        Execute(bundle.Entry.Text, bundle.EntryName, BundleSourceStore.FullPathOf(bundle.EntryName), new BundleSourceStore(bundle), arguments);

    private static int Execute(string source, string path, string? fullPath, ISourceStore? store, IReadOnlyList<string> arguments)
    {
        var output = Terminal.CreateProgramOutput();
        var session = new PyropSession(new LineInput(Console.In), output, HostContext.ForProcess(arguments), store);
        try
        {
            return session.Run(source, path, fullPath) ?? 0;
        }
        catch (PyropException exception)
        {
            Terminal.SeparateErrorsFrom(output);
            ErrorRenderer.Render(exception, path, session.SourceText);
            return 1;
        }
        finally
        {
            output.LeaveScreen();
        }
    }
}
