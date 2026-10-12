using Pyrop.Lexing;

namespace Pyrop.Parsing;

public sealed record SourceFile(string FullPath, string DisplayName, string Text, IReadOnlyList<Token> Tokens)
{
    public string Directory => Path.GetDirectoryName(FullPath)!;
}

public sealed class SourceLoader(ISourceStore store)
{
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, SourceFile> _files = new(PathComparer);
    private readonly Dictionary<string, string> _texts = new();

    public IReadOnlyDictionary<string, string> Texts => _texts;

    public IEnumerable<SourceFile> Files => _files.Values;

    public static string Resolve(string path, string baseDirectory) =>
        Path.GetFullPath(Path.Combine(baseDirectory, path));

    public SourceFile Load(string path, string baseDirectory, SourcePosition requestedAt)
    {
        var fullPath = Resolve(path, baseDirectory);
        if (_files.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        StoredSource source;
        try
        {
            source = store.Read(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PyropException(ErrorStage.Parse, $"Cannot include \"{path}\": {DescribeFailure(exception, fullPath)}", requestedAt);
        }

        _texts[source.DisplayName] = source.Text;
        var file = new SourceFile(fullPath, source.DisplayName, source.Text, Lexer.Tokenize(source.Text, source.DisplayName));
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
