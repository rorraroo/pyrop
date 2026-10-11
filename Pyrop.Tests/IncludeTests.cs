namespace Pyrop.Tests;

public sealed class IncludeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-include-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public IncludeTests()
    {
        _session = new PyropSession(new StringReader(""), _output);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteFile(string relativePath, string text)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string RunFile(string relativePath, string text)
    {
        var path = WriteFile(relativePath, text);
        _session.Run(text, path);
        return _output.ToString();
    }

    private PyropError FailFile(string relativePath, string text)
    {
        var path = WriteFile(relativePath, text);
        return Assert.Single(Assert.Throws<PyropException>(() => _session.Run(text, path)).Errors);
    }

    [Fact]
    public void IncludedFunctionsCanBeCalledAnywhere()
    {
        WriteFile("lib.prp", "function int square int x return multiply x x");

        Assert.Equal("16\n", RunFile("main.prp", "writeline square 4\ninclude \"lib.prp\""));
    }

    [Fact]
    public void IncludedStatementsRunAtTheIncludeOnce()
    {
        WriteFile("lib.prp", "def int counter 10\nwriteline \"lib loaded\"");

        var output = RunFile("main.prp", "writeline \"start\"\ninclude \"lib.prp\"\ninclude \"lib.prp\"\nwriteline counter");

        Assert.Equal("start\nlib loaded\n10\n", output);
    }

    [Fact]
    public void ResolvesPathsRelativeToTheIncludingFile()
    {
        WriteFile("lib/math.prp", "include \"helpers.prp\"\nfunction int triple int x return add x double x");
        WriteFile("lib/helpers.prp", "function int double int x return add x x");

        Assert.Equal("9\n", RunFile("main.prp", "include \"lib/math.prp\"\nwriteline triple 3"));
    }

    [Fact]
    public void CircularIncludesAreHarmless()
    {
        WriteFile("a.prp", "include \"b.prp\"\nfunction int one return 1");
        WriteFile("b.prp", "include \"a.prp\"\nfunction int two return add one one");

        Assert.Equal("2\n", RunFile("main.prp", "include \"a.prp\"\nwriteline two"));
    }

    [Fact]
    public void IncludingTheMainFileIsIgnored()
    {
        Assert.Equal("once\n", RunFile("main.prp", "include \"main.prp\"\nwriteline \"once\""));
    }

    [Fact]
    public void ReportsMissingFilesAtTheInclude()
    {
        var error = FailFile("main.prp", "include \"missing.prp\"");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains("Cannot include \"missing.prp\"", error.Message);
        Assert.EndsWith("main.prp", error.Position.Source);
    }

    [Fact]
    public void ReportsErrorsInsideIncludedFilesWithTheirFile()
    {
        WriteFile("lib.prp", "\nwriteline add 1 true");

        var error = FailFile("main.prp", "include \"lib.prp\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal(2, error.Position.Line);
        Assert.NotNull(_session.SourceText(error.Position.Source));
    }

    [Theory]
    [InlineData("block include \"lib.prp\" end", "only allowed at the top level")]
    [InlineData("function void f include \"lib.prp\"", "only allowed at the top level")]
    [InlineData("def string path \"lib.prp\"\ninclude path", "string literal path")]
    [InlineData("def int shared 0\ninclude \"lib.prp\"", "already declared")]
    public void RejectsInvalidIncludes(string main, string message)
    {
        WriteFile("lib.prp", "def int shared 1");

        var error = FailFile("main.prp", main);

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void RejectsFunctionsDefinedInTwoFiles()
    {
        WriteFile("lib.prp", "function int f return 1");

        Assert.Contains("already defined", FailFile("main.prp", "include \"lib.prp\"\nfunction int f return 2").Message);
    }

    [Fact]
    public void ReplSessionsRememberIncludedFiles()
    {
        var lib = WriteFile("lib.prp", "function int answer return 42\nwriteline \"loaded\"");
        var include = $"include \"{lib.Replace("\\", "\\\\")}\"";

        _session.Run(include);
        _session.Run(include);
        _session.Run("writeline answer");

        Assert.Equal("loaded\n42\n", _output.ToString());
    }

    [Fact]
    public void FailedReplInputsDoNotMarkFilesAsIncluded()
    {
        var lib = WriteFile("lib.prp", "function int answer return 42");
        var include = $"include \"{lib.Replace("\\", "\\\\")}\"";

        Assert.Throws<PyropException>(() => _session.Run($"{include}\nwriteline add 1 true"));
        _session.Run($"{include}\nwriteline answer");

        Assert.Equal("42\n", _output.ToString());
    }
}
