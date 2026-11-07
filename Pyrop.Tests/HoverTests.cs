using Pyrop.Cli;
using Pyrop.LanguageServer;

namespace Pyrop.Tests;

public class HoverTests
{
    private static string? Hover(string source, string word, int occurrence = 1)
    {
        var lines = source.Split('\n');
        var seen = 0;
        for (var line = 0; line < lines.Length; line++)
        {
            for (var column = lines[line].IndexOf(word, StringComparison.Ordinal); column >= 0; column = lines[line].IndexOf(word, column + 1, StringComparison.Ordinal))
            {
                var before = column == 0 || !IsWordCharacter(lines[line][column - 1]);
                var after = column + word.Length == lines[line].Length || !IsWordCharacter(lines[line][column + word.Length]);
                if (before && after && ++seen == occurrence)
                {
                    return At(source, line + 1, column + 1);
                }
            }
        }

        throw new ArgumentException($"'{word}' does not occur {occurrence} times.");
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string? At(string source, int line, int column)
    {
        var analyzed = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true)).Analyze(source, "main.prp");
        Assert.NotNull(analyzed);
        return HoverProvider.Describe(analyzed, line, column)?.Markdown;
    }

    private static string Code(string text) => $"```pyrop\n{text}\n```";

    [Fact]
    public void ABuiltInCallShowsItsGroupingTypeAndOverloads()
    {
        const string source = "def int x 2\nwrite_line add 1 multiply 2 x";

        Assert.Equal(
            $"{Code("add 1 (multiply 2 x)")}\n\nGives `int`.\n\n{Code("add int int → int\nadd float float → float\nadd string string → string")}",
            Hover(source, "add"));
        Assert.StartsWith(Code("multiply 2 x"), Hover(source, "multiply"));
    }

    [Fact]
    public void TheChosenOverloadComesFirst()
    {
        Assert.Contains(Code("add string string → string\nadd int int → int\nadd float float → float"), Hover("write_line add \"a\" \"b\"", "add"));
    }

    [Fact]
    public void AVariableShowsItsTypeWhereItIsUsedAndDeclared()
    {
        const string source = "def list int xs list_of int 1 end\nfor int n in xs write_line n";

        Assert.Equal(Code("list int xs"), Hover(source, "xs"));
        Assert.Equal(Code("list int xs"), Hover(source, "xs", 2));
        Assert.Equal(Code("int n"), Hover(source, "n"));
        Assert.Equal(Code("int n"), Hover(source, "n", 2));
    }

    [Fact]
    public void AFunctionCallShowsTheFunctionsHeader()
    {
        const string source = "function int twice int n\n    return multiply 2 n\nwrite_line twice 4";

        Assert.Equal($"{Code("twice 4")}\n\nGives `int`.\n\n{Code("function int twice int n")}", Hover(source, "twice", 2));
        Assert.Equal(Code("function int twice int n"), Hover(source, "twice"));
        Assert.Equal(Code("int n"), Hover(source, "n"));
        Assert.Equal(Code("int n"), Hover(source, "n", 2));
    }

    [Fact]
    public void AStatementKeywordShowsTheStatementsFirstLine()
    {
        const string source = "if greater 3 2\n    write_line 1\nwhile false write_line 2\ndef int total add 1 2";

        Assert.Equal(Code("if (greater 3 2)"), Hover(source, "if"));
        Assert.Equal(Code("while false"), Hover(source, "while"));
        Assert.Equal(Code("def int total (add 1 2)"), Hover(source, "def"));
        Assert.Equal(Code("int total"), Hover(source, "total"));
    }

    [Theory]
    [InlineData("export record Pt int a int b", "export")]
    [InlineData("export record Pt int a int b", "record")]
    [InlineData("export choice Mode on off end", "export")]
    public void ExportAndTheDeclarationKeywordShowTheDeclaration(string source, string word)
    {
        Assert.Equal(Code(source), Hover(source, word));
    }

    [Fact]
    public void RecordsShowTheirDeclarationAndFieldsTheirType()
    {
        const string source = "record Point float x float y\ndef Point p Point 1.0 2.0\nwrite_line the x of p";

        Assert.Equal(Code("record Point float x float y"), Hover(source, "Point", 2));
        Assert.Equal($"{Code("Point 1.0 2.0")}\n\nGives `Point`.\n\n{Code("record Point float x float y")}", Hover(source, "Point", 3));
        Assert.Equal($"{Code("float x")}\n\nA field of `Point`.", Hover(source, "x", 2));
        Assert.Equal($"{Code("float x")}\n\nA field of `Point`.", Hover(source, "x"));
        Assert.StartsWith($"{Code("the x of p")}\n\nGives `float`.", Hover(source, "the"));
    }

    [Fact]
    public void AVariantShowsItsDataAndChoice()
    {
        const string source = "choice Action go int stay end\ndef Action a go 3";

        Assert.Equal($"{Code("go 3")}\n\nGives `Action`.\n\n{Code("go int → Action")}", Hover(source, "go", 2));
        Assert.Equal(Code("go int → Action"), Hover(source, "go"));
        Assert.Equal(Code("choice Action go int stay end"), Hover(source, "Action", 2));
    }

    [Fact]
    public void AQualifiedLibraryCallIsExplainedFromAnyOfItsWords()
    {
        const string source = "use math library\nwrite_line from math abs -3";
        var expected = $"{Code("from math abs -3")}\n\nGives `int`.\n\n{Code("abs int → int\nabs float → float")}";

        Assert.Equal(expected, Hover(source, "from"));
        Assert.Equal(expected, Hover(source, "math", 2));
        Assert.Equal(expected, Hover(source, "abs"));
        Assert.Equal(Code("use math library"), Hover(source, "math"));
    }

    [Fact]
    public void TypeErrorsDoNotStopHover()
    {
        const string source = "def string s add 1 2\nwrite_line add s true";

        Assert.Contains("Gives `int`.", Hover(source, "add"));
        Assert.Equal(
            $"{Code("add s true")}\n\n{Code("add int int → int\nadd float float → float\nadd string string → string")}",
            Hover(source, "add", 2));
    }

    [Fact]
    public void PastAParseErrorNamesAreStillExplained()
    {
        const string source = "function int one\n    return 1\nwrite_line add 1\nwrite_line one";

        Assert.Equal(Code("function int one"), Hover(source, "one", 2));
        Assert.Equal(Code("add int int → int\nadd float float → float\nadd string string → string"), Hover(source, "add"));
    }

    [Fact]
    public void AParameterNamedLikeAModuleIsTheParameter()
    {
        const string source = "use math library\nfunction int twice int math\n    return multiply 2 math\nwrite_line twice from math abs -3";

        Assert.Equal(Code("use math library"), Hover(source, "math"));
        Assert.Equal(Code("int math"), Hover(source, "math", 2));
        Assert.Equal(Code("int math"), Hover(source, "math", 3));
        Assert.StartsWith(Code("from math abs -3"), Hover(source, "math", 4));
    }

    [Fact]
    public void LiteralsSpacesAndUnknownWordsShowNothing()
    {
        const string source = "write_line true\nwrite_line 12";

        Assert.Null(Hover(source, "true"));
        Assert.Null(At(source, 2, 12));
        Assert.Null(At(source, 1, 11));
    }

    [Fact]
    public void ATypeOfAnotherModuleIsWrittenQualified()
    {
        var directory = Directory.CreateTempSubdirectory("pyrop-hover-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "shapes.prp"), "export record Point float x float y\nexport function Point origin\n    return Point 0.0 0.0\n");
            var main = Path.Combine(directory, "main.prp");
            const string source = "use shapes \"shapes.prp\"\ndef from shapes Point p from shapes origin\nwrite_line p";
            var analyzed = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true)).Analyze(source, main, main);

            var hover = HoverProvider.Describe(analyzed!, 2, 33)?.Markdown;

            Assert.Equal(
                $"{Code("from shapes origin")}\n\nGives `from shapes Point`.\n\n{Code("export function from shapes Point origin")}\n\nDeclared in `{Path.Combine(directory, "shapes.prp")}`.",
                hover);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
