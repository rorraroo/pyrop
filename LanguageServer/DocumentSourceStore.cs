using Pyrop.Parsing;

namespace Pyrop.LanguageServer;

public sealed class DocumentSourceStore(IReadOnlyDictionary<string, string> documents) : ISourceStore
{
    public StoredSource Read(string fullPath) =>
        new(documents.TryGetValue(fullPath, out var text) ? text : File.ReadAllText(fullPath), fullPath);

    public string DisplayNameOf(string fullPath) => fullPath;
}
