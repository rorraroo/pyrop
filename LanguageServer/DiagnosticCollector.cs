using Pyrop.Parsing;

namespace Pyrop.LanguageServer;

public sealed record Diagnostic(int Line, int Start, int End, ErrorStage Stage, string Message);

public sealed class DiagnosticCollector
{
    private readonly Dictionary<string, List<Diagnostic>> _files = new(SourceLoader.PathComparer);
    private readonly Dictionary<string, string[]> _lines = new(SourceLoader.PathComparer);

    public IReadOnlyDictionary<string, List<Diagnostic>> Files => _files;

    public void Include(string fullPath) => _files.TryAdd(fullPath, []);

    public void Add(PyropException exception, string checkedPath, Func<string?, string?> sourceText)
    {
        foreach (var error in exception.Errors)
        {
            var path = error.Position.Source ?? checkedPath;
            var diagnostic = DiagnosticOf(error, LinesOf(path, sourceText));
            if (!_files.TryGetValue(path, out var diagnostics))
            {
                diagnostics = [];
                _files[path] = diagnostics;
            }

            if (!diagnostics.Contains(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    private string[] LinesOf(string path, Func<string?, string?> sourceText)
    {
        if (!_lines.TryGetValue(path, out var lines))
        {
            lines = sourceText(path)?.Split('\n') ?? [];
            _lines[path] = lines;
        }

        return lines;
    }

    private static Diagnostic DiagnosticOf(PyropError error, string[] lines)
    {
        var line = Math.Max(0, error.Position.Line - 1);
        var start = Math.Max(0, error.Position.Column - 1);
        var end = start;
        if (line < lines.Length)
        {
            var text = lines[line].TrimEnd('\r');
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }
        }

        return new Diagnostic(line, start, end, error.Stage, error.Message);
    }
}
