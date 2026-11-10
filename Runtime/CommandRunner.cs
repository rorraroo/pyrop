using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private const uint OemCodePage = 1;
    private const long MostBytesPerCharacter = 4;

    private static readonly string[] BatchExtensions = [".bat", ".cmd"];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ProcessCommandRunner Instance { get; } = new();

    public CommandOutcome Capture(string command, IReadOnlyList<string> arguments)
    {
        using var process = Started(command, arguments, captures: true);
        CloseInput(process);
        var output = ReadWithinLimit(process.StandardOutput.BaseStream, process);
        var errors = ReadWithinLimit(process.StandardError.BaseStream, process);
        process.WaitForExit();
        var written = Decoded(command, output.GetAwaiter().GetResult());
        var reported = Decoded(command, errors.GetAwaiter().GetResult());
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

    private async Task<byte[]?> ReadWithinLimit(Stream stream, Process process)
    {
        var bytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
            {
                return bytes.ToArray();
            }

            if (bytes.Length + count > outputLimit * MostBytesPerCharacter)
            {
                Stop(process);
                return null;
            }

            bytes.Write(buffer, 0, count);
        }
    }

    private string? Decoded(string command, byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        var text = DecodedText(command, bytes);
        return text.Length > outputLimit ? null : text;
    }

    private static string DecodedText(string command, byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            return text.StartsWith('﻿') ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return OperatingSystem.IsWindows()
                ? OemText(bytes)
                : throw new CommandFailedException($"Cannot read what \"{command}\" wrote: it is not UTF-8 text.");
        }
    }

    private static string OemText(byte[] bytes)
    {
        var characters = new char[MultiByteToWideChar(OemCodePage, 0, bytes, bytes.Length, null, 0)];
        var written = MultiByteToWideChar(OemCodePage, 0, bytes, bytes.Length, characters, characters.Length);
        return new string(characters, 0, written);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] bytes, int byteCount, [Out] char[]? characters, int characterCount);

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
