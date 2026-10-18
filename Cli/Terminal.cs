using Spectre.Console;

namespace Pyrop.Cli;

public static class Terminal
{
    private const int RedirectedWidth = 4096;

    public static bool Plain { get; private set; }

    public static bool ErrorsPlain { get; private set; } = true;

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

        var errorsPlain = plainRequested || noColor || Console.IsErrorRedirected;
        Errors = Create(Console.Error, errorsPlain, Console.IsErrorRedirected);
        ErrorsPlain = errorsPlain || !Errors.Profile.Capabilities.Ansi;
    }

    public static ConsoleProgramOutput CreateProgramOutput() =>
        new(
            AnsiConsole.Profile.Out.Writer,
            Plain,
            inputEchoed: !Console.IsInputRedirected,
            newLine: NewLineFor(Console.IsOutputRedirected),
            errors: new ConsoleStream(
                Errors.Profile.Out.Writer, ErrorsPlain, NewLineFor(Console.IsErrorRedirected), onScreen: !Console.IsErrorRedirected),
            sharedScreen: !Console.IsOutputRedirected && !Console.IsErrorRedirected,
            richConsole: Plain ? null : AnsiConsole.Console,
            selectable: !Plain && !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive);

    public static void SeparateErrorsFrom(ConsoleProgramOutput output) => output.BeginErrorMessage();

    private static string NewLineFor(bool redirected) => OperatingSystem.IsWindows() && !redirected ? "\r\n" : "\n";

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
