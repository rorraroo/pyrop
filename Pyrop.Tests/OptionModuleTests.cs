namespace Pyrop.Tests;

public sealed class OptionModuleTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-option-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public OptionModuleTests()
    {
        _session = new PyropSession(new StringReader(""), _output);
        WriteFile("geo.prp", Geometry);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Geometry =
        "export record Point float x float y end\n" +
        "export function option Point parse_point string text\n" +
        "block\n" +
        "    define list string parts split text \",\"\n" +
        "    if not equal length_of parts 2 return none Point\n" +
        "    if_some float x attempt convert float get parts 0\n" +
        "        if_some float y attempt convert float get parts 1 return some Point x y\n" +
        "    return none Point\n" +
        "end\n" +
        "export function float broken string text return convert float text\n";

    private string WriteFile(string relativePath, string text)
    {
        var path = Path.Combine(_directory, relativePath);
        File.WriteAllText(path, text);
        return path;
    }

    private string RunFile(string text)
    {
        _session.Run(text, WriteFile("main.prp", text));
        return _output.ToString();
    }

    private PyropError FailFile(string text)
    {
        var path = WriteFile("main.prp", text);
        return Assert.Single(Assert.Throws<PyropException>(() => _session.Run(text, path)).Errors);
    }

    [Fact]
    public void OptionsOfAnotherModulesRecordsWork()
    {
        const string source = """
            use geo "geo.prp"
            define option from geo Point found from geo parse_point "1.5,2"
            write_line found
            write_line from geo parse_point "1.5"
            write_line from geo parse_point "a,b"
            if_some from geo Point p found write_line the y of p
            write_line equal none from geo Point from geo parse_point "x"
            """;

        Assert.Equal("some (Point x 1.5 y 2.0)\nnone\nnone\n2.0\ntrue\n", RunFile(source));
    }

    [Fact]
    public void AFailureInsideAModuleFunctionIsNotCaughtByTheCaller()
    {
        var error = FailFile("use geo \"geo.prp\"\nwrite_line attempt add 1.0 from geo broken \"x\"");
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("geo.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal((10, 49), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void AnExportedFunctionCannotExposeAPrivateRecordThroughAnOption()
    {
        WriteFile("hidden.prp", "record Secret int code end\nexport function option Secret lookup return none Secret\n");
        var error = FailFile("use hidden \"hidden.prp\"\nwrite_line 1");
        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(
            "Exported function 'lookup' uses the record 'Secret', but 'Secret' is not exported; export it too.",
            error.Message);
    }

    [Fact]
    public void TreePrintsAnotherModulesOptionTypes()
    {
        const string source = "use geo \"geo.prp\"\nif_some from geo Point p none from geo Point write_line p";
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, WriteFile("main.prp", source));
        Assert.Null(inspected.Error);
        Assert.Equal(
            "use geo \"geo.prp\"\nif_some from geo Point p (none from geo Point)\n    write_line p\n",
            inspected.Text);
    }
}
