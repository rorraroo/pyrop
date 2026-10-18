using Pyrop.Runtime;

namespace Pyrop.Cli;

public sealed class ConsoleStream(TextWriter writer, bool plain, string newLine = "\n", bool onScreen = false)
{
    public static ConsoleStream Null { get; } = new(TextWriter.Null, plain: true);

    public bool OnScreen { get; } = onScreen;

    public bool AtLineStart { get; set; } = true;

    public void Write(string text, OutputStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        var rendered = plain || style == OutputStyle.None ? text : Styled(text, style);
        writer.Write(newLine == "\n" ? rendered : rendered.Replace("\n", newLine));
        writer.Flush();
        AtLineStart = text[^1] == '\n';
    }

    private static string Styled(string text, OutputStyle style)
    {
        var code = style switch
        {
            OutputStyle.Emphasis => "1",
            OutputStyle.Muted => "90",
            OutputStyle.Success => "32",
            OutputStyle.Warning => "33",
            OutputStyle.Danger => "31",
            OutputStyle.Info => "36",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

        return string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : $"\e[{code}m{line}\e[0m"));
    }
}
