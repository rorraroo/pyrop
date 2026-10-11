using Spectre.Console;

namespace Pyrop.Cli;

public static class ErrorRenderer
{
    public static void Render(PyropException exception, string source, string origin)
    {
        var lines = source.Split('\n');
        foreach (var error in exception.Errors)
        {
            AnsiConsole.MarkupLine(
                $"[red bold]{StageName(error.Stage)}[/] [grey]{Markup.Escape(origin)}:{error.Position}[/] {Markup.Escape(error.Message)}");

            if (error.Position.Line < 1 || error.Position.Line > lines.Length)
            {
                continue;
            }

            var line = lines[error.Position.Line - 1].TrimEnd('\r');
            var caretPadding = new string(line.Take(Math.Max(0, error.Position.Column - 1)).Select(c => c == '\t' ? '\t' : ' ').ToArray());
            AnsiConsole.MarkupLine($"  [grey]|[/] {Markup.Escape(line)}");
            AnsiConsole.MarkupLine($"  [grey]|[/] {caretPadding}[red]^[/]");
        }
    }

    private static string StageName(ErrorStage stage) => stage switch
    {
        ErrorStage.Lex => "Syntax error",
        ErrorStage.Parse => "Parse error",
        ErrorStage.Type => "Type error",
        ErrorStage.Runtime => "Runtime error",
        _ => "Internal error",
    };
}
