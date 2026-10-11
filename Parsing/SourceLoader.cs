using Pyrop.Lexing;

namespace Pyrop.Parsing;

public sealed record SourceFile(string FullPath, string DisplayName, string Text, IReadOnlyList<Token> Tokens)
{
    public string Directory => Path.GetDirectoryName(FullPath)!;
}

public sealed class SourceLoader
{
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, SourceFile> _files = new(PathComparer);
    private readonly Dictionary<string, string> _texts = new();

    public IReadOnlyDictionary<string, string> Texts => _texts;

    public static string Resolve(string path, string baseDirectory) =>
        Path.GetFullPath(Path.Combine(baseDirectory, path));

    public SourceFile Load(string path, string baseDirectory, SourcePosition requestedAt)
    {
        var fullPath = Resolve(path, baseDirectory);
        if (_files.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PyropException(ErrorStage.Parse, $"Cannot include \"{path}\": {DescribeFailure(exception, fullPath)}", requestedAt);
        }

        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        var displayName = relativePath.StartsWith("..") || Path.IsPathRooted(relativePath) ? fullPath : relativePath;
        _texts[displayName] = text;
        var file = new SourceFile(fullPath, displayName, text, Lexer.Tokenize(text, displayName));
        _files[fullPath] = file;
        return file;
    }

    private static string DescribeFailure(Exception exception, string fullPath) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => $"no file at {fullPath}.",
        UnauthorizedAccessException => $"access to {fullPath} was denied.",
        _ => exception.Message,
    };
}

public sealed class IncludeContext(SourceLoader loader, string baseDirectory, HashSet<string> included)
{
    public SourceLoader Loader { get; } = loader;

    public string BaseDirectory { get; } = baseDirectory;

    public HashSet<string> Included { get; } = included;
}
