using System.Net;
using System.Reflection;
using System.Text;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Pyrop.DocsSite;

public sealed record SiteProblem(string Page, int Line, string Message)
{
    public override string ToString() => $"{Page}:{Line}: {Message}";
}

public sealed class SiteBuilder
{
    private sealed record Page(string RelativePath, MarkdownDocument Document, HashSet<string> Anchors)
    {
        public string OutputPath => Path.ChangeExtension(RelativePath, ".html");
    }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAutoLinks()
        .Build();

    private readonly string _docs;
    private readonly string _icon;

    public SiteBuilder(string docs, string icon)
    {
        _docs = Path.GetFullPath(docs);
        _icon = icon;
    }

    public static string Version
    {
        get
        {
            var version = typeof(PyropSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            return version.Split('+')[0];
        }
    }

    public IReadOnlyList<SiteProblem> Build(string output)
    {
        var pages = Directory.EnumerateFiles(_docs, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(Load)
            .ToDictionary(page => page.RelativePath, StringComparer.Ordinal);
        var problems = pages.Values.SelectMany(page => CheckLinks(page, pages)).ToList();
        if (problems.Count > 0)
        {
            return problems;
        }

        var sections = SectionsOf(pages);
        var order = Navigation.ReadingOrder(sections);
        foreach (var page in pages.Values)
        {
            var target = Path.Combine(output, page.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, Layout(page, sections, order), new UTF8Encoding(false));
        }

        File.WriteAllText(Path.Combine(output, "site.css"), Stylesheet.Text, new UTF8Encoding(false));
        File.Copy(_icon, Path.Combine(output, "pyrop.svg"), overwrite: true);
        return [];
    }

    private Page Load(string path)
    {
        var relative = Path.GetRelativePath(_docs, path).Replace('\\', '/');
        var document = Markdown.Parse(File.ReadAllText(path), Pipeline);
        var anchors = document.Descendants<HeadingBlock>()
            .Select(heading => heading.GetAttributes().Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return new Page(relative, document, anchors);
    }

    private static IEnumerable<SiteProblem> CheckLinks(Page page, IReadOnlyDictionary<string, Page> pages)
    {
        foreach (var link in page.Document.Descendants<LinkInline>())
        {
            if (link.Url is not { } url || IsExternal(url))
            {
                continue;
            }

            var hash = url.IndexOf('#');
            var file = hash < 0 ? url : url[..hash];
            var anchor = hash < 0 ? null : url[(hash + 1)..];
            var line = link.Line + 1;
            Page? target = page;
            if (file.Length > 0)
            {
                var resolved = Navigation.Resolve(page.RelativePath, file);
                if (!file.EndsWith(".md", StringComparison.Ordinal))
                {
                    yield return new SiteProblem(page.RelativePath, line, $"the link {url} does not lead to a page; link to a .md file");
                    continue;
                }

                if (resolved is null || !pages.TryGetValue(resolved, out target))
                {
                    yield return new SiteProblem(page.RelativePath, line, $"the link {url} leads to a page that does not exist");
                    continue;
                }

                link.Url = Path.ChangeExtension(file, ".html") + (anchor is null ? "" : "#" + anchor);
            }

            if (anchor is not null && !target.Anchors.Contains(anchor))
            {
                yield return new SiteProblem(page.RelativePath, line, $"the link {url} names a heading that {target.RelativePath} does not have");
            }
        }
    }

    private static bool IsExternal(string url) => url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.Ordinal);

    private static IReadOnlyList<NavigationItem> SectionsOf(IReadOnlyDictionary<string, Page> pages)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        MarkdownDocument? DocumentOf(string path) => pages.GetValueOrDefault(path)?.Document;
        return new[] { ("guide/index.md", "Guide"), ("reference/index.md", "Reference") }
            .Where(section => pages.ContainsKey(section.Item1))
            .Select(section => new NavigationItem(
                section.Item1,
                section.Item2,
                Navigation.GroupsOf(section.Item1, pages[section.Item1].Document, link => Navigation.Resolve(section.Item1, link), DocumentOf, visited)))
            .ToList();
    }

    private static string Layout(Page page, IReadOnlyList<NavigationItem> sections, IReadOnlyList<NavigationItem> order)
    {
        var root = RootOf(page);
        var title = page.Document.Descendants<HeadingBlock>().FirstOrDefault() is { Inline: { } inline } ? PlainText(inline) : "Pyrop";
        var pageTitle = title == "Pyrop" ? "Pyrop" : $"{title} · Pyrop";
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(pageTitle)}</title>
            <script>{ColorModeScript}</script>
            <link rel="icon" href="{root}pyrop.svg" type="image/svg+xml">
            <link rel="stylesheet" href="{BootstrapCss}" integrity="{BootstrapCssIntegrity}" crossorigin="anonymous">
            <link rel="stylesheet" href="{root}site.css">
            </head>
            <body>
            <nav class="navbar navbar-expand navbar-dark sticky-top site-navbar">
            <div class="container-xxl">
            <button class="btn docs-sidebar-toggle d-lg-none me-2" type="button" data-bs-toggle="offcanvas" data-bs-target="#site-sidebar" aria-controls="site-sidebar" aria-label="Show contents"><span class="navbar-toggler-icon"></span></button>
            <a class="navbar-brand" href="{root}index.html"><img src="{root}pyrop.svg" alt="">Pyrop</a>
            <ul class="navbar-nav me-auto">
            <li class="nav-item"><a class="nav-link{Active(page, "guide/")}" href="{root}guide/index.html"{Current(page, "guide/")}>Guide</a></li>
            <li class="nav-item"><a class="nav-link{Active(page, "reference/")}" href="{root}reference/index.html"{Current(page, "reference/")}>Reference</a></li>
            </ul>
            </div>
            </nav>
            <div class="container-xxl">
            <div class="row">
            <div class="col-lg-3 col-xl-3 docs-sidebar-column">
            <aside class="offcanvas-lg offcanvas-start docs-sidebar" tabindex="-1" id="site-sidebar" aria-labelledby="site-sidebar-title">
            <div class="offcanvas-header">
            <h2 class="offcanvas-title h5" id="site-sidebar-title">Contents</h2>
            <button type="button" class="btn-close" data-bs-dismiss="offcanvas" data-bs-target="#site-sidebar" aria-label="Close"></button>
            </div>
            <div class="offcanvas-body">
            {Navigation.Render(sections, page.RelativePath, root)}
            </div>
            </aside>
            </div>
            <div class="col-lg-9 col-xl-9">
            <main class="docs-content py-4">
            {Content(page)}{Navigation.RenderPager(order, page.RelativePath, RootOf(page))}
            </main>
            <footer class="docs-content pb-4 text-body-secondary small">Pyrop {WebUtility.HtmlEncode(Version)}</footer>
            </div>
            </div>
            </div>
            <script src="{BootstrapJs}" integrity="{BootstrapJsIntegrity}" crossorigin="anonymous"></script>
            <script>{CurrentIntoViewScript}</script>
            </body>
            </html>

            """;
    }

    private static string RootOf(Page page) =>
        string.Concat(Enumerable.Repeat("../", page.RelativePath.Count(character => character == '/')));

    public const string BootstrapCss = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";

    private const string BootstrapCssIntegrity = "sha384-QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH";

    public const string BootstrapJs = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js";

    private const string BootstrapJsIntegrity = "sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz";

    private const string ColorModeScript =
        "(function(){var q=matchMedia('(prefers-color-scheme: dark)');function s(){document.documentElement.setAttribute('data-bs-theme',q.matches?'dark':'light');}s();q.addEventListener('change',s);})();";

    private const string CurrentIntoViewScript =
        "(function(){var s=document.querySelector('.docs-sidebar'),a=s&&s.querySelector('.docs-nav .active');if(a&&a.offsetTop>s.clientHeight-80){s.scrollTop=a.offsetTop-s.clientHeight/2;}})();";

    private static string Active(Page page, string section) =>
        page.RelativePath.StartsWith(section, StringComparison.Ordinal) ? " active" : "";

    private static string Current(Page page, string section) =>
        page.RelativePath.StartsWith(section, StringComparison.Ordinal) ? " aria-current=\"page\"" : "";

    private static string Content(Page page)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        var codeRenderer = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        if (codeRenderer is not null)
        {
            renderer.ObjectRenderers.Remove(codeRenderer);
        }

        renderer.ObjectRenderers.Insert(0, new ExampleBlockRenderer(ModuleFilesOf(page.Document)));
        renderer.Render(page.Document);
        writer.Flush();
        return writer.ToString()
            .Replace("<table>", "<div class=\"table-responsive\"><table class=\"table table-bordered table-sm\">", StringComparison.Ordinal)
            .Replace("</table>", "</table></div>", StringComparison.Ordinal)
            .Replace("<blockquote>", "<blockquote class=\"docs-note\">", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ModuleFilesOf(MarkdownDocument document) =>
        document.Descendants<FencedCodeBlock>()
            .Where(block => block.Info == "prp" && block.Arguments is { Length: > 0 } name && !name.StartsWith("from ", StringComparison.Ordinal))
            .GroupBy(block => block.Arguments!.Trim())
            .ToDictionary(group => group.Key, group => ExampleBlockRenderer.TextOf(group.Last()));

    private static string PlainText(ContainerInline inline)
    {
        var text = new StringBuilder();
        foreach (var child in inline.Descendants<LeafInline>())
        {
            text.Append(child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => "",
            });
        }

        return text.ToString();
    }
}
