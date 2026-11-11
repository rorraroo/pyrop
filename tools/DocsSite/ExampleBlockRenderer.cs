using System.Net;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Pyrop.DocsSite;

public sealed class ExampleBlockRenderer(IReadOnlyDictionary<string, string> moduleFiles) : HtmlObjectRenderer<CodeBlock>
{
    public static string TextOf(CodeBlock block)
    {
        var lines = block.Lines.Lines.Take(block.Lines.Count).Select(line => line.Slice.ToString());
        return string.Join('\n', lines) + (block.Lines.Count > 0 ? "\n" : "");
    }

    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        var info = block is FencedCodeBlock fenced ? fenced.Info ?? "" : "";
        var arguments = block is FencedCodeBlock withArguments ? withArguments.Arguments?.Trim() ?? "" : "";
        var text = TextOf(block);
        var (css, caption, code) = info switch
        {
            "pyrop" => ("example", null, Highlighter.HighlightProgram(text, moduleFiles)),
            "prp" when arguments.StartsWith("from ", StringComparison.Ordinal) =>
                ("excerpt", $"from {arguments["from ".Length..].Trim()}", Highlighter.HighlightProgram(text, moduleFiles)),
            "prp" => ("module", arguments, Highlighter.HighlightProgram(text, moduleFiles)),
            "input" => ("input", "Input", WebUtility.HtmlEncode(text)),
            "output" => ("output", "Output", WebUtility.HtmlEncode(text)),
            "error" => ("error", "Error", WebUtility.HtmlEncode(text)),
            _ => ("plain", null, Highlighter.HighlightTemplate(text)),
        };

        renderer.EnsureLine();
        renderer.Write($"<figure class=\"code {css}\">");
        if (caption is not null)
        {
            renderer.Write($"<figcaption>{WebUtility.HtmlEncode(caption)}</figcaption>");
        }

        renderer.Write($"<pre><code>{code.TrimEnd('\n')}</code></pre></figure>");
        renderer.WriteLine();
    }
}
