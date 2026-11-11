using Pyrop.Cli;

namespace Pyrop.Tests;

public class FormatterTests
{
    private static string Format(string source) =>
        new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true)).Format(source, "main.prp");

    [Theory]
    [InlineData("  write_line 1\n\t\twrite_line 2\n", "write_line 1\nwrite_line 2\n")]
    [InlineData("if true\nwrite_line 1\n   else\n write_line 2\n", "if true\n    write_line 1\nelse\n    write_line 2\n")]
    [InlineData(
        "if false\nwrite_line 1\nelse if true\nwrite_line 2\nelse\nwrite_line 3\n",
        "if false\n    write_line 1\nelse if true\n    write_line 2\nelse\n    write_line 3\n")]
    [InlineData(
        "while false block\nwrite_line 1\nif true block\nwrite_line 2\nend\nend\n",
        "while false block\n    write_line 1\n    if true block\n        write_line 2\n    end\nend\n")]
    [InlineData("if true while false\nwrite_line 1\n", "if true while false\n    write_line 1\n")]
    [InlineData(
        "for int n in list_of int 1 end\nif true\nwrite_line n\n",
        "for int n in list_of int 1 end\n    if true\n        write_line n\n")]
    public void IndentsBodiesByTheirHeadersLine(string source, string expected)
    {
        Assert.Equal(expected, Format(source));
    }

    [Fact]
    public void ABlockOnItsOwnLineStaysAtItsHeadersLevel()
    {
        const string source = "function int one\n    block\n  define int x 1\nreturn x\n    end\nif true write_line one\nelse\n    block\nwrite_line 2\nend\n";

        Assert.Equal(
            "function int one\nblock\n    define int x 1\n    return x\nend\nif true write_line one\nelse\nblock\n    write_line 2\nend\n",
            Format(source));
    }

    [Fact]
    public void MatchBranchesAreOneLevelInAndTheirBodiesOneMore()
    {
        const string source =
            "choice Mode on off end\ndefine Mode mode on\nmatch mode\nwhen on write_line 1\nwhen off\nwrite_line 2\n      end\n";

        Assert.Equal(
            "choice Mode on off end\ndefine Mode mode on\nmatch mode\n    when on write_line 1\n    when off\n        write_line 2\nend\n",
            Format(source));
    }

    [Theory]
    [InlineData("when dot write_line 1\nwhen circle\nc r write_line c\n", "    when dot write_line 1\n    when circle\n        c r write_line c\n")]
    [InlineData("when circle\nc r write_line c\nwhen dot write_line 1\n", "    when circle\n        c r write_line c\n    when dot write_line 1\n")]
    public void AWhenContinuedOnTheNextLineIsOneLevelInFromTheWhen(string branches, string expected)
    {
        const string start = "choice Shape dot circle int int end\ndefine Shape s dot\nmatch s\n";

        Assert.Equal($"{start}{expected}end\n", Format($"{start}{branches}end\n"));
    }

    [Fact]
    public void ListItemsAreOneLevelInAndEndReturnsToTheOpeningLine()
    {
        const string source =
            "define list list int rows list_of list int\nlist_of int\n1\n2\nend\n  list_of int 3 end\n    end\nwrite_line rows\n";

        Assert.Equal(
            "define list list int rows list_of list int\n    list_of int\n        1\n        2\n    end\n    list_of int 3 end\nend\nwrite_line rows\n",
            Format(source));
    }

    [Fact]
    public void AContinuedStatementIsOneLevelInFromItsStart()
    {
        Assert.Equal(
            "if true block\n    define int x add 1\n        multiply 2\n        3\nend\n",
            Format("if true block\ndefine int x add 1\nmultiply 2\n3\nend\n"));
    }

    [Fact]
    public void AWithLineIsOneLevelInFromItsCopy()
    {
        const string source = "record Point int x int y end\nrecord Hero int hp Point pos end\ndefine Hero h Hero 1 Point 2 3\nset h copy h with hp 1\nwith pos copy the pos of h\nwith x 4\nwith y 5 end end\n";

        Assert.Equal(
            "record Point int x int y end\nrecord Hero int hp Point pos end\ndefine Hero h Hero 1 Point 2 3\nset h copy h with hp 1\n    with pos copy the pos of h\n        with x 4\n        with y 5 end end\n",
            Format(source));
    }

    [Fact]
    public void ChoicesImportsAndRecordsContinueOneLevelIn()
    {
        const string source =
            "export choice Shape\ncircle float\nsquare float\nend\nrecord Point\nfloat x\nfloat y\nend\nexport function float area Shape shape\nmatch shape\nwhen circle r return multiply r r\nwhen square s return multiply s s\nend\n";

        Assert.Equal(
            "export choice Shape\n    circle float\n    square float\nend\nrecord Point\n    float x\n    float y\nend\nexport function float area Shape shape\n    match shape\n        when circle r return multiply r r\n        when square s return multiply s s\n    end\n",
            Format(source));
    }

    [Fact]
    public void CommentsTakeTheLevelOfTheNextLineOrStayInTheBody()
    {
        const string source = "while false block\n# first\nwrite_line 1\n        # last in the body\nend\n   # the end\n";

        Assert.Equal(
            "while false block\n    # first\n    write_line 1\n    # last in the body\nend\n# the end\n",
            Format(source));
    }

    [Theory]
    [InlineData("write_line 1   \n   \n\nwrite_line 2\n\n\n", "write_line 1\n\n\nwrite_line 2\n")]
    [InlineData("write_line 1", "write_line 1\n")]
    [InlineData("if true\r\nwrite_line 1\n", "if true\r\n    write_line 1\r\n")]
    [InlineData("", "")]
    [InlineData("\n\n  \n", "")]
    [InlineData("# only a comment", "# only a comment\n")]
    [InlineData("write_line \"a  b\"   # keep   this\n", "write_line \"a  b\"   # keep   this\n")]
    public void TrimsLineEndsAndKeepsEverythingInsideLines(string source, string expected)
    {
        Assert.Equal(expected, Format(source));
    }

    [Fact]
    public void AFileWithTypeErrorsIsFormatted()
    {
        Assert.Equal("if 1\n    write_line add \"a\" 1\n", Format("if 1\nwrite_line add \"a\" 1"));
    }

    [Fact]
    public void AFileThatDoesNotParseIsAnError()
    {
        var error = Assert.Throws<PyropException>(() => Format("write_line add 1"));

        Assert.Equal(ErrorStage.Parse, error.Errors[0].Stage);
    }

    [Fact]
    public void FormattingTwiceChangesNothingMore()
    {
        const string source = "function int f int x\nif greater x 0 block\nreturn x\nend\nelse\nreturn 0\n# done\nwrite_line f 2\n";

        var once = Format(source);

        Assert.Equal(once, Format(once));
    }

    [Fact]
    public void EverySampleIsFormatted()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Pyrop.sln")))
        {
            root = root.Parent!;
        }

        var samples = Directory.EnumerateFiles(Path.Combine(root.FullName, "samples"), "*.prp", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(samples);
        foreach (var sample in samples)
        {
            var source = File.ReadAllText(sample);
            var formatted = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true)).Format(source, sample);
            Assert.True(source == formatted, $"{sample} is not formatted.");
        }
    }
}
