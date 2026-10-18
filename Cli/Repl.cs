using System.Text;
using Pyrop.Runtime;
using Spectre.Console;

namespace Pyrop.Cli;

public static class Repl
{
    public static string Version = "unknown";

    public static int Run()
    {
        var input = new LineInput(Console.In);
        var output = Terminal.CreateProgramOutput();
        var session = new PyropSession(input, output, HostContext.ForProcess([]));
        var pending = new StringBuilder();

        if (Terminal.Plain)
        {
            AnsiConsole.WriteLine($"Pyrop {Version}");
        }
        else
        {
            Banner.Write(Version);
        }
        AnsiConsole.MarkupLine("[bold]Pyrop[/] REPL. Type [grey]exit 0[/], or press [grey]Ctrl+Z[/] then [grey]Enter[/] (Windows) or [grey]Ctrl+D[/] to leave.");
        AnsiConsole.MarkupLine("[grey]Unfinished input continues on the next line; an empty line submits it.[/]");

        while (true)
        {
            output.EnsureLineStart();
            AnsiConsole.Markup(pending.Length == 0 ? "[green]pyrop>[/] " : "[grey]  ...>[/] ");
            var line = input.ReadLine();
            if (line is null)
            {
                var waiting = pending.ToString();
                if (pending.Length > 0 && session.Probe(waiting) != InputStatus.Incomplete && Submit(session, output, waiting) is { } finalCode)
                {
                    return finalCode;
                }

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
            if (Submit(session, output, source) is { } exitCode)
            {
                return exitCode;
            }
        }
    }

    private static int? Submit(PyropSession session, ConsoleProgramOutput output, string source)
    {
        try
        {
            if (session.Run(source) is { } exitCode)
            {
                output.EnsureLineStart();
                return exitCode;
            }
        }
        catch (PyropException exception)
        {
            Terminal.SeparateErrorsFrom(output);
            ErrorRenderer.Render(exception, "input", session.SourceText);
        }

        return null;
    }
}
