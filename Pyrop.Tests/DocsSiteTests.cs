using Pyrop.DocsSite;

namespace Pyrop.Tests;

public sealed class DocsSiteTests
{
    [Fact]
    public void TheDocsBuildIntoASiteWithoutBrokenLinks()
    {
        var root = RepositoryRoot();
        var output = Directory.CreateTempSubdirectory("pyrop-site-").FullName;
        try
        {
            var builder = new SiteBuilder(Path.Combine(root, "docs"), Path.Combine(root, "editors", "vscode", "icons", "pyrop.svg"));
            var problems = builder.Build(output);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.True(File.Exists(Path.Combine(output, "index.html")));
            Assert.True(File.Exists(Path.Combine(output, "site.css")));
            var chapter = File.ReadAllText(Path.Combine(output, "guide", "04-functions.html"));
            Assert.Contains("<a class=\"docs-pager-link next\" href=\"../guide/05-lists-and-maps.html\" rel=\"next\">", chapter);
            Assert.Contains("<a class=\"docs-pager-link previous\" href=\"../guide/03-decisions-and-loops.html\" rel=\"prev\">", chapter);
            var lastChapter = File.ReadAllText(Path.Combine(output, "guide", "13-two-finished-programs.html"));
            Assert.Contains("<a class=\"docs-pager-link next\" href=\"../reference/index.html\" rel=\"next\">", lastChapter);
            var statementPage = File.ReadAllText(Path.Combine(output, "reference", "statements", "if.html"));
            Assert.Contains("<a class=\"docs-pager-link next\" href=\"../../reference/statements/if-some.html\" rel=\"next\">", statementPage);
            Assert.Contains(SiteBuilder.BootstrapCss, chapter);
            Assert.Contains(SiteBuilder.BootstrapJs, chapter);
            Assert.Contains("<a class=\"docs-nav-link active\" href=\"../guide/04-functions.html\" aria-current=\"page\">Functions</a>", chapter);
            Assert.Contains("<details><summary><a class=\"docs-nav-link\" href=\"../reference/statements/index.html\">Statements</a></summary>", chapter);
            var statement = File.ReadAllText(Path.Combine(output, "reference", "statements", "if.html"));
            Assert.Contains("<details open><summary><a class=\"docs-nav-link\" href=\"../../reference/statements/index.html\">Statements</a></summary>", statement);
            Assert.Contains("<li class=\"docs-nav-group\">Control flow</li>", statement);
            var reference = File.ReadAllText(Path.Combine(output, "reference", "quick-reference.html"));
            Assert.Contains("<table class=\"table table-bordered table-sm\">", reference);
            Assert.Contains($"Pyrop {SiteBuilder.Version}", chapter);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void TheSearchIndexCoversEveryPageAndLeadsToHeadingsThatExist()
    {
        var root = RepositoryRoot();
        var output = Directory.CreateTempSubdirectory("pyrop-site-").FullName;
        try
        {
            var builder = new SiteBuilder(Path.Combine(root, "docs"), Path.Combine(root, "editors", "vscode", "icons", "pyrop.svg"));
            Assert.Empty(builder.Build(output));
            Assert.True(File.Exists(Path.Combine(output, "search.js")));
            var script = File.ReadAllText(Path.Combine(output, "search-index.js"));
            const string prefix = "window.pyropSearchIndex=";
            Assert.StartsWith(prefix, script);
            using var index = System.Text.Json.JsonDocument.Parse(script[prefix.Length..].TrimEnd().TrimEnd(';'));
            var urls = index.RootElement.EnumerateArray().Select(entry => entry.GetProperty("u").GetString()!).ToList();
            var pages = Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/'))
                .ToList();
            Assert.All(pages, page => Assert.Contains(urls, url => url == page || url.StartsWith(page + "#", StringComparison.Ordinal)));
            foreach (var url in urls.Where(url => url.Contains('#')))
            {
                var hash = url.IndexOf('#');
                Assert.Contains($"id=\"{url[(hash + 1)..]}\"", File.ReadAllText(Path.Combine(output, url[..hash])));
            }

            Assert.Contains("reference/operations/files.html#when-a-file-is-missing", urls);
            var chapter = File.ReadAllText(Path.Combine(output, "guide", "04-functions.html"));
            Assert.Contains("<form class=\"docs-search\" role=\"search\" data-root=\"../\" hidden>", chapter);
            Assert.Contains("<script src=\"../search.js\"></script>", chapter);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void SearchEntriesAreSectionsWithTheirWordsButNotTheirOutput()
    {
        var document = SiteBuilder.Parse(
            "# The page\n\nIntro with `code`.\n\n## First part\n\n| a | b |\n|---|---|\n| cell | more |\n\n```pyrop\nwrite_line \"shown\"\n```\n\n```output\nhidden\n```\n\n#### Small heading\n\n- a list item\n");
        Assert.Equal(
            [
                new SearchEntry("page.html", "The page", "The page", "Intro with code."),
                new SearchEntry("page.html#first-part", "The page", "First part", "a b cell more write_line \"shown\" Small heading a list item"),
            ],
            SearchIndex.EntriesOf("page.html", document));
    }

    [Fact]
    public void BrokenLinksAndAnchorsStopTheBuild()
    {
        var docs = Directory.CreateTempSubdirectory("pyrop-docs-").FullName;
        var output = Path.Combine(docs, "out");
        try
        {
            File.WriteAllText(Path.Combine(docs, "index.md"), "# Home\n\n[Here](other.md)\n\n[Missing](gone.md)\n\n[Bad anchor](other.md#nowhere)\n");
            File.WriteAllText(Path.Combine(docs, "other.md"), "# Other\n\n[Home](index.md#home)\n");
            var icon = Path.Combine(docs, "icon.svg");
            File.WriteAllText(icon, "<svg/>");
            var problems = new SiteBuilder(docs, icon).Build(output);
            Assert.Equal(
                ["index.md:5: the link gone.md leads to a page that does not exist", "index.md:7: the link other.md#nowhere names a heading that other.md does not have"],
                problems.Select(problem => problem.ToString()));
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(docs, recursive: true);
        }
    }

    [Fact]
    public void ExamplesAreColouredByWhatEachWordIs()
    {
        var html = Highlighter.HighlightProgram(
            "function int square int x return multiply x x\ndefine int y 3 # a comment\nwrite_line square y\n",
            new Dictionary<string, string>());
        Assert.Contains("<span class=\"k\">function</span> <span class=\"t\">int</span> <span class=\"f\">square</span>", html);
        Assert.Contains("<span class=\"b\">multiply</span> <span class=\"p\">x</span>", html);
        Assert.Contains("<span class=\"n\">y</span> <span class=\"u\">3</span> <span class=\"c\"># a comment</span>", html);
        Assert.Contains("<span class=\"f\">square</span> <span class=\"n\">y</span>", html);
    }

    [Fact]
    public void ModulesOfThePageAreUsedForItsExamples()
    {
        var html = Highlighter.HighlightProgram(
            "use shapes \"lib/shapes.prp\"\nwrite_line from shapes double 2\n",
            new Dictionary<string, string> { ["lib/shapes.prp"] = "export function int double int x return multiply 2 x\n" });
        Assert.Contains("<span class=\"k\">from</span> <span class=\"m\">shapes</span> <span class=\"f\">double</span>", html);
    }

    [Fact]
    public void TemplatesShowPlaceholdersAndOtherTextStaysPlain()
    {
        Assert.Equal(
            "<span class=\"k\">if</span> <span class=\"ph\">&lt;condition&gt;</span> <span class=\"ph\">&lt;statement&gt;</span>",
            Highlighter.HighlightTemplate("if <condition> <statement>"));
        Assert.Equal("pyrop --build tool.prp", Highlighter.HighlightTemplate("pyrop --build tool.prp"));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pyrop.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository was not found.");
    }
}
