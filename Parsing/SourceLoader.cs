using System.Text;
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

    public static string Resolve(string path, string baseDirectory, SourcePosition requestedAt)
    {
        if (path.Length == 0)
        {
            throw CannotUse(path, "the path is empty.", requestedAt);
        }

        if (path.Any(char.IsControl))
        {
            throw CannotUse(path, "the path contains a control character.", requestedAt);
        }

        try
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
        catch (PathTooLongException)
        {
            throw CannotUse(path, "the path is too long.", requestedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw CannotUse(path, "the path is not valid.", requestedAt);
        }
    }

    public SourceFile Load(string path, string baseDirectory, SourcePosition requestedAt)
    {
        var fullPath = Resolve(path, baseDirectory, requestedAt);
        if (_files.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        StoredSource source;
        try
        {
            source = store.Read(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw CannotUse(path, DescribeFailure(exception, fullPath), requestedAt);
        }

        _texts[source.DisplayName] = source.Text;
        var file = new SourceFile(fullPath, source.DisplayName, source.Text, Lexer.Tokenize(source.Text, source.DisplayName));
        _files[fullPath] = file;
        return file;
    }

    private static PyropException CannotUse(string path, string reason, SourcePosition requestedAt) =>
        new(ErrorStage.Parse, $"Cannot use \"{path}\": {reason}", requestedAt);

    private static string DescribeFailure(Exception exception, string fullPath) => exception switch
    {
        _ when Directory.Exists(fullPath) => $"{fullPath} is a folder, not a file.",
        PathTooLongException => "the path is too long.",
        DecoderFallbackException => $"{fullPath} is not UTF-8 text.",
        FileNotFoundException or DirectoryNotFoundException => $"no file at {fullPath}.",
        UnauthorizedAccessException => $"access to {fullPath} was denied.",
        _ => exception.Message,
    };
}

public sealed class ModuleLoading(SourceLoader loader, string baseDirectory, IReadOnlyDictionary<string, LoadedModule> loaded)
{
    public SourceLoader Loader { get; } = loader;

    public string BaseDirectory { get; } = baseDirectory;

    public IReadOnlyDictionary<string, LoadedModule> Loaded { get; } = loaded;
}
