using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class PolishTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-polish-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private string Quoted(string name) => $"\"{Path.Combine(_directory, name).Replace('\\', '/')}\"";

    [Theory]
    [InlineData("not ", "write_line ", " true")]
    [InlineData("block ", "", " end")]
    public void CodeNestedTooDeeplyIsAParseError(string level, string before, string inner)
    {
        var failure = Run.Failure(before + string.Concat(Enumerable.Repeat(level, 5_000)) + inner);

        Assert.Equal(ErrorStage.Parse, failure.Stage);
        Assert.Contains("nested more than 1000 levels", failure.Message);
    }

    [Fact]
    public void ATypeNestedTooDeeplyIsAParseError()
    {
        var failure = Run.Failure($"define {string.Concat(Enumerable.Repeat("list ", 5_000))}int x none int");

        Assert.Contains("nested more than 1000 levels", failure.Message);
    }

    [Fact]
    public void CodeWithinTheNestingLimitRuns() =>
        Assert.Equal("true\n", Run.Program($"write_line {string.Concat(Enumerable.Repeat("not not ", 400))}true"));

    [Fact]
    public void ACarriageReturnSurvivesBeingSaved()
    {
        var path = Quoted("note.txt");
        var output = Run.Program(
            $"use files library\nimport from files save load end\nrecord Note string body end\ndefine Note before Note \"x\\r\\ny\\rz\"\nsave {path} convert string before\nwrite_line equal before convert Note load {path}\n");

        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TheEscapeForACarriageReturnWorksInLiterals() =>
        Assert.Equal("[\"a\", \"b\"]\n", Run.Program("write_line split \"a\\r\\nb\" \"\\r\\n\""));

    [Fact]
    public void TheTextFormReaderRefusesALineBreakInsideQuotes()
    {
        var output = Run.Program("record R string s end\nwrite_line attempt convert R \"R s \\\"a\nb\\\"\"\n".Replace("\nb", "\\nb"));

        Assert.Equal("none\n", output);
    }

    [Fact]
    public void JsonLaysOutFloatsLikeEveryOtherValue()
    {
        var output = Run.Program("use data library\nimport from data parse_json json_text end\nwrite_line json_text parse_json \"[1, 2.5, 3.0]\"\n");

        Assert.Equal("[\n  1,\n  2.5,\n  3.0\n]\n", output);
    }

    [Fact]
    public void JsonNestedTooDeeplyIsAnOrdinaryFailure()
    {
        var deep = new string('[', 50_000) + new string(']', 50_000);
        var failure = Run.Failure($"use data library\nimport from data parse_json end\nwrite_line parse_json \"{deep}\"\n");

        Assert.Equal(ErrorStage.Runtime, failure.Stage);
        Assert.Contains("JSON can nest at most 1,000 levels", failure.Message);
    }

    [Fact]
    public void AReadOnlyFileCanBeReplacedAndDeleted()
    {
        File.WriteAllText(Path.Combine(_directory, "from.txt"), "new");
        var target = Path.Combine(_directory, "to.txt");
        File.WriteAllText(target, "old");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        Run.Program($"use files library\nimport from files copy_file delete_file end\ncopy_file {Quoted("from.txt")} {Quoted("to.txt")}\n");
        Assert.Equal("new", File.ReadAllText(target));

        File.SetAttributes(target, FileAttributes.ReadOnly);
        Run.Program($"use files library\nimport from files delete_file end\ndelete_file {Quoted("to.txt")}\n");
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void AFolderHoldingReadOnlyFilesCanBeDeleted()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_directory, "tree", "inner")).FullName;
        var file = Path.Combine(folder, "locked.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        Run.Program($"use files library\nimport from files delete_folder end\ndelete_folder {Quoted("tree")}\n");

        Assert.False(Directory.Exists(Path.Combine(_directory, "tree")));
    }

    [Fact]
    public void ACallThatRunsOutOfValuesNamesTheCall()
    {
        var failure = Run.Failure("write_line add 1\nwrite_line \"next\"\n");

        Assert.Equal(1, failure.Position.Line);
        Assert.Equal(12, failure.Position.Column);
        Assert.Contains("'add' needs 2 values but has only 1", failure.Message);
        Assert.Contains("'write_line' on line 2 begins a new statement", failure.Message);
    }

    [Fact]
    public void AVoidCallAfterAShortCallBlamesTheShortCall()
    {
        var failure = Run.Failure("function void log string text\nwrite_line text\nwrite_line add 1\nlog \"x\"\n");

        Assert.Contains("'add' needs 2 values", failure.Message);
    }

    [Fact]
    public void ACallEndingAtTheEndOfInputStaysIncomplete() =>
        Assert.True(Assert.Throws<PyropException>(() => Run.Program("write_line add 1")).IsIncompleteInput);

    [Theory]
    [InlineData("write_line ad 1 2", "'ad' is not an operation or function", "Did you mean 'add'?")]
    [InlineData("defin int x 1", "'defin' is not a statement, operation or function", "Did you mean 'define'?")]
    [InlineData("Write_Line 1", "'Write_Line' is not a statement", "Did you mean 'write_line'?")]
    [InlineData("print 1", "'print' is not a statement", "use write_line")]
    [InlineData("if true then write_line 1", "'then' is not a statement", "Nothing comes between")]
    [InlineData("if true write_line 1 elif false write_line 2", "'elif' is not a statement", "else if")]
    public void AnUnknownWordIsBlamedForItself(string source, string problem, string help)
    {
        var failure = Run.Failure(source);

        Assert.Contains(problem, failure.Message);
        Assert.Contains(help, failure.Message);
    }

    [Fact]
    public void AnUntypedParameterIsNamed() =>
        Assert.Contains("Parameter 'w' needs a type", Run.Failure("function int area w h\nreturn 1\n").Message);

    [Fact]
    public void AnElseInsideABlockSaysToCloseIt() =>
        Assert.Contains("close the block with 'end' first", Run.Failure("if true block\nwrite_line 1\nelse\nwrite_line 2\nend\n").Message);

    [Fact]
    public void AFunctionInsideAnOpenBlockPointsAtTheBlock() =>
        Assert.Contains("started at line 1, column 1 may be missing", Run.Failure("block\nwrite_line 1\nfunction void f\nwrite_line 2\n").Message);

    [Fact]
    public void SettingAFieldSaysHowToMakeANewRecord() =>
        Assert.Contains("A record does not change", Run.Failure("record P int x end\ndefine P p P 1\nset the x of p 3\n").Message);

    [Fact]
    public void SettingAnUndeclaredVariableSaysToDeclareIt() =>
        Assert.Contains("Declare it first with define", Run.Failure("set y 3\n").Message);

    [Fact]
    public void AnUndeclaredNameIsReportedOnce() =>
        Assert.Single(Run.Failures("write_line y\nwrite_line y\nset y 1\n"));

    [Theory]
    [InlineData("write_line 1 // no", "a comment starts with #")]
    [InlineData("write_line 'a'", "double quotes")]
    [InlineData("write_line a.b", "from math round")]
    [InlineData("define int my-var 1", "underscores")]
    [InlineData("write_line (1)", "no brackets")]
    [InlineData("write_line 1e5", "no exponent form")]
    public void ASymbolGetsAHintOfItsOwn(string source, string hint) =>
        Assert.Contains(hint, Run.Failure(source).Message);

    [Fact]
    public void AnEmojiIsReportedWhole() =>
        Assert.Contains("'😀'", Run.Failure("write_line 😀").Message);

    [Fact]
    public void ALongCopyChainCountsAsNesting()
    {
        var chain = string.Concat(Enumerable.Range(0, 1500).Select(index => $" with f{index} 1"));
        var fields = string.Concat(Enumerable.Range(0, 1500).Select(index => $" int f{index}"));

        Assert.Contains("nested more than 1000 levels", Run.Failure($"record R{fields} end\ndefine R p R{string.Concat(Enumerable.Repeat(" 1", 1500))}\nwrite_line copy p{chain} end\n").Message);
    }

    [Fact]
    public void AWordOnTheLineAfterAHeaderIsNotCalledAParameter() =>
        Assert.Contains("'frobnicate' is not a statement", Run.Failure("function void main\n    frobnicate 1\n").Message);

    [Fact]
    public void AGlobalDeclaredLaterIsNotCalledUnknown() =>
        Assert.StartsWith("'5' cannot stand alone", Run.Failure("function void f\n    write_line total 5\n\ndefine int total 1\n").Message);

    [Fact]
    public void AQualifiedFunctionIsNotCalledUnknown()
    {
        File.WriteAllText(Path.Combine(_directory, "m.prp"), "export function int count\n    return 1\n");

        Assert.StartsWith("'5' cannot stand alone", Run.Failure($"use m {Quoted("m.prp")}\nwrite_line from m count 5\n").Message);
    }

    [Fact]
    public void AnEndAfterAShortCallIsNotCalledAStatement() =>
        Assert.Contains("'end' on line 1 comes next", Run.Failure("write_line list_of int add 1 end").Message);

    [Fact]
    public void AVoidCallOnTheSameLineIsReportedAsVoid() =>
        Assert.Contains("returns void", Run.Failure("function void log\n    return void\n\nfunction int g\n    return add 1 log\n").Message);

    [Fact]
    public void AOneLetterWordGetsNoSuggestion() =>
        Assert.DoesNotContain("Did you mean", Run.Failure("n 5").Message);

    [Fact]
    public void AnUndeclaredNameIsReportedOncePerFunction() =>
        Assert.Equal(2, Run.Failures("function void a\n    write_line zz\n\nfunction void b\n    write_line zz\n").Count);

    [Fact]
    public void AValueInsideAThousandLevelsOfJsonIsAllowed()
    {
        const string imports = "use data library\nimport from data parse_json json_text end\n";

        Assert.StartsWith("[", Run.Program($"{imports}write_line json_text parse_json add add repeat \"[\" 1000 \"1\" repeat \"]\" 1000\n"));
        Assert.Contains("JSON can nest at most 1,000 levels", Run.Failure($"{imports}write_line json_text parse_json add add repeat \"[\" 1001 \"1\" repeat \"]\" 1001\n").Message);
    }

    [Fact]
    public void OutputThatIsNotUtf8IsReadOnWindowsAndRefusedElsewhere()
    {
        var path = Path.Combine(_directory, "oem.txt");
        File.WriteAllBytes(path, [0x70, 0xE9, 0x71]);

        if (OperatingSystem.IsWindows())
        {
            var output = ProcessCommandRunner.Instance.Capture("cmd", ["/c", "type", path]).Output;

            Assert.Equal(3, output.Length);
            Assert.Equal('p', output[0]);
            Assert.Equal('q', output[2]);
            Assert.DoesNotContain('\0', output);
            Assert.DoesNotContain('�', output);
        }
        else
        {
            Assert.Contains("is not UTF-8 text", Assert.Throws<CommandFailedException>(() => ProcessCommandRunner.Instance.Capture("cat", [path])).Message);
        }
    }

    [Fact]
    public void AScriptThatIsNotUtf8IsRefusedInUse()
    {
        File.WriteAllBytes(Path.Combine(_directory, "bad.prp"), [0x77, 0xFF, 0x0A]);
        var failure = Run.Failure($"use bad {Quoted("bad.prp")}\n");

        Assert.Contains("is not UTF-8 text", failure.Message);
    }

    [Fact]
    public void Utf16ScriptsWithAByteOrderMarkStillLoad()
    {
        File.WriteAllText(Path.Combine(_directory, "wide.prp"), "export function int two\nreturn 2\n", Encoding.Unicode);

        Assert.Equal("2\n", Run.Program($"use wide {Quoted("wide.prp")}\nwrite_line from wide two\n"));
    }
}
