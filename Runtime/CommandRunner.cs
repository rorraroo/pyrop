using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Pyrop.Runtime;

public sealed record CommandOutcome(int ExitCode, string Output, string Errors);

public sealed class CommandFailedException(string message) : Exception(message);

public interface ICommandRunner
{
    CommandOutcome Capture(string command, IReadOnlyList<string> arguments);

    int RunAttached(string command, IReadOnlyList<string> arguments);
}

public sealed class NoCommandRunner : ICommandRunner
{
    public static NoCommandRunner Instance { get; } = new();

    public CommandOutcome Capture(string command, IReadOnlyList<string> arguments) => throw Unavailable(command);

    public int RunAttached(string command, IReadOnlyList<string> arguments) => throw Unavailable(command);

    private static CommandFailedException Unavailable(string command) => new($"Cannot run \"{command}\": this host does not run commands.");
}

public sealed class ProcessCommandRunner(int outputLimit = TextOperations.MaxLength) : ICommandRunner
{
    private const int FileNotFound = 2;
    private const int WindowsAccessDenied = 5;
    private const int UnixAccessDenied = 13;
    private const int WindowsInvalidParameter = 87;
    private const int UnixInvalidArgument = 22;
    private const int WindowsNameTooLong = 206;
    private const int UnixArgumentsTooLong = 7;
    private const int UnixNameTooLong = 36;

    private static readonly string[] BatchExtensions = [".bat", ".cmd"];

    public static ProcessCommandRunner Instance { get; } = new();

    public CommandOutcome Capture(string command, IReadOnlyList<string> arguments)
    {
        using var process = Started(command, arguments, captures: true);
        CloseInput(process);
        var output = ReadWithinLimit(process.StandardOutput, process);
        var errors = ReadWithinLimit(process.StandardError, process);
        process.WaitForExit();
        var (written, reported) = (output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
        if (written is null || reported is null)
        {
            throw new CommandFailedException($"Cannot run \"{command}\": it wrote more than {outputLimit} characters.");
        }

        return new CommandOutcome(process.ExitCode, WithPlainLineEnds(written), WithPlainLineEnds(reported));
    }

    public int RunAttached(string command, IReadOnlyList<string> arguments)
    {
        using var process = Started(command, arguments, captures: false);
        process.WaitForExit();
        return process.ExitCode;
    }

    private static Process Started(string command, IReadOnlyList<string> arguments, bool captures)
    {
        RefuseBatchFile(command);
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = captures,
            RedirectStandardInput = captures,
            RedirectStandardOutput = captures,
            RedirectStandardError = captures,
        };
        if (captures)
        {
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(start) ?? throw new CommandFailedException($"Cannot run \"{command}\".");
        }
        catch (Win32Exception exception)
        {
            throw new CommandFailedException($"Cannot run \"{command}\": {ReasonFor(exception.NativeErrorCode)}");
        }
    }

    private static void RefuseBatchFile(string command)
    {
        var name = command.TrimEnd(' ', '.');
        if (OperatingSystem.IsWindows() && BatchExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CommandFailedException(
                $"Cannot run \"{command}\" directly: Windows hands a batch file to its shell, which would read the arguments as commands. Run it through cmd with /c.");
        }
    }

    private static string ReasonFor(int error) => error switch
    {
        FileNotFound => "no such program was found.",
        WindowsAccessDenied or UnixAccessDenied => "it is not a program that can be started, or access to it was denied.",
        WindowsInvalidParameter or UnixInvalidArgument => "that is not a valid program name.",
        WindowsNameTooLong or UnixArgumentsTooLong or UnixNameTooLong => "the command is too long.",
        _ => "the system could not start it.",
    };

    private async Task<string?> ReadWithinLimit(StreamReader reader, Process process)
    {
        var text = new StringBuilder();
        var buffer = new char[81920];
        while (true)
        {
            var count = await reader.ReadAsync(buffer);
            if (count == 0)
            {
                return text.ToString();
            }

            if (text.Length + count > outputLimit)
            {
                Stop(process);
                return null;
            }

            text.Append(buffer, 0, count);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static void CloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
    }

    private static string WithPlainLineEnds(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
