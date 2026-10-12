using Spectre.Console;

namespace Pyrop.Cli;

public static class Terminal
{
    private const int RedirectedWidth = 4096;

    public static bool Plain { get; private set; }

    public static IAnsiConsole Errors { get; private set; } = AnsiConsole.Console;

    public static void Configure(bool plainRequested)
    {
        var noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        Plain = plainRequested || noColor || Console.IsOutputRedirected;
        if (Plain)
        {
            AnsiConsole.Console = Create(Console.Out, plain: true, Console.IsOutputRedirected);
        }
        else
        {
            Plain = !AnsiConsole.Profile.Capabilities.Ansi;
        }

        Errors = Create(Console.Error, plainRequested || noColor || Console.IsErrorRedirected, Console.IsErrorRedirected);
    }

    public static ConsoleProgramOutput CreateProgramOutput() =>
        new(AnsiConsole.Profile.Out.Writer, Plain, inputEchoed: !Console.IsInputRedirected);

    public static void SeparateErrorsFrom(ConsoleProgramOutput output)
    {
        if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            output.EnsureLineStart();
        }
    }

    private static IAnsiConsole Create(TextWriter writer, bool plain, bool redirected)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = plain ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = plain ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
        });

        if (redirected)
        {
            console.Profile.Width = RedirectedWidth;
        }

        return console;
    }
}
