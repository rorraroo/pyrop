using Pyrop.Runtime;

namespace Pyrop.Cli;

public sealed class ConsoleProgramOutput(TextWriter writer, bool plain, bool inputEchoed = false, string newLine = "\n") : IProgramOutput
{
    public bool AtLineStart { get; private set; } = true;

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

    public void WritePrompt(string prompt, string? hint, OutputStyle style)
    {
        if (!plain)
        {
            Write("? ", OutputStyle.Info);
        }

        Write(prompt, style);
        Write(hint ?? "", OutputStyle.Muted);
    }

    public void LineRead()
    {
        if (inputEchoed)
        {
            AtLineStart = true;
        }
    }

    public void EnsureLineStart()
    {
        if (!AtLineStart)
        {
            Write("\n", OutputStyle.None);
        }
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
