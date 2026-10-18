namespace Pyrop.Parsing;

public sealed record StoredSource(string Text, string DisplayName);

public interface ISourceStore
{
    StoredSource Read(string fullPath);

    string DisplayNameOf(string fullPath);
}

public sealed class FileSourceStore : ISourceStore
{
    public static FileSourceStore Instance { get; } = new();

    public StoredSource Read(string fullPath) => new(File.ReadAllText(fullPath), DisplayNameOf(fullPath));

    public string DisplayNameOf(string fullPath)
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        return relativePath.StartsWith("..") || Path.IsPathRooted(relativePath) ? fullPath : relativePath;
    }
}
