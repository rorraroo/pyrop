using System.Globalization;
using System.Text;

namespace Pyrop.Tests;

public sealed class RecordTextTests
{
    private const string Types =
        "choice Place hall study end\n" +
        "choice Shape circle float rect float float node Shape Shape leaf Place end\n" +
        "record Point float x float y\n" +
        "record Player Place place int moves list string inventory option Point spot\n" +
        "record Inner string name option Inner next list Shape shapes\n" +
        "record Outer int n float f bool b string s list int ints map string Inner inners option Shape shape Place place Inner inner map Place list string tags\n";

    private static string Program(string body) => Run.Program(Types + body);

    private static PyropError Failure(string body) => Run.Failure(Types + body);

    [Fact]
    public void ARecordReadsBackFromItsTextForm()
    {
        Assert.Equal("true\n", Program(
            "def Player p Player study 5 list_of string \"lamp\" \"say \\\"hi\\\"\\n\" end some Point 1.0 -2.5\n" +
            "write_line equal convert Player convert string p p"));
    }

    [Fact]
    public void AChoiceWithDataReadsBackFromItsTextForm()
    {
        Assert.Equal("node (circle 1.0) (leaf study)\ntrue\n", Program(
            "def Shape s convert Shape \"node (circle 1.0) (leaf study)\"\nwrite_line s\nwrite_line equal s node circle 1.0 leaf study"));
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnored()
    {
        Assert.Equal("Point x 1.0 y 2.0\n", Program("write_line convert Point \"  Point x 1.0 y 2.0\\n\""));
    }

    [Fact]
    public void FloatsKeepTheirTextForm()
    {
        Assert.Equal("Point x 1E+20 y -0.001\ntrue\n", Program(
            "def Point p convert Point \"Point x 1E+20 y -0.001\"\nwrite_line p\nwrite_line equal p Point multiply 100000.0 1000000000000000.0 -0.001"));
    }

    [Theory]
    [InlineData("Player place study movs 5", 19, "the field 'moves'")]
    [InlineData("Player place kitchen moves 5", 13, "a variant of Place (hall and study)")]
    [InlineData("Player place study moves 5.0 inventory [] spot none", 25, "an int")]
    [InlineData("Player place study moves x", 25, "an int")]
    [InlineData("Player place study moves 5 inventory [\"a\" \"b\"] spot none", 41, "', ' or ']'")]
    [InlineData("Player place study moves 5 inventory [] spot some Point x 1.0 y 2.0", 50, "'('")]
    [InlineData("Player place study moves 5 inventory [] spot maybe", 45, "some or none")]
    [InlineData("Player place study moves 5 inventory [] spot none extra", 49, "the end of the text")]
    [InlineData("Player place study moves 5 inventory [\"a\\q\"] spot none", 40, "one of the escapes \\\", \\\\, \\n or \\t")]
    [InlineData("Player place study moves 5 inventory [\"a", 40, "'\"' to close the string")]
    [InlineData("Player place study moves 99999999999999999999 inventory [] spot none", 25, "an int")]
    [InlineData("Player  place study", 7, "the field 'place'")]
    [InlineData("Point x 1 y 2.0", 8, "a float, such as 1.0")]
    [InlineData("", 0, "the record name 'Player'")]
    public void AnythingElseFailsWithThePositionAndWhatWasExpected(string text, int position, string expected)
    {
        var target = text.StartsWith("Point") ? "Point" : "Player";
        var literal = text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var error = Failure($"write_line convert {target} \"{literal}\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"Cannot read a {target} at position {position}: expected {expected}.", error.Message);
    }

    [Fact]
    public void TheArticleFollowsTheName()
    {
        Assert.StartsWith("Cannot read an Inner at position 0", Failure("write_line convert Inner \"x\"").Message);
    }

    [Fact]
    public void AVariantWithDataOutsideBracketsWhereTheyBelongFails()
    {
        Assert.Equal(
            "Cannot read a Shape at position 5: expected '(' before the variant 'circle', which carries data.",
            Failure("write_line convert Shape \"node circle 1.0 (circle 2.0)\"").Message);
        Assert.Equal(
            "Cannot read a Shape at position 6: expected a variant of Place that carries data.",
            Failure("write_line convert Shape \"leaf (study)\"").Message);
    }

    [Fact]
    public void ADuplicateMapKeyFails()
    {
        Assert.Equal(
            "Cannot read an Outer at position 126: expected a key that is not already in the map.",
            Failure(
                "write_line convert Outer \"Outer n 1 f 1.0 b true s \\\"\\\" ints [] inners {} shape none place hall inner (Inner name \\\"\\\" next none shapes []) tags {hall: [], hall: []}\"").Message);
    }

    [Fact]
    public void AttemptGivesAnAbsentValue()
    {
        Assert.Equal("none\nsome (Point x 1.0 y 2.0)\n", Program(
            "write_line attempt convert Point \"Point x 1.0\"\nwrite_line attempt convert Point \"Point x 1.0 y 2.0\""));
    }

    [Fact]
    public void AViewReadsBackFromItsTextForm()
    {
        Assert.Equal("true\n", Run.Program(
            "use terminal library\nimport from terminal View end\n" +
            "def View v panel \"Hero\" rows list_of View bar \"Health\" 7 20 in_style \"danger\" text \"Poisoned\" menu list_of string \"a\" end 0 end\n" +
            "write_line equal convert View convert string v v"));
    }

    [Fact]
    public void ATextNestedTooDeeplyIsARuntimeErrorEvenUnderAttempt()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { NestingLimit = 50 };
        var deep = string.Concat(Enumerable.Repeat("node (leaf hall) (", 60)) + "leaf hall" + new string(')', 60);

        var exception = Assert.Throws<PyropException>(() => session.Run(Types + $"write_line attempt convert Shape \"{deep}\""));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("This text is nested too deeply to be read.", error.Message);
    }

    [Fact]
    public void AValueNestedExactlyToTheLimitReadsBack()
    {
        const string program =
            "choice T leaf int wrap T end\ndef T t leaf 1\ndef int i 0\nwhile less i 49 block\n    set t wrap t\n    set i add i 1\nend\n" +
            "write_line equal convert T convert string t t";
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { NestingLimit = 50 };

        session.Run(program);

        Assert.Equal("true\n", output.ToString());
    }

    [Fact]
    public void ExtremeNumbersReadBack()
    {
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", Run.Program(
            "record N int i float f\n" +
            "function bool same N n return equal convert N convert string n n\n" +
            "write_line same N subtract -9223372036854775807 1 convert float \"1E-05\"\n" +
            "write_line same N 9223372036854775807 convert float \"5E-324\"\n" +
            "write_line same N 0 convert float \"1.7976931348623157E+308\"\n" +
            "write_line same N -1 convert float \"-0.0\"\n" +
            "write_line same N 1 convert float \"1E+20\"\n" +
            "write_line same N 2 0.1"));
    }

    [Fact]
    public void NestedOptionsReadBack()
    {
        Assert.Equal("O o some some 2\ntrue\n", Run.Program(
            "record O option option int o\ndef O v O some some 2\nwrite_line v\nwrite_line equal convert O convert string v v"));
    }

    [Theory]
    [InlineData("I n 007")]
    [InlineData("I n -0")]
    [InlineData("I n 00")]
    public void AnIntNotWrittenAsTheTextFormWritesItFails(string text)
    {
        Assert.Equal("none\n", Run.Program($"record I int n\nwrite_line attempt convert I \"{text}\""));
    }

    [Fact]
    public void AListLongerThanTheCollectionLimitIsTheConversionsOwnFailure()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { CollectionLimit = 3 };

        session.Run(Types + "write_line attempt convert Player \"Player place hall moves 0 inventory [\\\"a\\\", \\\"b\\\", \\\"c\\\", \\\"d\\\"] spot none\"");

        Assert.Equal("none\n", output.ToString());
    }

    [Fact]
    public void AChoiceWithoutDataStillReadsAsBefore()
    {
        Assert.Equal("study\n", Program("write_line convert Place \" study \""));
    }

    [Fact]
    public void RandomValuesReadBackEqual()
    {
        var random = new Random(20261103);
        var body = new StringBuilder("def Outer v ");
        body.Append(new Generator(random).Outer(3)).Append('\n');
        body.Append("def int same 0\n");
        for (var round = 0; round < 150; round++)
        {
            body.Append("set v ").Append(new Generator(random).Outer(3)).Append('\n');
            body.Append("if equal convert Outer convert string v v set same add same 1\nelse write_line v\n");
        }

        body.Append("write_line same");

        Assert.Equal("150\n", Program(body.ToString()));
    }

    private sealed class Generator(Random random)
    {
        private const string Characters = "ab ,:[]{}()\"\\\n\tč-1.E";

        public string Outer(int depth) =>
            $"Outer {Int()} {Float()} {(random.Next(2) == 0 ? "true" : "false")} {String()} " +
            $"{List("int", () => Int())} {Map("string", "Inner", () => Inner(depth - 1))} {Option("Shape", () => Shape(depth - 1))} " +
            $"{Place()} {Inner(depth - 1)} {PlaceMap(depth)}";

        private string PlaceMap(int depth)
        {
            var keys = new[] { "hall", "study" }.Where(_ => random.Next(2) == 0);
            return $"map_of Place list string {string.Join(" ", keys.Select(key => $"{key} {List("string", String)}"))} end";
        }

        private string Inner(int depth) =>
            $"Inner {String()} {(depth > 0 ? Option("Inner", () => Inner(depth - 1)) : "none Inner")} {List("Shape", () => Shape(depth - 1))}";

        private string Shape(int depth) => (depth <= 0 ? random.Next(2) : random.Next(4)) switch
        {
            0 => $"circle {Float()}",
            1 => $"leaf {Place()}",
            2 => $"rect {Float()} {Float()}",
            _ => $"node {Shape(depth - 1)} {Shape(depth - 1)}",
        };

        private string Place() => random.Next(2) == 0 ? "hall" : "study";

        private string Int() => random.Next(4) switch
        {
            0 => "0",
            1 => "-9223372036854775807",
            _ => random.NextInt64(-1_000_000_000_000, 1_000_000_000_000).ToString(CultureInfo.InvariantCulture),
        };

        private string Float()
        {
            var value = Math.Round((random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-3, 8)), random.Next(0, 6));
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.') || text.Contains('E') ? text : text + ".0";
        }

        private string String()
        {
            var builder = new StringBuilder("\"");
            for (var index = random.Next(0, 6); index > 0; index--)
            {
                builder.Append(Characters[random.Next(Characters.Length)] switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    var character => character.ToString(),
                });
            }

            return builder.Append('"').ToString();
        }

        private string List(string type, Func<string> item) =>
            $"list_of {type} {string.Join(" ", Enumerable.Range(0, random.Next(0, 3)).Select(_ => item()))} end";

        private string Map(string key, string value, Func<string> item) =>
            $"map_of {key} {value} {string.Join(" ", Enumerable.Range(0, random.Next(0, 3)).Select(index => $"\"k{index}\" {item()}"))} end";

        private string Option(string type, Func<string> content) => random.Next(2) == 0 ? $"none {type}" : $"some {content()}";
    }
}
