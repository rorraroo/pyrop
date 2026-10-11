using Pyrop.Runtime;
using Spectre.Console;

namespace Pyrop.Cli;

public sealed class ConsoleProgramOutput : IProgramOutput
{
    public bool AtLineStart { get; private set; } = true;

    public void Write(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var writer = AnsiConsole.Profile.Out.Writer;
        writer.Write(text);
        writer.Flush();
        AtLineStart = text[^1] == '\n';
    }

    public void EnsureLineStart()
    {
        if (!AtLineStart)
        {
            Write("\n");
        }
    }
}
