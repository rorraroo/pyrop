using Pyrop.Cli;
using Pyrop.Parsing;

namespace Pyrop.Tests;

public sealed class ParseRecoveryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-recovery-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Save(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static PyropSession Session() => new(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true));

    private IReadOnlyList<PyropError> FailuresOf(string source)
    {
        var path = Save("main.prp", source);
        return Assert.Throws<PyropException>(() => Session().Run(source, path, path)).Errors;
    }

    private static string[] Lines(IEnumerable<PyropError> errors) => errors.Select(error => $"{error.Position.Line}: {error.Message}").ToArray();

    [Fact]
    public void BrokenBodiesAreEachReported()
    {
        var errors = FailuresOf("function void a\n    write_line 1 2\n\nfunction void b\n    write_line 3\n\nfunction void c\n    write_line 4 5\n");

        Assert.Equal([2, 8], errors.Select(error => error.Position.Line));
    }

    [Fact]
    public void AFunctionAfterAnUnfinishedOneStillParses()
    {
        var errors = FailuresOf("function void a\n\nfunction void b\n    write_line 1\n\nfunction void c\n    write_line 2 3\n");

        Assert.Equal([3, 7], errors.Select(error => error.Position.Line));
    }

    [Fact]
    public void ABlockLeftOpenIsOneError()
    {
        var errors = FailuresOf("function void a\n    block\n        write_line 1\n\nfunction void b\n    write_line 2\n");

        Assert.Equal("Functions can only be defined at the top level.", Assert.Single(errors).Message);
    }

    [Fact]
    public void ABrokenHeaderIsReportedOnceAndItsCallsAddNothing()
    {
        var errors = FailuresOf("function int f int a int a\n    return a\n\nfunction void g\n    f 1\n\nfunction void h\n    f 2\n");

        Assert.Equal("Parameter 'a' is declared twice.", Assert.Single(errors).Message);
    }

    [Fact]
    public void ABrokenHeaderDoesNotHideOtherErrors()
    {
        var errors = FailuresOf("function int f int a int a\n    return a\n\nfunction void g\n    write_line 1 2\n");

        Assert.Equal(["1: Parameter 'a' is declared twice.", "5: '2' cannot stand alone as a statement. This usually means the expression before it was given more arguments than its operation takes."], Lines(errors));
    }

    [Theory]
    [InlineData("block use lib \"lib.prp\" end\n\nfunction void a\n    write_line 1 2\n", 1, 4)]
    [InlineData("function void a\n    block\n        import from nothing x end\n    end\n\nfunction void b\n    write_line 1 2\n", 3, 7)]
    public void AMisplacedDeclarationWordIsNotReportedTwice(string source, int first, int second)
    {
        Assert.Equal([first, second], FailuresOf(source).Select(error => error.Position.Line));
    }

    [Fact]
    public void ALeftOverEndAfterAnErrorIsNotReported()
    {
        var errors = FailuresOf("block function int f return 1 end\n");

        Assert.Equal("Functions can only be defined at the top level.", Assert.Single(errors).Message);
    }

    [Fact]
    public void AStrayEndAsTheFirstErrorIsReported()
    {
        Assert.Equal("'end' without a matching 'block'.", Assert.Single(FailuresOf("end\n")).Message);
    }

    [Fact]
    public void TypeErrorsWaitForParseErrors()
    {
        var errors = FailuresOf("function void a\n    write_line add 1 true\n\nfunction void b\n    write_line 1 2\n");

        Assert.Equal(ErrorStage.Parse, Assert.Single(errors).Stage);
    }

    [Fact]
    public void ErrorsAreReportedInEachFile()
    {
        var module = Save("lib.prp", "export function int one\n    return 1 2\n");
        var main = "use lib \"lib.prp\"\n\nfunction void a\n    write_line 1 2\n";
        var path = Save("main.prp", main);

        var errors = Assert.Throws<PyropException>(() => Session().Run(main, path, path)).Errors;

        Assert.Equal(2, errors.Count);
        Assert.Equal(2, errors.Select(error => error.Position.Source).Distinct().Count());
        Assert.Contains(errors, error => error.Position.Source!.EndsWith("lib.prp", StringComparison.Ordinal) && error.Position.Line == 2);
        Assert.NotNull(module);
    }

    [Fact]
    public void ABrokenHeaderInAModuleIsTheErrorOfItsUseFromElsewhere()
    {
        Save("lib.prp", "export function int one int a int a\n    return a\n");
        var main = "use lib \"lib.prp\"\n\nwrite_line from lib one 1\n";
        var path = Save("main.prp", main);

        var errors = Assert.Throws<PyropException>(() => Session().Run(main, path, path)).Errors;

        Assert.Equal("Parameter 'a' is declared twice.", Assert.Single(errors).Message);
    }

    [Fact]
    public void AMissingOperandBeforeAFunctionDoesNotHideTheFunctions()
    {
        var errors = FailuresOf("function void a\n    write_line add\n\nfunction void b\n    write_line 1 2\n");

        Assert.Equal(["4: Expected an expression, found keyword 'function'.", "5: '2' cannot stand alone as a statement. This usually means the expression before it was given more arguments than its operation takes."], Lines(errors));
    }

    [Fact]
    public void AUseInsideABodyAfterAnErrorIsNotResumedAt()
    {
        var errors = FailuresOf("function void a\n    block\n        write_line 1 2\n        use other \"real.prp\"\n        return void\n    end\n");

        Assert.Equal(3, Assert.Single(errors).Position.Line);
    }

    [Theory]
    [InlineData("def int use 1\nwrite_line use\nfunction void b\n    write_line 1\n")]
    [InlineData("write_line 1 2\nwrite_line import\nwrite_line export\n")]
    public void AReservedWordInSkippedCodeIsNotADeclaration(string source)
    {
        Assert.Single(FailuresOf(source));
    }

    [Fact]
    public void SeveralFailedUsesAreEachReported()
    {
        var errors = FailuresOf("use a \"missing_a.prp\"\nuse b \"missing_b.prp\"\n");

        Assert.Equal([1, 2], errors.Select(error => error.Position.Line));
    }

    [Fact]
    public void TheWordFunctionAloneAtTheEndIsAHeaderFailure()
    {
        const string source = "write_line 1\nfunction";

        Assert.NotNull(Session().Analyze(source, Save("main.prp", source)));
    }

    [Fact]
    public void TypedInputStopsAtItsFirstError()
    {
        Assert.Single(Run.Failures("function void a\n    write_line 1 2\n\nfunction void b\n    write_line 3 4\n"));
    }

    [Fact]
    public void HoverStillWorksInTheFunctionsAfterAnError()
    {
        const string source = "function void a\n    write_line 1 2\n\nfunction int b int x\n    return add x 1\n";

        var analyzed = Session().Analyze(source, Save("main.prp", source));

        Assert.NotNull(analyzed);
        Assert.Contains("add x 1", Pyrop.LanguageServer.HoverProvider.Describe(analyzed, 5, 12)!.Markdown);
    }
}
