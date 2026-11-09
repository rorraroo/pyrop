using Pyrop.Runtime;

namespace Pyrop.Tests;

public class DataTests
{
    private const string Imports =
        "use data library\nimport from data Data parse_json parse_yaml json_text yaml_text text_at int_at float_at bool_at list_at end\n";

    private static string Program(string source) => Run.Program(Imports + source);

    private static PyropError Failure(string source)
    {
        var error = Run.Failure(Imports + source);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        return error;
    }

    private static string Quoted(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    [Theory]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("12", "12")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("\"čau\"", "\"čau\"")]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    [InlineData("[1, \"a\"]", "[\n  1,\n  \"a\"\n]")]
    [InlineData("{\"b\": 1, \"a\": [true]}", "{\n  \"b\": 1,\n  \"a\": [\n    true\n  ]\n}")]
    [InlineData("1.0", "1.0")]
    [InlineData("1e3", "1000.0")]
    public void ReadsAndWritesJson(string text, string written)
    {
        Assert.Equal(written + "\n", Program($"write_line json_text parse_json {Quoted(text)}"));
    }

    [Theory]
    [InlineData("{", "The text is not valid JSON (line 1).")]
    [InlineData("[1,]", "The text is not valid JSON (line 1).")]
    [InlineData("Service unavailable", "The text is not valid JSON (line 1).")]
    [InlineData("[\n1\n2]", "The text is not valid JSON (line 3).")]
    [InlineData("{\"a\": 1, \"a\": 2}", "The JSON text repeats the key \"a\".")]
    [InlineData("1e999", "The number 1e999 is too large to read.")]
    public void RefusesBadJson(string text, string message)
    {
        Assert.Equal(message, Failure($"write_line json_text parse_json {Quoted(text)}").Message);
    }

    [Fact]
    public void AVeryLargeWholeJsonNumberIsAFloat()
    {
        Assert.Equal("1E+20\n", Program("write_line json_text parse_json \"100000000000000000000\""));
    }

    [Theory]
    [InlineData("", "null")]
    [InlineData("~", "null")]
    [InlineData("null", "null")]
    [InlineData("True", "true")]
    [InlineData("FALSE", "false")]
    [InlineData("12", "12")]
    [InlineData("0x10", "16")]
    [InlineData("0o10", "8")]
    [InlineData("1.5", "1.5")]
    [InlineData("-2e0", "-2.0")]
    [InlineData("yes", "\"yes\"")]
    [InlineData("NO", "\"NO\"")]
    [InlineData("'12'", "\"12\"")]
    [InlineData("\"true\"", "\"true\"")]
    [InlineData("!!str 12", "\"12\"")]
    [InlineData("Service unavailable", "\"Service unavailable\"")]
    [InlineData("0x", "\"0x\"")]
    [InlineData("1.2.3", "\"1.2.3\"")]
    [InlineData("100000000000000000000", "1E+20")]
    public void ReadsYamlScalars(string text, string json)
    {
        Assert.Equal(json + "\n", Program($"write_line json_text parse_yaml {Quoted(text)}"));
    }

    [Fact]
    public void ReadsYamlStructure()
    {
        const string yaml = "name: backup\nkeep: 3\nfolders:\n  - a\n  - b\nnested: {x: 1, y: [2]}\n";

        Assert.Equal(
            "backup\n3\nb\n2\n",
            Program($"def Data d parse_yaml {Quoted(yaml)}\nwrite_line text_at d \"name\"\nwrite_line int_at d \"keep\"\nwrite_line text_at d \"folders.1\"\nwrite_line int_at d \"nested.y.0\""));
    }

    [Theory]
    [InlineData("a: 1\na: 2", "The YAML text repeats the key \"a\" (line 2).")]
    [InlineData("a: [1\nb", "The YAML text ends before it is complete.")]
    [InlineData("!secret x", "The YAML text uses the tag !secret, which Pyrop does not read (line 1).")]
    [InlineData("[1]: x", "A YAML key must be plain text (line 1).")]
    [InlineData(".inf", "Pyrop floats are always finite, so the YAML value .inf cannot be read (line 1).")]
    [InlineData("-.Inf", "Pyrop floats are always finite, so the YAML value -.Inf cannot be read (line 1).")]
    [InlineData(".nan", "Pyrop floats are always finite, so the YAML value .nan cannot be read (line 1).")]
    [InlineData("1e999", "The number 1e999 is too large to read (line 1).")]
    [InlineData("a: *nope", "The YAML text uses the alias *nope before any anchor of that name is finished.")]
    public void RefusesYamlOutsidePyropsValues(string text, string message)
    {
        Assert.Equal(message, Failure($"write_line json_text parse_yaml {Quoted(text)}").Message);
    }

    [Fact]
    public void ReadsOnlyTheFirstYamlDocument()
    {
        Assert.Equal("1\n", Program($"write_line int_at parse_yaml {Quoted("a: 1\n---\na: 2\n")} \"a\""));
    }

    [Fact]
    public void ReadsAliasesAsTheAnchoredValue()
    {
        Assert.Equal("5\n", Program($"write_line int_at parse_yaml {Quoted("a: &x {y: 5}\nb: *x\n")} \"b.y\""));
    }

    [Fact]
    public void AValueCannotContainItself()
    {
        Assert.Equal("The YAML text uses the alias *a before any anchor of that name is finished.", Failure($"write_line json_text parse_yaml {Quoted("&a [*a]")}").Message);
    }

    [Fact]
    public void AnAliasBombIsARuntimeError()
    {
        var lines = new List<string> { "a0: &a0 [x, x, x, x, x, x, x, x, x, x]" };
        for (var level = 1; level < 12; level++)
        {
            var refs = string.Join(", ", Enumerable.Repeat($"*a{level - 1}", 10));
            lines.Add($"a{level}: &a{level} [{refs}]");
        }

        Assert.Equal(
            "The YAML text expands to more than 10,000,000 values.",
            Failure($"write_line length_of list_at parse_yaml {Quoted(string.Join("\n", lines))} \"a11\"").Message);
    }

    [Theory]
    [InlineData("\"plain words\"", "plain words\n")]
    [InlineData("\"yes\"", "\"yes\"\n")]
    [InlineData("\"NULL\"", "\"NULL\"\n")]
    [InlineData("\"123\"", "\"123\"\n")]
    [InlineData("\"-x\"", "\"-x\"\n")]
    [InlineData("\".5\"", "\".5\"\n")]
    [InlineData("\"\"", "--- \"\"\n")]
    [InlineData("\"a: b\"", "'a: b'\n")]
    [InlineData("\"line\\nnext\"", "\"line\\nnext\"\n")]
    [InlineData("\"čau\"", "čau\n")]
    public void QuotesTextYamlWouldMisread(string text, string yaml)
    {
        Assert.Equal(yaml, Program($"write yaml_text convert Data {text}"));
    }

    [Fact]
    public void WritesBlockYaml()
    {
        const string source = "write yaml_text parse_json \"{\\\"name\\\": \\\"x\\\", \\\"keep\\\": 3, \\\"rate\\\": 1.0, \\\"on\\\": true, \\\"none\\\": null, \\\"list\\\": [1, [2]], \\\"map\\\": {\\\"a\\\": []}, \\\"empty\\\": {}}\"";

        Assert.Equal(
            "name: x\nkeep: 3\nrate: 1.0\n\"on\": true\nnone: null\nlist:\n  - 1\n  - - 2\nmap:\n  a: []\nempty: {}\n",
            Program(source));
    }

    [Theory]
    [InlineData("\"a\\nb\"")]
    [InlineData("\"yes\"")]
    [InlineData("\"   padded  \"")]
    [InlineData("\"# not a comment\"")]
    [InlineData("\"tab\\there\"")]
    [InlineData("\"[1, 2]\"")]
    [InlineData("\"{a: 1}\"")]
    [InlineData("\"key: value\"")]
    [InlineData("\"ünï €\"")]
    public void TextSurvivesYamlAndJson(string text)
    {
        Assert.Equal("true\ntrue\n", Program(
            $"def Data d convert Data {text}\nwrite_line equal d parse_yaml yaml_text d\nwrite_line equal d parse_json json_text d"));
    }

    [Theory]
    [InlineData("a.b", "true")]
    [InlineData("items.1", "true")]
    public void ConvertsAWholeDocumentBetweenFormats(string path, string expected)
    {
        var source =
            "def Data d parse_json \"{\\\"a\\\": {\\\"b\\\": 1}, \\\"items\\\": [1, 2.5, null, true, \\\"x\\\"]}\"\n" +
            $"write_line equal d parse_yaml yaml_text d\nwrite_line {(path == "a.b" ? "equal int_at d \"a.b\" 1" : "equal float_at d \"items.1\" 2.5")}";

        Assert.Equal($"true\n{expected}\n", Program(source));
    }

    [Theory]
    [InlineData("int_at d \"keep\"", "3")]
    [InlineData("float_at d \"keep\"", "3.0")]
    [InlineData("float_at d \"rate\"", "1.5")]
    [InlineData("text_at d \"name\"", "x")]
    [InlineData("bool_at d \"on\"", "true")]
    [InlineData("length_of list_at d \"list\"", "2")]
    [InlineData("int_at d \"list.1.deep\"", "7")]
    public void ReadsByPath(string expression, string expected)
    {
        var source = $"def Data d parse_yaml {Quoted("name: x\nkeep: 3\nrate: 1.5\non: true\nlist: [0, {deep: 7}]\n")}\nwrite_line {expression}";

        Assert.Equal(expected + "\n", Program(source));
    }

    [Fact]
    public void TheEmptyPathIsTheValueItself()
    {
        Assert.Equal("hi\n", Program("write_line text_at convert Data \"hi\" \"\""));
    }

    [Theory]
    [InlineData("int_at d \"gone\"", "Nothing is at \"gone\": there is no key \"gone\".")]
    [InlineData("int_at d \"list.5\"", "Nothing is at \"list.5\": the list has no position \"5\".")]
    [InlineData("int_at d \"list.x\"", "Nothing is at \"list.x\": the list has no position \"x\".")]
    [InlineData("int_at d \"list.-1\"", "Nothing is at \"list.-1\": the list has no position \"-1\".")]
    [InlineData("int_at d \"name.x\"", "Nothing is at \"name.x\": \"x\" is inside a text, which holds no parts.")]
    [InlineData("int_at d \"name\"", "The value at \"name\" is text, not int.")]
    [InlineData("text_at d \"keep\"", "The value at \"keep\" is int, not text.")]
    [InlineData("int_at d \"\"", "The value is map, not int.")]
    [InlineData("int_at d \"rate\"", "The value at \"rate\" is float, not int.")]
    public void AWrongPathIsARuntimeError(string expression, string message)
    {
        Assert.Equal(message, Failure($"def Data d parse_yaml {Quoted("name: x\nkeep: 3\nrate: 1.5\nlist: [0]\n")}\nwrite_line {expression}").Message);
    }

    [Fact]
    public void AttemptTurnsAMissingPathIntoNone()
    {
        Assert.Equal("none\n", Program("write_line attempt int_at parse_json \"{}\" \"x\""));
    }

    [Fact]
    public void MatchesOnData()
    {
        const string source =
            "match parse_json \"[1, 2]\"\n" +
            "    when data_list items write_line length_of items\n" +
            "    when data_null write_line \"null\"\n" +
            "    when data_bool b write_line b\n" +
            "    when data_int i write_line i\n" +
            "    when data_float f write_line f\n" +
            "    when data_text s write_line s\n" +
            "    when data_map m write_line length_of m\n" +
            "end";

        Assert.Equal("2\n", Program(source));
    }

    private const string Declarations = """
        choice Colour red green end
        choice Move go int jump int int rest end
        record Inner int n
        record Everything int i float f string s bool b list int l map string int m option int present option int absent Colour c Move g Move j Move r Inner inner map int bool keyed map Colour int coloured Data free

        """;

    private const string Whole = """
        def map string int m put map_of string int end "k" 1
        def map int bool keyed put map_of int bool end 7 true
        def map Colour int coloured put map_of Colour int end green 2
        def Everything e Everything 1 2.5 "s" true list_of int 1 2 end m some 3 none int red go 4 jump 5 6 rest Inner 8 keyed coloured convert Data "free"

        """;

    [Fact]
    public void AnyValueRoundTripsThroughData()
    {
        Assert.Equal("true\ntrue\ntrue\n", Program(Declarations + Whole +
            "write_line equal e convert Everything convert Data e\n" +
            "write_line equal e convert Everything parse_yaml yaml_text convert Data e\n" +
            "write_line equal e convert Everything parse_json json_text convert Data e"));
    }

    [Fact]
    public void ARecordBecomesAMapLeavingOutAbsentOptions()
    {
        Assert.Equal(
            "{\n  \"a\": 1\n}\n",
            Program("record R int a option int b\nwrite_line json_text convert Data R 1 none int"));
    }

    [Fact]
    public void ChoicesBecomeNamesOrSingleKeyMaps()
    {
        Assert.Equal(
            "rest\ngo: 4\njump:\n  - 5\n  - 6\n",
            Program("choice Move go int jump int int rest end\nwrite yaml_text convert Data rest\nwrite yaml_text convert Data go 4\nwrite yaml_text convert Data jump 5 6"));
    }

    [Fact]
    public void AnAbsentOptionInAListIsNull()
    {
        Assert.Equal(
            "[\n  1,\n  null\n]\n",
            Program("write_line json_text convert Data list_of option int some 1 none int end"));
    }

    [Fact]
    public void ReadingToleratesExtraKeysAndMissingOptions()
    {
        Assert.Equal(
            "1\nnone\n",
            Program($"record R int a option int b\ndef R r convert R parse_yaml {Quoted("a: 1\nextra: x\n")}\nwrite_line the a of r\nwrite_line the b of r"));
    }

    [Fact]
    public void AWholeNumberReadsAsAFloatButNotTheReverse()
    {
        Assert.Equal("3.0\n", Program("record R float a\nwrite_line the a of convert R parse_json \"{\\\"a\\\": 3}\""));
        Assert.Equal(
            "The value at \"a\" is float, not int.",
            Failure("record R int a\nwrite_line the a of convert R parse_json \"{\\\"a\\\": 3.0}\"").Message);
    }

    [Theory]
    [InlineData("{}", "The value has no field \"name\", which P needs.")]
    [InlineData("[]", "The value is list, not P.")]
    [InlineData("{\"name\": 1}", "The value at \"name\" is int, not string.")]
    public void AWrongShapeNamesWhere(string json, string message)
    {
        Assert.Equal(message, Failure($"record P string name\nwrite_line the name of convert P parse_json {Quoted(json)}").Message);
    }

    [Theory]
    [InlineData("\"blue\"", "The value is \"blue\", which is not a variant of Move.")]
    [InlineData("\"go\"", "The value names the variant go, which carries data.")]
    [InlineData("{\"rest\": 1}", "The value gives data to the variant rest, which carries none.")]
    [InlineData("{\"nope\": 1}", "The value names \"nope\", which is not a variant of Move.")]
    [InlineData("{\"jump\": [1]}", "The value at \"jump\" must be a list of 2 values for the variant jump.")]
    [InlineData("{\"a\": 1, \"b\": 2}", "The value is map, not a variant of Move.")]
    [InlineData("5", "The value is int, not a variant of Move.")]
    public void AWrongChoiceNamesWhere(string json, string message)
    {
        Assert.Equal(message, Failure($"choice Move go int jump int int rest end\nwrite_line convert Move parse_json {Quoted(json)}").Message);
    }

    [Fact]
    public void AMapKeyOfTheWrongTypeNamesIt()
    {
        Assert.Equal(
            "The value has the key \"x\", which is not int.",
            Failure("write_line convert map int int parse_json \"{\\\"x\\\": 1}\"").Message);
    }

    [Fact]
    public void AttemptCatchesAWrongShape()
    {
        Assert.Equal("none\n", Program("record P string name\nwrite_line attempt convert P parse_json \"{}\""));
    }

    [Fact]
    public void ConvertStringOfDataIsTheTextForm()
    {
        Assert.Equal("data_text \"x\"\nx\n", Program("write_line convert string convert Data \"x\"\nwrite_line text_at convert Data \"x\" \"\""));
    }

    [Theory]
    [InlineData("write_line json_text deep", "This data is nested too deeply.")]
    [InlineData("write_line yaml_text deep", "This data is nested too deeply.")]
    public void DeepDataStopsAtTheNestingLimit(string statement, string message)
    {
        var source = Imports + "def Data deep data_list list_of Data data_list list_of Data data_null end end\n" + statement;
        var session = new PyropSession(new StringReader(""), new StringOutput()) { NestingLimit = 2 };

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(source)).Errors);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void AVeryDeepJsonTextDoesNotCrash()
    {
        Assert.Equal(ErrorStage.Runtime, Run.Failure(Imports + "write_line json_text parse_json repeat \"[\" 200000").Stage);
    }

    [Fact]
    public void YamlNestedPastAThousandLevelsIsRefusedQuickly()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var error = Run.Failure(Imports + "write_line json_text parse_yaml repeat \"[\" 2000000");

        Assert.Equal("YAML can nest at most 1,000 levels deep.", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public void AttemptCatchesYamlThatIsTooDeep()
    {
        Assert.Equal("none\n", Program("write_line attempt parse_yaml repeat \"[\" 2000"));
    }

    [Theory]
    [InlineData("\\UFFFFFFFF")]
    [InlineData("\\U80000000")]
    public void AnImpossibleEscapeIsInvalidYamlNotACrash(string escape)
    {
        Assert.Equal("none\n", Program($"write_line attempt parse_yaml {Quoted($"\"{escape}\"")}"));
    }

    [Fact]
    public void AKeyCanCarryAnAnchor()
    {
        Assert.Equal("key\n", Program($"write_line text_at parse_yaml {Quoted("&k key: v\nother: *k\n")} \"other\""));
    }

    [Fact]
    public void AKeysTagIsChecked()
    {
        Assert.Equal(
            "The YAML text uses the tag !foo, which Pyrop does not read (line 1).",
            Failure($"write_line json_text parse_yaml {Quoted("!foo a: 1\n")}").Message);
    }

    [Fact]
    public void WhatFollowsTheFirstYamlDocumentIsNotRead()
    {
        Assert.Equal("1\n", Program($"write_line int_at parse_yaml {Quoted("a: 1\n...\n@@@@")} \"a\""));
    }

    [Fact]
    public void JsonWritesPastAThousandLevels()
    {
        Assert.Equal("true\n", Program("def Data d parse_json add repeat \"[\" 5000 repeat \"]\" 5000\nwrite_line equal d parse_json json_text d"));
    }

    [Fact]
    public void TwoKeysThatReadTheSameAreARuntimeError()
    {
        Assert.Equal(
            "The value has two keys that read as 1.",
            Failure("write_line convert map int string parse_json \"{\\\"1\\\": \\\"a\\\", \\\"01\\\": \\\"b\\\"}\"").Message);
    }

    [Fact]
    public void ANestedAbsenceReadsBackAsNone()
    {
        Assert.Equal("none\n", Program("def option option int v some none int\nwrite_line convert option option int convert Data v"));
    }

    [Fact]
    public void AVeryDeepYamlTextDoesNotCrash()
    {
        Assert.Equal(ErrorStage.Runtime, Run.Failure(Imports + "write_line json_text parse_yaml repeat \"[\" 200000").Stage);
    }
}
