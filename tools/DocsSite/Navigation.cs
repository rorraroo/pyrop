using System.Net;
using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Pyrop.DocsSite;

public sealed record NavigationItem(string Page, string LabelHtml, IReadOnlyList<NavigationGroup> Groups)
{
    public bool Contains(string page) => Page == page || Groups.Any(group => group.Items.Any(item => item.Contains(page)));
}

public sealed record NavigationGroup(string? Label, IReadOnlyList<NavigationItem> Items);

public static class Navigation
{
    public static IReadOnlyList<NavigationGroup> GroupsOf(string indexPage, MarkdownDocument document, Func<string, string?> resolve, Func<string, MarkdownDocument?> documentOf, ISet<string> visited)
    {
        visited.Add(indexPage);
        var groups = new List<NavigationGroup>();
        foreach (var list in document.Descendants<ListBlock>().Where(list => list.Parent is MarkdownDocument))
        {
            var items = new List<NavigationItem>();
            foreach (var entry in list.OfType<ListItemBlock>())
            {
                if (entry.FirstOrDefault() is not ParagraphBlock { Inline.FirstChild: LinkInline link } || link.Url is not { } url)
                {
                    continue;
                }

                var hash = url.IndexOf('#');
                var file = hash < 0 ? url : url[..hash];
                if (file.Length == 0 || resolve(Path.ChangeExtension(file, ".md")) is not { } page || documentOf(page) is not { } target)
                {
                    continue;
                }

                var children = page.EndsWith("/index.md", StringComparison.Ordinal) && !visited.Contains(page)
                    ? GroupsOf(page, target, linked => Resolve(page, linked), documentOf, visited)
                    : [];
                items.Add(new NavigationItem(page, LabelOf(link), children));
            }

            if (items.Count > 0)
            {
                groups.Add(new NavigationGroup(GroupLabelOf(list), items));
            }
        }

        return groups;
    }

    public static string? Resolve(string fromPage, string link)
    {
        var parts = fromPage.Split('/').SkipLast(1).ToList();
        foreach (var part in link.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    public static string Render(IReadOnlyList<NavigationItem> sections, string currentPage, string root)
    {
        var html = new StringBuilder("<nav class=\"docs-nav\" aria-label=\"Contents\">");
        foreach (var section in sections)
        {
            html.Append("<div class=\"docs-nav-section\">");
            html.Append(Link(section, currentPage, root, "docs-nav-heading"));
            AppendGroups(html, section.Groups, currentPage, root);
            html.Append("</div>");
        }

        return html.Append("</nav>").ToString();
    }

    public static IReadOnlyList<NavigationItem> ReadingOrder(IReadOnlyList<NavigationItem> sections)
    {
        var order = new List<NavigationItem>();
        void Add(NavigationItem item)
        {
            order.Add(item);
            foreach (var child in item.Groups.SelectMany(group => group.Items))
            {
                Add(child);
            }
        }

        foreach (var section in sections)
        {
            Add(section);
        }

        return order;
    }

    public static string RenderPager(IReadOnlyList<NavigationItem> order, string currentPage, string root)
    {
        var position = order.Select(item => item.Page).ToList().IndexOf(currentPage);
        if (position < 0)
        {
            return "";
        }

        string LinkTo(NavigationItem item, string caption, string css) =>
            $"<a class=\"docs-pager-link {css}\" href=\"{root}{Path.ChangeExtension(item.Page, ".html")}\" rel=\"{(css == "next" ? "next" : "prev")}\">"
            + $"<span class=\"docs-pager-caption\">{caption}</span><span class=\"docs-pager-title\">{item.LabelHtml}</span></a>";

        var html = new StringBuilder("<nav class=\"docs-pager\" aria-label=\"Previous and next page\">");
        if (position > 0)
        {
            html.Append(LinkTo(order[position - 1], "← Previous", "previous"));
        }

        if (position + 1 < order.Count)
        {
            html.Append(LinkTo(order[position + 1], "Next →", "next"));
        }

        return html.Append("</nav>").ToString();
    }

    private static void AppendGroups(StringBuilder html, IReadOnlyList<NavigationGroup> groups, string currentPage, string root)
    {
        if (groups.Count == 0)
        {
            return;
        }

        html.Append("<ul class=\"list-unstyled\">");
        foreach (var group in groups)
        {
            if (group.Label is not null)
            {
                html.Append($"<li class=\"docs-nav-group\">{WebUtility.HtmlEncode(group.Label)}</li>");
            }

            foreach (var item in group.Items)
            {
                html.Append("<li>");
                if (item.Groups.Count > 0)
                {
                    html.Append(item.Contains(currentPage) ? "<details open>" : "<details>");
                    html.Append("<summary>").Append(Link(item, currentPage, root, "docs-nav-link")).Append("</summary>");
                    AppendGroups(html, item.Groups, currentPage, root);
                    html.Append("</details>");
                }
                else
                {
                    html.Append(Link(item, currentPage, root, "docs-nav-link"));
                }

                html.Append("</li>");
            }
        }

        html.Append("</ul>");
    }

    private static string Link(NavigationItem item, string currentPage, string root, string css)
    {
        var current = item.Page == currentPage;
        var href = root + Path.ChangeExtension(item.Page, ".html");
        return $"<a class=\"{css}{(current ? " active" : "")}\" href=\"{href}\"{(current ? " aria-current=\"page\"" : "")}>{item.LabelHtml}</a>";
    }

    private static string LabelOf(ContainerInline container)
    {
        var html = new StringBuilder();
        foreach (var inline in container)
        {
            html.Append(inline switch
            {
                LiteralInline literal => WebUtility.HtmlEncode(literal.Content.ToString()),
                CodeInline code => $"<code>{WebUtility.HtmlEncode(code.Content)}</code>",
                ContainerInline nested => LabelOf(nested),
                _ => "",
            });
        }

        return html.ToString();
    }

    private static string? GroupLabelOf(ListBlock list)
    {
        if (list.Parent is not { } parent)
        {
            return null;
        }

        var position = parent.IndexOf(list);
        return position > 0 && parent[position - 1] is ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } strong }
            ? PlainText(strong)
            : null;
    }

    private static string PlainText(ContainerInline container) =>
        string.Concat(container.Select(inline => inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            ContainerInline nested => PlainText(nested),
            _ => "",
        }));
}
