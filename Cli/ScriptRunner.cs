using Pyrop.Runtime;
using Spectre.Console;

namespace Pyrop.Cli;

public static class ScriptRunner
{
    public static int Run(string path, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red bold]Error[/] File '{Markup.Escape(path)}' was not found.");
            return 1;
        }

        var source = File.ReadAllText(path);
        var output = new ConsoleProgramOutput();
        var session = new PyropSession(Console.In, output, HostContext.ForProcess(arguments));
        try
        {
            return session.Run(source, path) ?? 0;
        }
        catch (PyropException exception)
        {
            output.EnsureLineStart();
            ErrorRenderer.Render(exception, path, session.SourceText);
            return 1;
        }
    }
}
