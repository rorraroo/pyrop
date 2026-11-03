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
            "greet ask \"Name? \"\nwrite_line read\nuse program library\nwrite_line convert int get from program arguments 0";

        Assert.Equal(
            "function void greet string name\n    write_line name\ngreet (ask \"Name? \")\nwrite_line read\n" +
            "use program library\nwrite_line (convert int (get from program arguments 0))\n",
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
    public void ShowsReturnVoidAndModulesAsWritten()
    {
        var directory = Directory.CreateTempSubdirectory("pyrop-tree-").FullName;
        var library = Path.Combine(directory, "geometry.prp");
        File.WriteAllText(
            library,
            "export function float distance float x float y return add x y\n" +
            "export function int answer return 42\nexport function void greet write_line \"hi\"");
        var path = library.Replace('\\', '/');
        const string program =
            "import from geometry distance end\nexport function void stop return void\n" +
            "write_line from geometry distance 3.0 distance 1.0 2.0\nwrite_line from geometry answer\nfrom geometry greet";

        try
        {
            Assert.Equal(
                $"use geometry \"{path}\"\nimport from geometry distance end\nexport function void stop\n    return void\n" +
                "write_line (from geometry distance 3.0 (distance 1.0 2.0))\nwrite_line from geometry answer\nfrom geometry greet\n",
                Tree($"use geometry \"{path}\"\n{program}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    public void BracketsCollectionFormsAndPrintsTypesInFull()
    {
        const string source =
            "def map string list int m map_of string list int \"k\" list_of int 7 end end\n" +
            "def list list int a list_of list int\n    list_of int 1 add 1 1 end\n    list_of int end\nend\n" +
            "function list string names map string list int scores return keys_of scores\n" +
            "set m put m \"x\" get a 0\nwrite_line list_of string \"a\\\"b\" end";

        Assert.Equal(
            "def map string list int m (map_of string list int \"k\" (list_of int 7 end) end)\n" +
            "def list list int a (list_of list int (list_of int 1 (add 1 1) end) (list_of int end) end)\n" +
            "function list string names map string list int scores\n    return (keys_of scores)\n" +
            "set m (put m \"x\" (get a 0))\n" +
            "write_line (list_of string \"a\\\"b\" end)\n",
            Tree(source));
    }

    [Fact]
    public void PrintsDeeplyNestedProgramsWithoutRunningOutOfStack()
    {
        const int depth = 20_000;
        var source = "use math library\nimport from math abs end\n" +
            $"write_line {string.Concat(Enumerable.Repeat("abs ", depth))}1\n" +
            $"def int n length_of list_of {string.Concat(Enumerable.Repeat("list ", depth))}int end";

        var inspected = Inspect(source);

        Assert.Null(inspected.Error);
        Assert.StartsWith("use math library\nimport from math abs end\nwrite_line (abs (abs ", inspected.Text);
        Assert.EndsWith(" list int end))\n", inspected.Text);
    }

    [Fact]
    public void ShowsNoTreeForASyntaxError()
    {
        var inspected = Inspect("write_line 1 +");

        Assert.Null(inspected.Tree);
        Assert.Equal(ErrorStage.Lex, Assert.Single(inspected.Error!.Errors).Stage);
    }
}
