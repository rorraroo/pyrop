using System.Text;
using Spectre.Console;

namespace Pyrop.Cli;

public static class Repl
{
    public static string Version = "unknown";

    public static int Run()
    {
        var output = new ConsoleProgramOutput();
        var session = new PyropSession(Console.In, output);
        var pending = new StringBuilder();

        AnsiConsole.Write(new FigletText("Pyrop").Color(Color.FromHex("#7A1C1C")));
        AnsiConsole.Write(new Rule("[grey]v" + Version + "[/]").Centered().Justify(Justify.Left));
        AnsiConsole.MarkupLine("[bold]Pyrop[/] REPL. Press [grey]Ctrl+Z[/] then [grey]Enter[/] (Windows) or [grey]Ctrl+D[/] to exit.");
        AnsiConsole.MarkupLine("[grey]Unfinished input continues on the next line; an empty line submits it.[/]");

        while (true)
        {
            output.EnsureLineStart();
            AnsiConsole.Markup(pending.Length == 0 ? "[green]pyrop>[/] " : "[grey]  ...>[/] ");
            var line = Console.In.ReadLine();
            if (line is null)
            {
                AnsiConsole.WriteLine();
                return 0;
            }

            var isBlank = string.IsNullOrWhiteSpace(line);
            if (pending.Length == 0 && isBlank)
            {
                continue;
            }

            pending.AppendLine(line);
            var source = pending.ToString();
            if (!isBlank && session.Probe(source) != InputStatus.Ready)
            {
                continue;
            }

            pending.Clear();
            try
            {
                session.Run(source);
            }
            catch (PyropException exception)
            {
                output.EnsureLineStart();
                ErrorRenderer.Render(exception, "input", session.SourceText);
            }
        }
    }
}
