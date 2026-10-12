using System.Text;

namespace Pyrop.Runtime;

public static class TextFiles
{
    private const int InvalidNameResult = unchecked((int)0x8007007B);
    private const int SharingViolationResult = unchecked((int)0x80070020);
    private const int LockViolationResult = unchecked((int)0x80070021);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private static readonly char[] Separators = [.. new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }.Distinct()];

    public static string Read(IBuiltinInvocation invocation, string path)
    {
        RequireFilePath(invocation, path);
        try
        {
            var bytes = File.ReadAllBytes(path);
            var start = bytes.AsSpan().StartsWith(ByteOrderMark) ? ByteOrderMark.Length : 0;
            return NormalizeLineEndings(StrictUtf8.GetString(bytes, start, bytes.Length - start));
        }
        catch (DecoderFallbackException)
        {
            throw invocation.Fail($"File \"{path}\" is not valid UTF-8 text.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw invocation.Fail($"File \"{path}\" does not exist.");
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw invocation.Fail(FileSystemFailure("read", path, exception));
        }
    }

    public static Value? Write(IBuiltinInvocation invocation, string path, string text, bool append)
    {
        RequireFilePath(invocation, path);
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            throw invocation.Fail($"The text for \"{path}\" contains characters that cannot be written as UTF-8.");
        }

        try
        {
            using var stream = new FileStream(path, append ? FileMode.Append : FileMode.OpenOrCreate, FileAccess.Write);
            if (!append)
            {
                stream.SetLength(0);
            }

            stream.Write(bytes);
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            throw invocation.Fail(MissingDirectory(path));
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw invocation.Fail(FileSystemFailure("write", path, exception));
        }
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static void RequireFilePath(IBuiltinInvocation invocation, string path)
    {
        if (path.Length == 0)
        {
            throw invocation.Fail("A file path cannot be empty.");
        }

        if (path.Any(char.IsControl))
        {
            throw invocation.Fail($"\"{path}\" is not a valid file path; it contains a control character.");
        }

        if (Directory.Exists(path))
        {
            throw invocation.Fail($"\"{path}\" is a directory, not a file.");
        }
    }

    private static string MissingDirectory(string path)
    {
        var directory = path[..Math.Max(path.LastIndexOfAny(Separators), 0)];
        return File.Exists(directory)
            ? $"\"{directory}\" is a file, not a directory, so \"{path}\" cannot be written."
            : $"Directory \"{directory}\" does not exist, so \"{path}\" cannot be written.";
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static string FileSystemFailure(string verb, string path, Exception exception) => exception switch
    {
        UnauthorizedAccessException => $"Access to \"{path}\" was denied.",
        PathTooLongException => $"\"{path}\" is too long to be a file path.",
        IOException { HResult: SharingViolationResult or LockViolationResult } =>
            $"Cannot {verb} \"{path}\" because another process is using it.",
        IOException { HResult: InvalidNameResult } or ArgumentException or NotSupportedException =>
            $"\"{path}\" is not a valid file path.",
        _ => $"Cannot {verb} \"{path}\"; the operating system reported an error.",
    };
}
