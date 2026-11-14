using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Pyrop.DocsSite;

public sealed record SearchEntry(string Url, string Page, string Heading, string Text);

public static class SearchIndex
{
    private static readonly HashSet<string> LeftOut = new(StringComparer.Ordinal) { "input", "output", "error" };

    public static IEnumerable<SearchEntry> EntriesOf(string outputPath, MarkdownDocument document)
    {
        var page = document.Descendants<HeadingBlock>().FirstOrDefault() is { Inline: { } title } ? PlainText(title) : "Pyrop";
        var heading = page;
        string? anchor = null;
        var text = new StringBuilder();
        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: <= 3 } next)
            {
                if (text.Length > 0 || anchor is not null)
                {
                    yield return Entry(outputPath, anchor, page, heading, text);
                }

                heading = next.Inline is { } inline ? PlainText(inline) : "";
                anchor = next.Level == 1 ? null : next.GetAttributes().Id;
                text.Clear();
                continue;
            }

            AppendText(text, block);
        }

        yield return Entry(outputPath, anchor, page, heading, text);
    }

    public static string Script(IEnumerable<SearchEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("u", entry.Url);
                writer.WriteString("p", entry.Page);
                writer.WriteString("h", entry.Heading);
                writer.WriteString("x", entry.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return "window.pyropSearchIndex=" + Encoding.UTF8.GetString(stream.ToArray()) + ";\n";
    }

    private static SearchEntry Entry(string outputPath, string? anchor, string page, string heading, StringBuilder text) =>
        new(anchor is null ? outputPath : $"{outputPath}#{anchor}", page, heading, Collapse(text.ToString()));

    private static void AppendText(StringBuilder text, Block block)
    {
        switch (block)
        {
            case FencedCodeBlock { Info: { } info } when LeftOut.Contains(info):
                return;
            case CodeBlock code:
                text.Append(' ').Append(ExampleBlockRenderer.TextOf(code));
                return;
            case LeafBlock { Inline: { } inline }:
                text.Append(' ').Append(PlainText(inline));
                return;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    AppendText(text, child);
                }

                return;
        }
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string PlainText(ContainerInline container)
    {
        var text = new StringBuilder();
        foreach (var inline in container)
        {
            text.Append(inline switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                LineBreakInline => " ",
                ContainerInline nested => PlainText(nested),
                _ => "",
            });
        }

        return text.ToString();
    }
}
