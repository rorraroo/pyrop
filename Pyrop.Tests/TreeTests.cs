using Pyrop.Syntax;

namespace Pyrop.Tests;

public class TreeTests
{
    private static InspectedProgram Inspect(string source) =>
        new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, "main.prp");

    private static string Tree(string source)
    {
        var inspected = Inspect(source);
        Assert.Null(inspected.Error);
        return TreePrinter.Print(inspected.Tree!);
    }

    [Fact]
    public void BracketsEveryCallThatTakesArguments()
    {
        Assert.Equal(
            "def int product 0\nset product (multiply (add 1 2) (subtract 10 4))\n",
            Tree("def int product 0 set product multiply add 1 2 subtract 10 4"));
    }

    [Fact]
    public void LeavesStatementCallsAndCallsWithoutArgumentsBare()
    {
        const string source =
            "function void greet string name write_line name\n" +
            "greet ask \"Name? \"\nwrite_line read\nwrite_line convert int argument_at 0";

        Assert.Equal(
            "function void greet string name\n    write_line name\ngreet (ask \"Name? \")\nwrite_line read\n" +
            "write_line (convert int (argument_at 0))\n",
            Tree(source));
    }

    [Fact]
    public void IndentsBodiesAndChainsElseIf()
    {
        const string source =
            "function int sign int x if greater x 0 return 1 else if less x 0 return -1 else return 0";

        Assert.Equal(
            "function int sign int x\n    if (greater x 0)\n        return 1\n    else if (less x 0)\n        return -1\n    else\n        return 0\n",
            Tree(source));
    }

    [Fact]
    public void KeepsBlocksOnTheirHeaderLine()
    {
        const string source =
            "with_style \"warning\" block while true block if true break else continue end write \"x\" end\n" +
            "block exit 2 end";

        Assert.Equal(
            "with_style \"warning\" block\n    while true block\n        if true\n            break\n        else\n            continue\n    end\n" +
            "    write \"x\"\nend\nblock\n    exit 2\nend\n",
            Tree(source));
    }

    [Fact]
    public void WritesLiteralsAsTheyWouldBeTyped()
    {
        Assert.Equal(
            "write_line \"say \\\"hi\\\"\\t\\\\\\n\"\nwrite_line 2.0\nwrite_line -7\nwrite_line false\n",
            Tree("write_line \"say \\\"hi\\\"\\t\\\\\\n\" write_line 2.0 write_line -7 write_line false"));
    }

    [Fact]
    public void ShowsReturnVoidAndIncludeAsWritten()
    {
        var library = Path.Combine(Directory.CreateTempSubdirectory("pyrop-tree-").FullName, "lib.prp");
        File.WriteAllText(library, "def int answer 42");
        var path = library.Replace('\\', '/');

        Assert.Equal(
            $"function void stop\n    return void\ninclude \"{path}\"\n",
            Tree($"function void stop return void\ninclude \"{path}\""));
        Directory.Delete(Path.GetDirectoryName(library)!, recursive: true);
    }

    [Fact]
    public void ShowsWhatParsedBeforeAParseError()
    {
        var inspected = Inspect("write_line add 1 2\ndef int x multiply 2 3 4\nwrite_line \"after\"");

        Assert.Equal("write_line (add 1 2)\ndef int x (multiply 2 3)\n", TreePrinter.Print(inspected.Tree!));
        var error = Assert.Single(inspected.Error!.Errors);
        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void ShowsTheWholeTreeWithTypeErrors()
    {
        var inspected = Inspect("write_line add 1 \"a\"\nwrite_line 2");

        Assert.Equal("write_line (add 1 \"a\")\nwrite_line 2\n", TreePrinter.Print(inspected.Tree!));
        Assert.Equal(ErrorStage.Type, Assert.Single(inspected.Error!.Errors).Stage);
    }

    [Fact]
    public void ShowsNoTreeForASyntaxError()
    {
        var inspected = Inspect("write_line 1 +");

        Assert.Null(inspected.Tree);
        Assert.Equal(ErrorStage.Lex, Assert.Single(inspected.Error!.Errors).Stage);
    }
}
