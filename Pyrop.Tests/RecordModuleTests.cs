namespace Pyrop.Tests;

public sealed class RecordModuleTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-record-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public RecordModuleTests()
    {
        _session = new PyropSession(new StringReader(""), _output);
        WriteFile("geo.prp", Geometry);
        WriteFile("other.prp", "export record Point float x float y\n");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Geometry =
        "export record Point float x float y\n" +
        "record Secret int code\n" +
        "export function Point origin return Point 0.0 0.0\n" +
        "export function float sum Point p return add get_field x p get_field y p\n";

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
    public void AQualifiedRecordIsATypeAndAConstructor()
    {
        const string source = """
            use geo "geo.prp"
            def from geo Point a from geo Point 3.0 4.0
            def list from geo Point all list_of from geo Point a from geo origin end
            write_line from geo sum a
            write_line all
            write_line equal item_at all 1 from geo Point 0.0 0.0
            """;

        Assert.Equal("7.0\n[Point x 3.0 y 4.0, Point x 0.0 y 0.0]\ntrue\n", RunFile(source));
    }

    [Fact]
    public void AnImportedRecordWorksUnqualified()
    {
        const string source = """
            def Point a Point 1.0 2.0
            use geo "geo.prp"
            import from geo Point
            function Point twice Point p return Point multiply 2.0 get_field x p multiply 2.0 get_field y p
            write_line twice a
            write_line equal a from geo Point 1.0 2.0
            """;

        Assert.Equal("Point x 2.0 y 4.0\ntrue\n", RunFile(source));
    }

    [Fact]
    public void AParameterListLooksAtTheWholeQualifiedName()
    {
        const string source = """
            use geo "geo.prp"
            function float scaled from geo Point p float factor return multiply factor from geo sum p
            function void show float value from geo sum from geo Point value 1.0
            write_line scaled from geo Point 1.0 2.0 2.0
            show 5.0
            """;

        Assert.Equal("6.0\n", RunFile(source));
    }

    [Fact]
    public void AQualifiedFunctionAfterTheParametersStartsTheBody()
    {
        const string source = """
            use geo "geo.prp"
            function void show from geo Point p from geo sum p
            show from geo origin
            write_line "done"
            """;

        Assert.Equal("done\n", RunFile(source));
    }

    [Fact]
    public void RecordsWithTheSameNameInTwoModulesAreDifferentTypes()
    {
        var error = FailFile("use geo \"geo.prp\"\nuse other \"other.prp\"\ndef from geo Point a from other Point 1.0 2.0");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal((3, 22), (error.Position.Line, error.Position.Column));
        Assert.Matches(
            @"^Cannot initialize Point \(declared in .*geo\.prp\) variable 'a' with a value of type Point \(declared in .*other\.prp\)\.$",
            error.Message);
    }

    [Fact]
    public void AmbiguousNamesAreQualifiedInsideCompoundTypes()
    {
        var error = FailFile(
            "use geo \"geo.prp\"\nuse other \"other.prp\"\nwrite_line equal list_of from geo Point end list_of from other Point end");

        Assert.Matches(
            @"^'equal' cannot take list Point \(declared in .*geo\.prp\) list Point \(declared in .*other\.prp\)\. It accepts: T T",
            error.Message);
    }

    [Fact]
    public void AModuleRecordAndALocalRecordWithTheSameNameAreDifferent()
    {
        var error = FailFile("use geo \"geo.prp\"\nrecord Point float x float y\ndef Point p from geo origin");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Matches(@"^Cannot initialize Point \(declared in .*main\.prp\) variable 'p' with a value of type Point \(declared in .*geo\.prp\)\.$", error.Message);
    }

    [Theory]
    [InlineData("use geo \"geo.prp\"\ndef from geo Secret s 1", 2, 14, "Record 'Secret' of module 'geo' is not exported; mark its declaration in")]
    [InlineData("use geo \"geo.prp\"\nimport from geo Secret", 2, 17, "Record 'Secret' of module 'geo' is not exported; mark its declaration in")]
    [InlineData("use geo \"geo.prp\"\ndef from geo sum s 1", 2, 14, "'sum' of module 'geo' is a function, not a type.")]
    [InlineData("use geo \"geo.prp\"\ndef from geo Line s 1", 2, 14, "Module 'geo' has no record or choice 'Line'.")]
    [InlineData("use geo \"geo.prp\"\nwrite_line from geo Line 1", 2, 21, "Module 'geo' has no function, record, choice or variant 'Line'.")]
    [InlineData("def from shapes Point p 1", 1, 10, "'shapes' is not a module of this file; load it with use shapes \"<path>\".")]
    [InlineData("use geo \"geo.prp\"\nimport from geo Point\nrecord Point int x", 2, 17, "'Point' is a record of this file and cannot also be imported.")]
    [InlineData("use Point \"geo.prp\"\nrecord Point int x", 2, 8, "'Point' is a module name in this file and cannot also name a record.")]
    [InlineData("use geo \"geo.prp\"\nimport from geo Point\ndef int Point 1", 3, 9, "'Point' is an imported record and cannot be used as a variable name.")]
    [InlineData("use geo \"nope.prp\"\nrecord R from geo Point p", 1, 9, "Cannot use \"nope.prp\": no file at")]
    [InlineData("use geo \"geo.prp\"\nimport from geo Pointt\nrecord R Pointt p", 2, 17, "Module 'geo' has no function, record, choice or variant 'Pointt'.")]
    [InlineData("use geo \"geo.prp\"\nimport from geo Pointt\nfunction void f Pointt p write_line 1", 2, 17, "Module 'geo' has no function, record, choice or variant 'Pointt'.")]
    public void ReportsWrongQualifiedRecords(string source, int line, int column, string message)
    {
        var error = FailFile(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith(message, error.Message);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void AQualifiedConstructorCannotBeAStatement()
    {
        var error = FailFile("use geo \"geo.prp\"\nfrom geo Point 1.0 2.0");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal((2, 1), (error.Position.Line, error.Position.Column));
        Assert.StartsWith("A record constructor cannot be a statement: 'from geo Point …' would make a record", error.Message);
        Assert.EndsWith("as in def from geo Point <name> from geo Point ….", error.Message);
    }

    [Fact]
    public void AModuleCannotPassOnAnImportedRecord()
    {
        WriteFile("relay.prp", "use geo \"geo.prp\"\nimport from geo Point\nexport function Point make return Point 1.0 1.0\n");

        var error = FailFile("use relay \"relay.prp\"\ndef from relay Point p from relay make");

        Assert.Equal("Module 'relay' only imports 'Point' and cannot pass it on; use the module that exports it.", error.Message);
    }

    [Theory]
    [InlineData("export record Hero list Stats stats\nrecord Stats int strength\n", 1, 15, "Exported record 'Hero' uses the record 'Stats' in its field 'stats', but 'Stats' is not exported; export it too.")]
    [InlineData("record Stats int strength\nexport function void best map string Stats all return void\n", 2, 22, "Exported function 'best' uses the record 'Stats', but 'Stats' is not exported; export it too.")]
    public void AModuleCannotExportDeclarationsThatExposeAPrivateRecord(string module, int line, int column, string message)
    {
        WriteFile("stats.prp", module);

        var error = FailFile("use stats \"stats.prp\"\nwrite_line 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        Assert.EndsWith("stats.prp", error.Position.Source);
    }

    [Fact]
    public void ErrorsInsideAModuleNameTheirFile()
    {
        WriteFile("broken.prp", "export record Node int value Node next\n");

        var error = FailFile("use broken \"broken.prp\"\nwrite_line 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.EndsWith("broken.prp", error.Position.Source);
        Assert.StartsWith("Record 'Node' contains itself", error.Message);
    }

    [Fact]
    public void TheTreePrintsQualifiedRecordTypes()
    {
        const string source = """
            use geo "geo.prp"
            use other "other.prp"
            import from other Point
            function from geo Point flip Point p return from geo Point get_field y p get_field x p
            def list from geo Point all empty list from geo Point
            """;

        var inspected = _session.Inspect(source, WriteFile("main.prp", source));

        Assert.Null(inspected.Error);
        Assert.Equal(
            "use geo \"geo.prp\"\nuse other \"other.prp\"\nimport from other Point\n" +
            "function from geo Point flip Point p\n    return (from geo Point (get_field y p) (get_field x p))\n" +
            "def list from geo Point all (empty list from geo Point)\n",
            inspected.Text);
    }

    [Fact]
    public void ReplInputsCanUseModuleRecords()
    {
        _session.Run($"use geo \"{Path.Combine(_directory, "geo.prp").Replace("\\", "\\\\")}\"");
        _session.Run("def from geo Point p from geo origin");
        _session.Run("import from geo Point");
        _session.Run("set p Point 2.0 3.0");
        _session.Run("write_line from geo sum p");

        Assert.Equal("5.0\n", _output.ToString());
    }
}
