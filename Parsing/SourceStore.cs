namespace Pyrop.Parsing;

public sealed record StoredSource(string Text, string DisplayName);

public interface ISourceStore
{
    StoredSource Read(string fullPath);
}

public sealed class FileSourceStore : ISourceStore
{
    public static FileSourceStore Instance { get; } = new();

    public StoredSource Read(string fullPath)
    {
        var text = File.ReadAllText(fullPath);
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        var displayName = relativePath.StartsWith("..") || Path.IsPathRooted(relativePath) ? fullPath : relativePath;
        return new StoredSource(text, displayName);
    }
}
