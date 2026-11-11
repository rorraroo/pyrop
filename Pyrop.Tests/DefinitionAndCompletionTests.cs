using Pyrop.Cli;
using Pyrop.LanguageServer;

namespace Pyrop.Tests;

public sealed class DefinitionAndCompletionTests : IDisposable
{
    private sealed record Found(int Line, int Start, string? Path)
    {
        public (int, int) Pair() => (Line, Start);
    }

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-definition-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Save(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private AnalyzedSource Analyzed(string source)
    {
        var path = Save("main.prp", source);
        var analyzed = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true)).Analyze(source, path, path);
        Assert.NotNull(analyzed);
        return analyzed;
    }

    private static (int Line, int Column) Marked(ref string source)
    {
        var index = source.IndexOf('|');
        Assert.True(index >= 0, "The source needs a | where the cursor is.");
        var before = source[..index];
        source = source.Remove(index, 1);
        return (before.Count(character => character == '\n') + 1, index - before.LastIndexOf('\n'));
    }

    private Found? Definition(string markedSource)
    {
        var source = markedSource;
        var (line, column) = Marked(ref source);
        return DefinitionProvider.Find(Analyzed(source), line, column) is { } target ? new Found(target.Line + 1, target.Start + 1, target.Path) : null;
    }

    private IReadOnlyList<CompletionEntry> Complete(string markedSource, bool typedSpace = false)
    {
        var source = markedSource;
        var (line, column) = Marked(ref source);
        return CompletionProvider.Complete(Analyzed(source), line, column, typedSpace);
    }

    private IEnumerable<string> Labels(string markedSource, bool typedSpace = false) => Complete(markedSource, typedSpace).Select(entry => entry.Label);

    [Fact]
    public void AFunctionCallGoesToItsHeader()
    {
        Assert.Equal((1, 14), Definition("function int square int x\n    return multiply x x\n\nwrite_line |square 2\n")?.Pair());
    }

    [Fact]
    public void AVariableGoesToItsDef()
    {
        Assert.Equal((1, 12), Definition("define int total 1\nwrite_line |total\n")?.Pair());
    }

    [Fact]
    public void ASetGoesToTheDefOfItsVariable()
    {
        Assert.Equal((1, 12), Definition("define int total 1\nset |total 2\n")?.Pair());
    }

    [Fact]
    public void AParameterGoesToTheParameterList()
    {
        Assert.Equal((1, 25), Definition("function int square int x\n    return multiply |x x\n")?.Pair());
    }

    [Fact]
    public void AnInnerNameIsFoundBeforeAGlobalOfTheSameName()
    {
        Assert.Equal((3, 13), Definition("define int n 1\nfunction void f\n    for int n in list_of int 1 end\n        write_line |n\n")?.Pair());
    }

    [Fact]
    public void AFunctionReadsAGlobalDeclaredLater()
    {
        Assert.Equal((3, 12), Definition("function int f\n    return |later\ndefine int later 1\n")?.Pair());
    }

    [Fact]
    public void ABlockScopeEnds()
    {
        Assert.Equal((1, 12), Definition("define int n 1\nblock\n    define int m 2\n    write_line m\nend\nwrite_line |n\n")?.Pair());
    }

    [Fact]
    public void AnIfSomeValueGoesToItsName()
    {
        Assert.Equal((1, 13), Definition("if_some int v some 1\n    write_line |v\n")?.Pair());
    }

    [Fact]
    public void AWhenNameGoesToItsBinding()
    {
        Assert.Equal((3, 17), Definition("choice Shape circle int square end\nmatch circle 1\n    when circle r write_line |r\n    when square write_line 0\nend\n")?.Pair());
    }

    [Fact]
    public void AFieldGoesToTheFieldOfTheValuesRecord()
    {
        const string source = "record A int x end\nrecord B int x end\ndefine B b B 1\nwrite_line the |x of b\n";

        Assert.Equal((2, 14), Definition(source)?.Pair());
    }

    [Fact]
    public void ARecordTypeAndVariantGoToTheirDeclarations()
    {
        Assert.Equal((1, 8), Definition("record Point int x end\ndefine |Point p Point 1\nwrite_line the x of p\n")?.Pair());
        Assert.Equal((1, 18), Definition("choice Color red green end\ndefine Color c |green\nwrite_line c\n")?.Pair());
    }

    [Fact]
    public void AQualifiedNameGoesToTheOtherFile()
    {
        var library = Save("shapes.prp", "export function int area int side\n    return multiply side side\n");

        var target = Definition("use shapes \"shapes.prp\"\nwrite_line from shapes |area 2\n");

        Assert.Equal(new Found(1, 21, library), target);
    }

    [Fact]
    public void AnImportedNameGoesToTheOtherFile()
    {
        var library = Save("shapes.prp", "export function int area int side\n    return multiply side side\n");

        var target = Definition("use shapes \"shapes.prp\"\nimport from shapes area end\nwrite_line |area 2\n");

        Assert.Equal(new Found(1, 21, library), target);
    }

    [Fact]
    public void AModuleNameGoesToItsFile()
    {
        var library = Save("shapes.prp", "export function int area int side\n    return multiply side side\n");

        Assert.Equal(new Found(1, 1, library), Definition("use |shapes \"shapes.prp\"\nwrite_line from shapes area 2\n"));
    }

    [Theory]
    [InlineData("write_line |add 1 2\n")]
    [InlineData("|write_line 1\n")]
    [InlineData("use math library\nwrite_line from |math absolute 1\n")]
    [InlineData("use math library\nwrite_line from math |absolute 1\n")]
    public void BuiltInsHaveNoDefinition(string source)
    {
        Assert.Null(Definition(source));
    }

    [Fact]
    public void AFunctionAfterABrokenOneStillResolves()
    {
        Assert.Equal((4, 20), Definition("function void a\n    write_line 1 2\n\nfunction int b int x\n    return |x\n")?.Pair());
    }

    [Fact]
    public void ACallableShowsHowManyOperandsItTakes()
    {
        var entries = Complete("function int square int x\n    return x\n\nwrite_line squ|");

        var square = Assert.Single(entries);
        Assert.Equal("square", square.Label);
        Assert.Equal("takes 1", square.Takes);
        Assert.Equal("function int square int x", square.Detail);
    }

    [Fact]
    public void ABuiltInListsItsOverloads()
    {
        var entry = Assert.Single(Complete("write_line ad|"), candidate => candidate.Label == "add");

        Assert.Equal("takes 2", entry.Takes);
        Assert.Equal("add int int → int", entry.Detail);
        Assert.Contains("add string string → string", entry.Documentation);
    }

    [Fact]
    public void ARecordConstructorAndAVariantShowWhatTheyTake()
    {
        var words = Complete("record Point int x int y end\nchoice Shape dot circle int end\nwrite_line |").ToDictionary(entry => entry.Label);
        Assert.Equal("takes 2", words["Point"].Takes);
        Assert.Equal("takes 1", words["circle"].Takes);
        Assert.Null(words["dot"].Takes);
    }

    [Fact]
    public void ANoArgumentOperationTakesNothing()
    {
        Assert.Equal("takes nothing", Assert.Single(Complete("define string s rea|"), entry => entry.Label == "read").Takes);
    }

    [Fact]
    public void LocalsComeFirstAndOnlyThoseDeclaredBeforeTheCursor()
    {
        var labels = Labels("function void f int count\n    define int total 1\n    write_line |\n    define int later 2\n").ToList();

        Assert.Equal(["count", "total"], labels.Take(2));
        Assert.DoesNotContain("later", labels);
    }

    [Fact]
    public void AnotherFunctionsLocalsAreNotListed()
    {
        var labels = Labels("function void g\n    define int other 1\n    write_line other\n\nfunction void f\n    write_line |\n");

        Assert.DoesNotContain("other", labels);
    }

    [Fact]
    public void GlobalsAreListedInsideAFunctionEvenIfDeclaredLater()
    {
        Assert.Contains("later", Labels("function int f\n    return |\n\ndefine int later 1\n"));
    }

    [Fact]
    public void ForIfSomeAndWhenNamesAreListed()
    {
        var labels = Labels("choice S a int b end\nfunction void f S s\n    for int item in list_of int 1 end\n        if_some int got some item\n            match s\n                when a value write_line |\n                when b write_line 1\n            end\n").ToList();

        Assert.Contains("item", labels);
        Assert.Contains("got", labels);
        Assert.Contains("value", labels);
    }

    [Fact]
    public void ModulesAreListedAfterFrom()
    {
        Assert.Equal(["math", "files"], Labels("use math library\nuse files library\nwrite_line from |"));
    }

    [Fact]
    public void ALibrarysOperationsAreListedAfterItsName()
    {
        var entry = Assert.Single(Complete("use math library\nwrite_line from math ab|"));

        Assert.Equal(("absolute", "takes 1"), (entry.Label, entry.Takes));
    }

    [Fact]
    public void OnlyExportedNamesOfAModuleAreListed()
    {
        Save("lib.prp", "export function int shown\n    return 1\n\nfunction int hidden\n    return 2\n");

        Assert.Equal(["shown"], Labels("use lib \"lib.prp\"\nwrite_line from lib |"));
    }

    [Fact]
    public void AnImportListOffersTheModulesNames()
    {
        Save("lib.prp", "export function int one\n    return 1\n\nexport function int two\n    return 2\n");

        Assert.Equal(["two"], Labels("use lib \"lib.prp\"\nimport from lib one tw|"));
    }

    [Fact]
    public void FieldsAreListedAfterTheAndWith()
    {
        const string declarations = "record P int x int y end\ndefine P p P 1 2\n";

        Assert.Equal(["x", "y"], Labels(declarations + "write_line the |"));
        Assert.Equal(["y"], Labels(declarations + "write_line copy p with y|"));
    }

    [Theory]
    [InlineData("define |")]
    [InlineData("define li|")]
    [InlineData("for |")]
    [InlineData("write_line convert |")]
    [InlineData("write_line list_of |")]
    [InlineData("write_line map_of int |")]
    [InlineData("define list |")]
    [InlineData("define map |")]
    [InlineData("define map int |")]
    [InlineData("function list |")]
    [InlineData("function int f list |")]
    [InlineData("write_line convert list |")]
    [InlineData("write_line list_of list |")]
    public void TypesAreListedWhereATypeGoes(string source)
    {
        var labels = Labels(source).ToList();

        Assert.Contains(labels, label => label is "int" or "list");
        Assert.DoesNotContain("add", labels);
    }

    [Fact]
    public void VoidIsOnlyAReturnType()
    {
        Assert.Contains("void", Labels("function vo|"));
        Assert.Contains("void", Labels("function |"));
        Assert.DoesNotContain("void", Labels("define |"));
    }

    [Fact]
    public void DeclaredTypesAreListed()
    {
        Assert.Contains("Point", Labels("record Point int x end\ndefine Po|"));
    }

    [Theory]
    [InlineData("define int |")]
    [InlineData("define list int na|")]
    [InlineData("function int na|")]
    [InlineData("function int f int na|")]
    [InlineData("use na|")]
    [InlineData("use math |")]
    public void NothingIsListedWhereANewNameGoes(string source)
    {
        Assert.Empty(Complete(source));
    }

    [Theory]
    [InlineData("write_line \"ad|\"")]
    [InlineData("# ad|")]
    [InlineData("write_line 1 # ad|")]
    public void NothingIsListedInAStringOrComment(string source)
    {
        Assert.Empty(Complete(source));
    }

    [Fact]
    public void BothTypesAndWordsAfterAHeadersLastParameter()
    {
        var labels = Labels("function void f int a\n    |\n").ToList();

        Assert.Contains("int", labels);
        Assert.Contains("write_line", labels);
        Assert.Contains("a", labels);
    }

    [Fact]
    public void ASpaceOnlyOpensClosedLists()
    {
        Assert.Empty(Complete("write_line |", typedSpace: true));
        Assert.NotEmpty(Complete("write_line the |".Insert(0, "record P int x end\n"), typedSpace: true));
        Assert.Contains("int", Labels("define |", typedSpace: true));
        Assert.DoesNotContain("write_line", Labels("function void f int a\n    |\n", typedSpace: true));
    }

    [Fact]
    public void CompletionWorksInCodeThatDoesNotParse()
    {
        var labels = Labels("function void f int count\n    write_line add coun|\n\nfunction void g\n    write_line 1\n").ToList();

        Assert.Equal(["count"], labels);
    }

    [Fact]
    public void AnUnterminatedStringGivesNothing()
    {
        var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true));

        Assert.Null(session.Analyze("write_line \"open", Save("main.prp", "write_line \"open")));
    }

    [Theory]
    [InlineData("function void f\n    write_line |\n\nfunction void g\n    write_line 1\n")]
    [InlineData("function void f\n    write_line add |\n\nfunction void g\n    write_line 1\n")]
    [InlineData("function void f\n    set |\n\nfunction void g\n    write_line 1\n")]
    public void AnUnfinishedLineAboveAFunctionStillCompletes(string source)
    {
        Assert.Contains("g", Labels(source));
    }

    [Fact]
    public void KeywordsAreListedLast()
    {
        var entries = Complete("define int while_count 1\nwhi|");

        Assert.Equal(["while_count", "while"], entries.OrderBy(entry => entry.Rank).Select(entry => entry.Label));
    }
}
