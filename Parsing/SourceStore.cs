using System.Text;

namespace Pyrop.Parsing;

public sealed record StoredSource(string Text, string DisplayName);

public interface ISourceStore
{
    StoredSource Read(string fullPath);

    string DisplayNameOf(string fullPath);
}

public sealed class FileSourceStore : ISourceStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static FileSourceStore Instance { get; } = new();

    public static string ReadText(string path)
    {
        using var reader = new StreamReader(path, StrictUtf8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public StoredSource Read(string fullPath) => new(ReadText(fullPath), DisplayNameOf(fullPath));

    public string DisplayNameOf(string fullPath)
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        return relativePath.StartsWith("..") || Path.IsPathRooted(relativePath) ? fullPath : relativePath;
    }
}
