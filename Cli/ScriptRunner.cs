using Spectre.Console;

namespace Pyrop.Cli;

public static class ScriptRunner
{
    public static int Run(string path)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red bold]Error[/] File '{Markup.Escape(path)}' was not found.");
            return 1;
        }

        var source = File.ReadAllText(path);
        var output = new ConsoleProgramOutput();
        var session = new PyropSession(Console.In, output);
        try
        {
            session.Run(source, path);
            return 0;
        }
        catch (PyropException exception)
        {
            output.EnsureLineStart();
            ErrorRenderer.Render(exception, path, session.SourceText);
            return 1;
        }
    }
}
