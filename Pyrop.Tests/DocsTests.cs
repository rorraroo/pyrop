using System.Text;
using Pyrop.Cli;
using Pyrop.Runtime;

namespace Pyrop.Tests;

[CollectionDefinition(nameof(DocsTests), DisableParallelization = true)]
public sealed class DocsCollection;

[Collection(nameof(DocsTests))]
public sealed class DocsTests
{
    private sealed record Block(string Info, string Text, int Line);

    private abstract record Snippet(string Page, int Line, string Source);

    private sealed record ModuleFile(string Page, int Line, string Source, string Name) : Snippet(Page, Line, Source);

    private sealed record Example(string Page, int Line, string Source, string? Input, string? Output, string? Error) : Snippet(Page, Line, Source);

    private sealed record Malformed(string Page, int Line, string Problem) : Snippet(Page, Line, "");

    private sealed record Excerpt(string Page, int Line, string Source, string File) : Snippet(Page, Line, Source);

    [Fact]
    public void EveryPyropSnippetInTheDocsRunsAndShowsItsOutput()
    {
        var docs = DocsDirectory();
        if (docs is null)
        {
            return;
        }

        var failures = new List<string>();
        var count = 0;
        var workingDirectory = Environment.CurrentDirectory;
        foreach (var page in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var directory = Directory.CreateTempSubdirectory("pyrop-docs-").FullName;
            Environment.CurrentDirectory = directory;
            try
            {
                foreach (var snippet in SnippetsOf(page))
                {
                    count++;
                    if (Check(snippet, directory) is { } failure)
                    {
                        failures.Add($"{Path.GetRelativePath(docs, snippet.Page)}:{snippet.Line}: {failure}");
                    }
                }
            }
            finally
            {
                Environment.CurrentDirectory = workingDirectory;
                Directory.Delete(directory, recursive: true);
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {count} snippets failed:\n\n{string.Join("\n\n", failures)}");
    }

    private static string? DocsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pyrop.sln")))
            {
                var docs = Path.Combine(directory.FullName, "docs");
                return Directory.Exists(docs) ? docs : null;
            }
        }

        return null;
    }

    private static IEnumerable<Snippet> SnippetsOf(string page)
    {
        var blocks = BlocksOf(File.ReadAllLines(page));
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var parts = block.Info.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            switch (parts)
            {
                case ["prp", var excerpt] when excerpt.StartsWith("from ", StringComparison.Ordinal):
                    yield return new Excerpt(page, block.Line, block.Text, excerpt["from ".Length..].Trim());
                    continue;
                case ["prp", var name]:
                    yield return new ModuleFile(page, block.Line, block.Text, name.Trim());
                    continue;
                case ["prp"]:
                    yield return new Malformed(page, block.Line, "a ```prp block declares a module file and needs its name, such as ```prp shapes.prp");
                    continue;
                case ["pyrop", _]:
                    yield return new Malformed(page, block.Line, "a ```pyrop block is a runnable example and takes no file name; declare a module file with ```prp <name>");
                    continue;
                case not ["pyrop"]:
                    continue;
            }

            string? input = null, output = null, error = null;
            while (index + 1 < blocks.Count && blocks[index + 1].Info is "input" or "output" or "error")
            {
                index++;
                switch (blocks[index].Info)
                {
                    case "input":
                        input = blocks[index].Text;
                        break;
                    case "output":
                        output = blocks[index].Text;
                        break;
                    default:
                        error = blocks[index].Text.Trim();
                        break;
                }
            }

            yield return new Example(page, block.Line, block.Text, input, output, error);
        }
    }

    private static List<Block> BlocksOf(string[] lines)
    {
        var blocks = new List<Block>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            var info = lines[index][3..].Trim();
            var start = index + 1;
            var text = new StringBuilder();
            for (index++; index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal); index++)
            {
                text.Append(lines[index]).Append('\n');
            }

            blocks.Add(new Block(info, text.ToString(), start));
        }

        return blocks;
    }

    private static string? Check(Snippet snippet, string directory) => snippet switch
    {
        Malformed malformed => malformed.Problem,
        ModuleFile module => Save(module, directory),
        Excerpt excerpt => Compare(excerpt),
        Example example => Run(example, directory),
        _ => null,
    };

    private static string? Compare(Excerpt excerpt)
    {
        var file = Path.Combine(Path.GetDirectoryName(DocsDirectory()!)!, excerpt.File);
        if (!File.Exists(file))
        {
            return $"the excerpt names {excerpt.File}, which does not exist";
        }

        return File.ReadAllText(file).Replace("\r\n", "\n").Contains(excerpt.Source, StringComparison.Ordinal)
            ? null
            : $"the excerpt is no longer in {excerpt.File}:\n{excerpt.Source}";
    }

    private static string? Save(ModuleFile module, string directory)
    {
        var file = Path.Combine(directory, module.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, module.Source);
        return Unformatted(module.Source, file);
    }

    private static string? Unformatted(string source, string path)
    {
        var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true), HostContext.Isolated);
        try
        {
            var formatted = session.Format(source, path, path);
            return formatted == source ? null : $"is not formatted; pyrop --format would make it\n{formatted}";
        }
        catch (PyropException)
        {
            return null;
        }
    }

    private static string? Run(Example example, string directory)
    {
        if (example.Output is null && example.Error is null)
        {
            return "a ```pyrop block is a complete example, so it needs an ```output or ```error block after it";
        }

        var main = Path.Combine(directory, "main.prp");
        File.WriteAllText(main, example.Source);
        if (Unformatted(example.Source, main) is { } unformatted)
        {
            return unformatted;
        }

        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: true, errors: new ConsoleStream(writer, plain: true), sharedScreen: true);
        var session = new PyropSession(new StringReader(example.Input ?? ""), output, HostContext.Isolated with { Commands = ProcessCommandRunner.Instance });
        try
        {
            session.Run(example.Source, main, main);
        }
        catch (PyropException exception)
        {
            var message = exception.Errors[0].Message;
            if (example.Error is null)
            {
                return $"unexpected error: {message}";
            }

            return message == example.Error ? null : $"expected the error\n  {example.Error}\nbut got\n  {message}";
        }

        if (example.Error is not null)
        {
            return $"expected the error\n  {example.Error}\nbut the program ran";
        }

        var actual = writer.ToString().Replace("\r\n", "\n");
        if (actual.Length == 0)
        {
            return "the program writes nothing, so a reader cannot see what it does";
        }

        return actual == example.Output ? null : $"expected the output\n{example.Output}but got\n{actual}";
    }
}
