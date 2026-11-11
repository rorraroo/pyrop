using System.Net;
using System.Text;
using Pyrop.Cli;
using Pyrop.LanguageServer;
using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.DocsSite;

public static class Highlighter
{
    private static readonly string VirtualFolder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pyrop-docs-site"));

    private sealed class PageSourceStore(IReadOnlyDictionary<string, string> files) : ISourceStore
    {
        public StoredSource Read(string fullPath) =>
            files.TryGetValue(fullPath, out var text) ? new StoredSource(text, DisplayNameOf(fullPath)) : throw new FileNotFoundException(null, fullPath);

        public string DisplayNameOf(string fullPath) => Path.GetRelativePath(VirtualFolder, fullPath);
    }

    private enum Mode
    {
        Program,
        Template,
    }

    public static string HighlightProgram(string source, IReadOnlyDictionary<string, string> moduleFiles)
    {
        var files = moduleFiles.ToDictionary(pair => FullPathOf(pair.Key), pair => pair.Value, SourceLoader.PathComparer);
        var store = new PageSourceStore(files);
        var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true), store: store);
        Dictionary<(int, int), string> classes = [];
        try
        {
            if (session.Analyze(source, "example.prp", FullPathOf("example.prp")) is { } analysed)
            {
                foreach (var token in SemanticClassifier.Classify(analysed))
                {
                    classes[(token.Line, token.Start)] = ClassOf(token.Kind, token.Modifiers);
                }
            }
        }
        catch (PyropException)
        {
        }

        return Render(source, Mode.Program, classes);
    }

    public static string HighlightTemplate(string source)
    {
        var settled = string.Concat(Segments(source, Mode.Template).Select(segment => segment.Kind == "ph" ? "x" : segment.Text));
        try
        {
            Lexer.Tokenize(settled);
        }
        catch (PyropException)
        {
            return WebUtility.HtmlEncode(source);
        }

        return Render(source, Mode.Template, new Dictionary<(int, int), string>());
    }

    private static string FullPathOf(string name) => Path.GetFullPath(Path.Combine(VirtualFolder, name));

    private static string ClassOf(SemanticKind kind, SemanticModifiers modifiers) => kind switch
    {
        SemanticKind.Function when modifiers.HasFlag(SemanticModifiers.BuiltIn) => "b",
        SemanticKind.Function => "f",
        SemanticKind.Type => "t",
        SemanticKind.Variant => "v",
        SemanticKind.Parameter => "p",
        SemanticKind.Variable => "n",
        SemanticKind.Field => "d",
        _ => "m",
    };

    private static string? ClassOfWord(string word) =>
        Builtins.All.ContainsKey(word) ? "b"
        : PyropTypes.Names.Contains(word) ? "t"
        : word is Keywords.True or Keywords.False ? "u"
        : Keywords.IsReserved(word) ? "k"
        : null;

    private sealed record Segment(string Text, string? Kind, int Line, int Column);

    private static string Render(string source, Mode mode, IReadOnlyDictionary<(int, int), string> classes)
    {
        var html = new StringBuilder();
        foreach (var segment in Segments(source, mode))
        {
            var kind = segment.Kind == "w"
                ? classes.GetValueOrDefault((segment.Line, segment.Column)) ?? ClassOfWord(segment.Text)
                : segment.Kind;
            var text = WebUtility.HtmlEncode(segment.Text);
            html.Append(kind is null ? text : $"<span class=\"{kind}\">{text}</span>");
        }

        return html.ToString();
    }

    private static IEnumerable<Segment> Segments(string source, Mode mode)
    {
        var line = 0;
        var column = 0;
        var index = 0;
        var plain = new StringBuilder();
        var plainLine = 0;
        var plainColumn = 0;
        while (index < source.Length)
        {
            var start = index;
            var kind = KindAt(source, index, mode, out var length);
            if (kind is null)
            {
                if (plain.Length == 0)
                {
                    (plainLine, plainColumn) = (line, column);
                }

                plain.Append(source[index]);
                Advance(source[index], ref line, ref column);
                index++;
                continue;
            }

            if (plain.Length > 0)
            {
                yield return new Segment(plain.ToString(), null, plainLine, plainColumn);
                plain.Clear();
            }

            var text = source.Substring(start, length);
            yield return new Segment(text, kind, line, column);
            foreach (var character in text)
            {
                Advance(character, ref line, ref column);
            }

            index += length;
        }

        if (plain.Length > 0)
        {
            yield return new Segment(plain.ToString(), null, plainLine, plainColumn);
        }
    }

    private static void Advance(char character, ref int line, ref int column)
    {
        if (character == '\n')
        {
            line++;
            column = 0;
        }
        else
        {
            column++;
        }
    }

    private static string? KindAt(string source, int index, Mode mode, out int length)
    {
        var character = source[index];
        length = 0;
        if (character == '#')
        {
            length = LengthWhile(source, index, c => c != '\n');
            return "c";
        }

        if (character == '"')
        {
            var end = index + 1;
            while (end < source.Length && source[end] != '"' && source[end] != '\n')
            {
                end += source[end] == '\\' && end + 1 < source.Length && source[end + 1] != '\n' ? 2 : 1;
            }

            length = Math.Min(end + 1, source.Length) - index;
            return "s";
        }

        if (mode == Mode.Template && character == '<')
        {
            var close = source.IndexOf('>', index);
            var newline = source.IndexOf('\n', index);
            if (close > index + 1 && (newline < 0 || close < newline))
            {
                length = close + 1 - index;
                return "ph";
            }
        }

        if (mode == Mode.Template && character == '…')
        {
            length = 1;
            return "ph";
        }

        var startsNumber = char.IsAsciiDigit(character)
            || character == '-' && index + 1 < source.Length && char.IsAsciiDigit(source[index + 1]) && (index == 0 || char.IsWhiteSpace(source[index - 1]));
        if (startsNumber && (index == 0 || !IsWordCharacter(source[index - 1])))
        {
            length = 1 + LengthWhile(source, index + 1, c => char.IsAsciiDigit(c) || c == '.');
            return "u";
        }

        if (char.IsLetter(character) || character == '_')
        {
            length = LengthWhile(source, index, IsWordCharacter);
            return "w";
        }

        return null;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static int LengthWhile(string source, int index, Func<char, bool> condition)
    {
        var end = index;
        while (end < source.Length && condition(source[end]))
        {
            end++;
        }

        return end - index;
    }
}
