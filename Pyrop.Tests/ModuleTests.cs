namespace Pyrop.Tests;

public sealed class ModuleTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-module-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public ModuleTests()
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

    private string Quoted(string relativePath) => $"\"{Path.Combine(_directory, relativePath).Replace("\\", "\\\\")}\"";

    private const string Geometry =
        "export function int square int x return multiply x x\n" +
        "export function int cube int x return multiply x square x\n" +
        "function int hidden return 7\n";

    [Fact]
    public void AForWalksTheResultOfAQualifiedCall()
    {
        WriteFile("items.prp", "export function list string starting_items return list_of string \"lamp\" \"rope\" end\n");

        Assert.Equal(
            "lamp\nrope\n",
            RunFile("main.prp", "use items \"items.prp\"\nfor string item in from items starting_items write_line item"));
    }

    [Fact]
    public void QualifiedFunctionsCanBeCalledAnywhereInTheFile()
    {
        WriteFile("geometry.prp", Geometry);

        Assert.Equal("16\n27\n", RunFile("main.prp", "write_line from geometry square 4\nuse geometry \"geometry.prp\"\nwrite_line from geometry cube 3"));
    }

    [Fact]
    public void AQualifiedNameIsOneWordWithTheFunctionsArity()
    {
        WriteFile("geometry.prp", Geometry);

        Assert.Equal("28\n", RunFile("main.prp", "use geometry \"geometry.prp\"\nwrite_line add from geometry square 1 from geometry cube 3"));
    }

    [Fact]
    public void ImportUnqualifiesOneNameForTheWholeFile()
    {
        WriteFile("geometry.prp", Geometry);

        var output = RunFile(
            "main.prp",
            "write_line square 5\nuse geometry \"geometry.prp\"\nimport from geometry square\nwrite_line from geometry square 2");

        Assert.Equal("25\n4\n", output);
    }

    [Fact]
    public void AQualifiedVoidFunctionCanBeAStatementButNotAValue()
    {
        WriteFile("greeter.prp", "export function void greet string name write_line add \"Hi \" name");

        Assert.Equal("Hi Ann\n", RunFile("main.prp", "use greeter \"greeter.prp\"\nfrom greeter greet \"Ann\""));
        var error = FailFile("other.prp", "use greeter \"greeter.prp\"\nwrite_line from greeter greet \"Bo\"");
        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("Function 'greet' returns void and cannot be used as a value.", error.Message);
        Assert.Equal((2, 12), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void FunctionsArePrivateUnlessExported()
    {
        WriteFile("geometry.prp", Geometry);

        var error = FailFile("main.prp", "use geometry \"geometry.prp\"\nwrite_line from geometry hidden");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith("Function 'hidden' of module 'geometry' is not exported; mark its definition in ", error.Message);
        Assert.EndsWith("geometry.prp with export.", error.Message);
        Assert.Equal((2, 26), (error.Position.Line, error.Position.Column));
        Assert.EndsWith("main.prp", error.Position.Source);
    }

    [Theory]
    [InlineData("use geometry \"geometry.prp\"\nwrite_line from shapes square 2", "'shapes' is not a module of this file; load it with use shapes \"<path>\".")]
    [InlineData("use geometry \"geometry.prp\"\nwrite_line from geometry circle 2", "Module 'geometry' has no function, record, choice or variant 'circle'.")]
    [InlineData("use geometry \"geometry.prp\"\nimport from geometry hidden", "Function 'hidden' of module 'geometry' is not exported; mark its definition in")]
    [InlineData("use geometry \"geometry.prp\"\nimport from shapes square", "'shapes' is not a module of this file")]
    [InlineData("use geometry \"geometry.prp\"\nwrite_line from geometry 5", "Expected a name after 'from geometry', found '5'.")]
    [InlineData("use geometry \"geometry.prp\"\nimport geometry square", "'import' takes a qualified name: import from <module> <name>, found 'geometry'.")]
    [InlineData("use geometry \"geometry.prp\"\nwrite_line geometry", "'geometry' is a module; name one of its exported functions with from geometry <name>.")]
    [InlineData("use geometry \"geometry.prp\"\ngeometry", "'geometry' is a module; name one of its exported functions with from geometry <name>.")]
    public void ReportsWrongQualifiedNames(string main, string message)
    {
        WriteFile("geometry.prp", Geometry);

        var error = FailFile("main.prp", main);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith(message, error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void ImportsAreNotPassedOn()
    {
        WriteFile("geometry.prp", Geometry);
        WriteFile("middle.prp", "use geometry \"geometry.prp\"\nimport from geometry square");

        var error = FailFile("main.prp", "use middle \"middle.prp\"\nwrite_line from middle square 2");

        Assert.Equal("Module 'middle' only imports 'square' and cannot pass it on; use the module that exports it.", error.Message);
    }

    [Fact]
    public void EachFileHasItsOwnFunctionsAndGlobals()
    {
        WriteFile("counter.prp",
            "def int count 0\nfunction int next block set count add count 1 return count end\n" +
            "export function int step return next\nexport function int current return count");

        var output = RunFile(
            "main.prp",
            "use counter \"counter.prp\"\ndef int count 100\nfunction int next return 0\n" +
            "write_line from counter step\nwrite_line from counter step\nwrite_line next\nwrite_line count\nwrite_line from counter current");

        Assert.Equal("1\n2\n0\n100\n2\n", output);
    }

    [Fact]
    public void AModuleCannotSeeAnotherModulesGlobals()
    {
        WriteFile("lib.prp", "export function int peek return secret");

        var error = FailFile("main.prp", "use lib \"lib.prp\"\ndef int secret 1\nwrite_line from lib peek");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("Undeclared variable 'secret'.", error.Message);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
    }

    [Fact]
    public void ModulesRunOnceBeforeTheirUsersDependenciesFirst()
    {
        WriteFile("base.prp", "write_line \"base\"\nexport function int one return 1");
        WriteFile("left.prp", "use base \"base.prp\"\nwrite_line \"left\"\nexport function int two return add from base one 1");
        WriteFile("right.prp", "use base \"base.prp\"\nwrite_line \"right\"\nexport function int three return add from base one 2");

        var output = RunFile(
            "main.prp",
            "write_line \"main\"\nuse r \"right.prp\"\nuse l \"left.prp\"\nuse again \"left.prp\"\nwrite_line add from l two from r three");

        Assert.Equal("base\nright\nleft\nmain\n5\n", output);
    }

    [Fact]
    public void AModuleIsSetUpBeforeACallPlacedAboveItsUse()
    {
        WriteFile("config.prp", "def string greeting \"ready\"\nexport function string read_greeting return greeting");

        Assert.Equal("ready\n", RunFile("main.prp", "write_line from config read_greeting\nuse config \"config.prp\""));
    }

    [Fact]
    public void ResolvesPathsRelativeToTheUsingFile()
    {
        WriteFile("lib/math.prp", "use helpers \"helpers.prp\"\nexport function int triple int x return add x from helpers double x");
        WriteFile("lib/helpers.prp", "export function int double int x return add x x");

        Assert.Equal("9\n", RunFile("main.prp", "use math \"lib/math.prp\"\nwrite_line from math triple 3"));
    }

    [Fact]
    public void ReportsACycleNamingItsFiles()
    {
        WriteFile("a.prp", "use b \"b.prp\"\nexport function int one return 1");
        WriteFile("b.prp", "\nuse a \"a.prp\"\nexport function int two return 2");

        var error = FailFile("main.prp", "use a \"a.prp\"\nwrite_line from a one");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Matches(@"^The modules use each other in a cycle: \S*a\.prp uses \S*b\.prp, and \S*b\.prp uses \S*a\.prp\. Move what they share into a module of its own\.$", error.Message);
        Assert.Equal("b.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal((2, 7), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void ReportsACycleThroughTheMainFile()
    {
        WriteFile("lib.prp", "use main \"main.prp\"");

        var error = FailFile("main.prp", "use lib \"lib.prp\"");

        Assert.Matches(@"cycle: \S*main\.prp uses \S*lib\.prp, and \S*lib\.prp uses \S*main\.prp\.", error.Message);
    }

    [Fact]
    public void AFileUsingItselfIsACycle()
    {
        var error = FailFile("main.prp", "use me \"main.prp\"");

        Assert.Matches(@"cycle: \S*main\.prp uses \S*main\.prp\.", error.Message);
    }

    [Fact]
    public void ReportsMissingFilesAtTheUse()
    {
        var error = FailFile("main.prp", "use lib \"missing.prp\"");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith("Cannot use \"missing.prp\": no file at ", error.Message);
        Assert.EndsWith("main.prp", error.Position.Source);
        Assert.Equal((1, 9), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void ReportsErrorsInsideModulesWithTheirFile()
    {
        WriteFile("lib.prp", "\nwrite_line add 1 true");

        var error = FailFile("main.prp", "use lib \"lib.prp\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal(2, error.Position.Line);
        Assert.NotNull(_session.SourceText(error.Position.Source));
    }

    [Fact]
    public void ReportsParseErrorsInsideModulesAsComplete()
    {
        WriteFile("lib.prp", "write_line add 1");

        var error = FailFile("main.prp", "use lib \"lib.prp\"");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal(InputStatus.Ready, _session.Probe($"use lib {Quoted("lib.prp")}"));
    }

    [Fact]
    public void ReportsRuntimeErrorsInsideModuleFunctionsWithTheirFile()
    {
        WriteFile("lib.prp", "export function int broken int x\n    return divide x 0");

        var error = FailFile("main.prp", "use lib \"lib.prp\"\nwrite_line from lib broken 1");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal(2, error.Position.Line);
    }

    [Theory]
    [InlineData("block use lib \"lib.prp\" end", "'use' is only allowed at the top level.")]
    [InlineData("function void f use lib \"lib.prp\"", "'use' is only allowed at the top level.")]
    [InlineData("use lib \"lib.prp\"\nif true import from lib answer", "'import' is only allowed at the top level.")]
    [InlineData("block export function int f return 1 end", "'export' is only allowed at the top level.")]
    [InlineData("export def int x 1", "'export' must be followed by a function, record or choice definition, found 'def'.")]
    [InlineData("export write_line 1", "'export' must be followed by a function, record or choice definition, found 'write_line'.")]
    [InlineData("def string path \"lib.prp\"\nuse lib path", "'use' takes a module name and then a string literal path: use lib \"<path>\", found 'path'.")]
    [InlineData("use \"lib.prp\"", "Expected a module name after 'use', found string \"lib.prp\".")]
    [InlineData("use if \"lib.prp\"", "'if' is a reserved word and cannot be used as a module name.")]
    [InlineData("function void f use t \"nope.prp\"", "'use' is only allowed at the top level.")]
    [InlineData("block use lib \"main.prp\" end", "'use' is only allowed at the top level.")]
    [InlineData("function int lib return 1\nif true use lib \"lib.prp\"", "'use' is only allowed at the top level.")]
    [InlineData("block import from nothing answer end", "'import' is only allowed at the top level.")]
    [InlineData("use lib \"lib.prp\"\nwhile true import from lib missing", "'import' is only allowed at the top level.")]
    [InlineData("write_line from lib answer\nuse lib \"nope.prp\"", "Cannot use \"nope.prp\": no file at ")]
    [InlineData("use lib \"nope.prp\"\nimport from lib answer", "Cannot use \"nope.prp\": no file at ")]
    [InlineData("use t \"\"", "Cannot use \"\": the path is empty.")]
    [InlineData("use t \"a\tb.prp\"", "Cannot use \"a\tb.prp\": the path contains a control character.")]
    [InlineData("use t \"a\0b.prp\"", "Cannot use \"a\0b.prp\": the path contains a control character.")]
    public void RejectsInvalidModuleStatements(string main, string message)
    {
        WriteFile("lib.prp", "export function int answer return 42");

        var error = FailFile("main.prp", main);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith(message, error.Message);
    }

    [Theory]
    [InlineData("use lib \"lib.prp\"\nuse lib \"other.prp\"", "Module name 'lib' is already used in this file.", 2, 5)]
    [InlineData("use lib \"lib.prp\"\nimport from lib answer\nimport from lib answer", "'answer' is already imported in this file.", 3, 17)]
    [InlineData("use lib \"lib.prp\"\nuse other \"other.prp\"\nimport from lib answer\nimport from other answer", "'answer' is already imported in this file.", 4, 19)]
    [InlineData("use lib \"lib.prp\"\nfunction int answer return 1\nimport from lib answer", "'answer' is a function of this file and cannot also be imported.", 3, 17)]
    [InlineData("import from lib answer\nuse lib \"lib.prp\"\nfunction int answer return 1", "'answer' is a function of this file and cannot also be imported.", 1, 17)]
    [InlineData("use lib \"lib.prp\"\nuse answer \"other.prp\"\nimport from lib answer", "'answer' is a module name in this file and cannot also be imported.", 3, 17)]
    [InlineData("function int lib return 1\nuse lib \"lib.prp\"", "'lib' is a function of this file and cannot also name a module.", 2, 5)]
    [InlineData("use lib \"lib.prp\"\nimport from lib answer\ndef int answer 1", "'answer' is an imported function and cannot be used as a variable name.", 3, 9)]
    [InlineData("use lib \"lib.prp\"\ndef int lib 1", "'lib' is a module and cannot be used as a variable name.", 2, 9)]
    [InlineData("use lib \"lib.prp\"\nfunction int f int lib return lib", "'lib' is a module and cannot be used as a parameter name.", 2, 20)]
    [InlineData("use lib \"lib.prp\"\nimport from lib answer\nfunction int f int answer return answer", "'answer' is an imported function and cannot be used as a parameter name.", 3, 20)]
    [InlineData("use lib \"lib.prp\"\nset from lib answer 1", "A variable name must be a plain name; 'from' starts a qualified name, which can only name an exported function, record, choice or variant.", 2, 5)]
    public void RejectsClashingNames(string main, string message, int line, int column)
    {
        WriteFile("lib.prp", "export function int answer return 42");
        WriteFile("other.prp", "export function int answer return 43");

        var error = FailFile("main.prp", main);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void SameNamesInDifferentModulesDoNotClash()
    {
        WriteFile("lib.prp", "def int answer 1\nexport function int value return answer");
        WriteFile("other.prp", "def int answer 2\nexport function int value return answer");

        var output = RunFile(
            "main.prp",
            "use lib \"lib.prp\"\nuse other \"other.prp\"\nimport from lib value\nwrite_line value\nwrite_line from other value");

        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void ExportInTheMainFileIsAllowed()
    {
        Assert.Equal("3\n", RunFile("main.prp", "export function int three return 3\nwrite_line three"));
    }

    [Fact]
    public void IncludeIsAnOrdinaryNameAgain()
    {
        Assert.Equal("1\n", RunFile("main.prp", "def int include 1\nwrite_line include"));
    }

    [Theory]
    [InlineData("use")]
    [InlineData("import")]
    [InlineData("export")]
    [InlineData("from")]
    public void ModuleKeywordsAreReserved(string word)
    {
        var error = FailFile("main.prp", $"def int {word} 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
    }

    [Fact]
    public void ReplSessionsKeepModulesAndImports()
    {
        WriteFile("lib.prp", "export function int answer return 42\nwrite_line \"loaded\"");
        var use = $"use lib {Quoted("lib.prp")}";

        _session.Run(use);
        _session.Run(use);
        _session.Run($"use again {Quoted("lib.prp")}");
        _session.Run("import from lib answer");
        _session.Run("import from lib answer");
        _session.Run("write_line add answer from again answer");

        Assert.Equal("loaded\n84\n", _output.ToString());
    }

    [Fact]
    public void FailedReplInputsLoadNothing()
    {
        WriteFile("lib.prp", "export function int answer return 42\nwrite_line \"loaded\"");
        var use = $"use lib {Quoted("lib.prp")}";

        Assert.Throws<PyropException>(() => _session.Run($"{use}\nwrite_line add 1 true"));
        Assert.Throws<PyropException>(() => _session.Run("write_line from lib answer"));
        _session.Run($"{use}\nwrite_line from lib answer");

        Assert.Equal("loaded\n42\n", _output.ToString());
    }

    [Fact]
    public void ReplInputsCannotRebindOrClashWithEarlierNames()
    {
        WriteFile("lib.prp", "export function int answer return 42");
        WriteFile("other.prp", "export function int answer return 43");
        _session.Run($"use lib {Quoted("lib.prp")}\nimport from lib answer\ndef int total 0\nfunction int helper return 1");

        Assert.StartsWith("Module name 'lib' already names ", Failure($"use lib {Quoted("other.prp")}").Message);
        Assert.Equal("'answer' is imported in this file and cannot also be a function name.", Failure("function int answer return 1").Message);
        Assert.Equal("'lib' is a module name in this file and cannot also be a function name.", Failure("function int lib return 1").Message);
        Assert.Equal("'answer' is already imported in this file.", Failure($"use other {Quoted("other.prp")}\nimport from other answer").Message);
        Assert.Equal("'helper' is a function of this file and cannot also name a module.", Failure($"use helper {Quoted("other.prp")}").Message);
        Assert.Equal("'total' is already a global variable and cannot be used as a module name.", Failure($"use total {Quoted("other.prp")}").Message);
    }

    [Fact]
    public void ReplImportsOfGlobalNamesAreRejected()
    {
        WriteFile("lib.prp", "export function int answer return 42");
        _session.Run($"use lib {Quoted("lib.prp")}\ndef int answer 1");

        Assert.Equal("'answer' is already a global variable and cannot be used as an imported name.", Failure("import from lib answer").Message);
    }

    [Theory]
    [InlineData("use")]
    [InlineData("use lib")]
    [InlineData("import")]
    [InlineData("import from")]
    [InlineData("import from lib")]
    [InlineData("export")]
    [InlineData("write_line from")]
    [InlineData("write_line from lib")]
    public void UnfinishedModuleStatementsAskForMoreInput(string source)
    {
        Assert.Equal(InputStatus.Incomplete, _session.Probe(source));
    }

    [Fact]
    public void ReportsAPathThatIsTooLongAtThePath()
    {
        var error = FailFile("main.prp", $"use t \"{new string('a', 40_000)}.prp\"");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.EndsWith("\": the path is too long.", error.Message);
        Assert.Equal((1, 7), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void ReportsADirectoryAsADirectory()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));

        var error = FailFile("main.prp", "use t \"sub\"");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal($"Cannot use \"sub\": {Path.Combine(_directory, "sub")} is a directory, not a file.", error.Message);
    }

    [Fact]
    public void ReplInputsReportInvalidPathsWithoutEndingTheSession()
    {
        Assert.Contains("the path contains a control character", Failure("use t \"a\0b\"").Message);
        Assert.Contains("the path is too long", Failure($"use t \"{new string('a', 40_000)}\"").Message);

        _session.Run("write_line 1");
        Assert.Equal("1\n", _output.ToString());
    }

    [Fact]
    public void DuplicatesWithinOneReplInputAreErrorsEvenAfterAnEarlierInput()
    {
        WriteFile("lib.prp", "export function int answer return 42");
        var use = $"use lib {Quoted("lib.prp")}";
        _session.Run($"{use}\nimport from lib answer");

        var duplicateUse = Failure($"{use}\n{use}");
        Assert.Equal("Module name 'lib' is already used in this file.", duplicateUse.Message);
        Assert.Equal((2, 5), (duplicateUse.Position.Line, duplicateUse.Position.Column));
        var duplicateImport = Failure("import from lib answer\nimport from lib answer");
        Assert.Equal("'answer' is already imported in this file.", duplicateImport.Message);
        Assert.Equal((2, 17), (duplicateImport.Position.Line, duplicateImport.Position.Column));
    }

    [Fact]
    public void ARuntimeErrorWhileSettingUpAModuleInTheReplKeepsItLoaded()
    {
        WriteFile("lib.prp",
            "def int stage 1\nexport function int read_stage return stage\nwrite_line \"setting up\"\nwrite_line divide 1 0\nset stage 2");
        var use = $"use lib {Quoted("lib.prp")}";

        var error = Failure(use);
        _session.Run("write_line from lib read_stage");
        _session.Run($"use again {Quoted("lib.prp")}\nwrite_line from again read_stage");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
        Assert.Equal("setting up\n1\n1\n", _output.ToString());
    }

    [Fact]
    public void ExitInAModuleEndsTheWholeProgram()
    {
        WriteFile("lib.prp", "write_line \"in lib\"\nexit 5\nwrite_line \"never\"");
        const string main = "write_line \"main\"\nuse lib \"lib.prp\"\nwrite_line \"after\"";
        var path = WriteFile("main.prp", main);

        Assert.Equal(5, _session.Run(main, path));
        Assert.Equal("in lib\n", _output.ToString());
    }

    [Fact]
    public void ReportsAnExitCodeOutOfRangeInTheModule()
    {
        WriteFile("lib.prp", "exit 256");

        var error = FailFile("main.prp", "use lib \"lib.prp\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("lib.prp", Path.GetFileName(error.Position.Source));
    }

    private PyropError Failure(string source) =>
        Assert.Single(Assert.Throws<PyropException>(() => _session.Run(source)).Errors);
}
