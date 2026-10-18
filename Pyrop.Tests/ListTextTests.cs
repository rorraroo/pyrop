using System.Diagnostics;
using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class ListTextTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-lists-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static HostContext Arguments(params string[] arguments) => new(arguments, _ => null);

    private string RunWith(string source, params string[] paths) =>
        Run.Program(source, host: Arguments(paths.Select(PathOf).ToArray()));

    private PyropError FailWith(string source, params string[] paths) =>
        Run.Failure(source, host: Arguments(paths.Select(PathOf).ToArray()));

    private static PyropError RuntimeFailure(string source)
    {
        var error = Run.Failure(source);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        return error;
    }

    [Theory]
    [InlineData("split \"a,b,c\" \",\"", "[\"a\", \"b\", \"c\"]")]
    [InlineData("split \"a,,b,\" \",\"", "[\"a\", \"\", \"b\", \"\"]")]
    [InlineData("split \",a\" \",\"", "[\"\", \"a\"]")]
    [InlineData("split \"abc\" \",\"", "[\"abc\"]")]
    [InlineData("split \"\" \",\"", "[\"\"]")]
    [InlineData("split \"a, b, c\" \", \"", "[\"a\", \"b\", \"c\"]")]
    [InlineData("split \"aaaa\" \"aa\"", "[\"\", \"\", \"\"]")]
    [InlineData("split \"aaa\" \"aa\"", "[\"\", \"a\"]")]
    [InlineData("split \"A,a\" \"a\"", "[\"A,\", \"\"]")]
    [InlineData("split \"x\\ny\\n\" \"\\n\"", "[\"x\", \"y\", \"\"]")]
    public void SplitCutsAtEveryMatch(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program($"write_line {expression}"));
    }

    [Fact]
    public void SplitMatchesOnlyWholeCharacters()
    {
        Assert.Equal("[\"c\u030C\"]\n", Run.Program("write_line split \"c\u030C\" \"c\""));
        Assert.Equal("[\"\u010D\", \"\u010D\"]\n", Run.Program("write_line split \"\u010D-\u010D\" \"-\""));
        Assert.Equal("[\"a\", \"b\"]\n", Run.Program("write_line split \"a\U0001F5E1\uFE0Fb\" \"\U0001F5E1\uFE0F\""));
    }

    [Fact]
    public void SplitTreatsACarriageReturnAndLineFeedAsOneCharacter()
    {
        var host = new HostContext(["a\r\nb"], _ => null);

        Assert.Equal("1\ntrue\n", Run.Program(
            "write_line length_of split argument_at 0 \"\\n\"\nwrite_line equal item_at split argument_at 0 \"\\n\" 0 argument_at 0",
            host: host));
    }

    [Fact]
    public void SplitRejectsAnEmptySeparator()
    {
        var error = RuntimeFailure("def string sep \"\"\nwrite_line split \"abc\" sep");

        Assert.Equal("'split' cannot split at an empty separator.", error.Message);
        Assert.Equal(2, error.Position.Line);
        Assert.Equal(12, error.Position.Column);
    }

    [Fact]
    public void SplitGivesAListOfStrings()
    {
        Assert.Equal("3\nb\n", Run.Program("def list string parts split \"a b c\" \" \"\nwrite_line length_of parts\nwrite_line item_at parts 1"));
    }

    [Fact]
    public void SplitRespectsTheSizeLimit()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { CollectionLimit = 3 };

        session.Run("write_line split \"a,b,c\" \",\"");
        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run("write_line split \"a,b,c,d\" \",\"")).Errors);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("A list cannot hold more than 3 items.", error.Message);
        Assert.Equal("[\"a\", \"b\", \"c\"]\n", output.ToString());
    }

    [Fact]
    public void SplittingALongTextStaysFast()
    {
        var watch = Stopwatch.StartNew();
        var output = Run.Program("def string text repeat \"word \" 200000\nwrite_line length_of split text \" \"\nwrite_line length_of join split text \" \" \" \"");

        Assert.Equal("200001\n1000000\n", output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Theory]
    [InlineData("join list_of string \"a\" \"b\" \"c\" end \", \"", "a, b, c")]
    [InlineData("join list_of string \"a\" end \", \"", "a")]
    [InlineData("join list_of string end \", \"", "")]
    [InlineData("join list_of string \"\" \"\" end \"-\"", "-")]
    [InlineData("join list_of string \"x\" \"y\" end \"\"", "xy")]
    public void JoinPutsTheSeparatorBetweenItems(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program($"write_line {expression}"));
    }

    [Theory]
    [InlineData("a,,b,", ",")]
    [InlineData("", ",")]
    [InlineData(",", ",")]
    [InlineData("one two", "  ")]
    public void JoinUndoesSplit(string text, string separator)
    {
        Assert.Equal("true\n", Run.Program($"write_line equal join split \"{text}\" \"{separator}\" \"{separator}\" \"{text}\""));
    }

    [Fact]
    public void JoinRespectsTheTextLimit()
    {
        var error = RuntimeFailure("def string half repeat \"a\" 50000000\nwrite_line length_of join list_of string half half end \"b\"");

        Assert.Equal("The result of 'join' would exceed the largest allowed text size.", error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void JoinAcceptsResultsUpToTheTextLimit()
    {
        Assert.Equal(
            "100000000\n",
            Run.Program("def string half repeat \"a\" 49999999\nwrite_line length_of join list_of string half half end \"bb\""));
    }

    [Theory]
    [InlineData("join list_of int 1 end \",\"", "'join' cannot take list int string. It accepts: list string string.")]
    [InlineData("split \"a\" 1", "'split' cannot take string int. It accepts: string string.")]
    public void RejectsWrongOperandTypes(string expression, string message)
    {
        var error = Run.Failure($"write_line {expression}");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("a\nb\n", "[\"a\", \"b\"]")]
    [InlineData("a\nb", "[\"a\", \"b\"]")]
    [InlineData("", "[]")]
    [InlineData("\n", "[\"\"]")]
    [InlineData("\n\n", "[\"\", \"\"]")]
    [InlineData("a\r\n\r\nb\rc\r\n", "[\"a\", \"\", \"b\", \"c\"]")]
    [InlineData("\uFEFFžluť\n", "[\"žluť\"]")]
    public void ReadLinesGivesTheLinesOfAFile(string content, string expected)
    {
        File.WriteAllText(PathOf("lines.txt"), content, new UTF8Encoding(false));

        Assert.Equal(expected + "\n", RunWith("write_line read_lines argument_at 0", "lines.txt"));
    }

    [Fact]
    public void ReadLinesWorksWithFor()
    {
        File.WriteAllText(PathOf("todo.txt"), "- milk\nnote\n- bread\n");

        Assert.Equal(
            "- milk\n- bread\n",
            RunWith("for string line in read_lines argument_at 0 if starts_with line \"- \" write_line line", "todo.txt"));
    }

    [Theory]
    [InlineData("missing.txt", "File \"{0}\" does not exist.")]
    [InlineData("", "A file path cannot be empty.")]
    public void ReadLinesFailsLikeReadFile(string name, string message)
    {
        var path = name.Length == 0 ? "" : PathOf(name);
        var error = Run.Failure("write_line read_lines argument_at 0", host: Arguments(path));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(string.Format(message, path), error.Message);
    }

    [Fact]
    public void ReadLinesRejectsADirectory()
    {
        Directory.CreateDirectory(PathOf("folder"));

        Assert.Equal($"\"{PathOf("folder")}\" is a directory, not a file.", FailWith("write_line read_lines argument_at 0", "folder").Message);
    }

    [Fact]
    public void ReadLinesRejectsAFileThatIsNotUtf8()
    {
        File.WriteAllBytes(PathOf("binary.bin"), [0x61, 0x0A, 0xFF, 0xFE]);

        Assert.Equal($"File \"{PathOf("binary.bin")}\" is not valid UTF-8 text.", FailWith("write_line read_lines argument_at 0", "binary.bin").Message);
    }

    [Fact]
    public void ReadLinesRespectsTheSizeLimit()
    {
        File.WriteAllText(PathOf("four.txt"), "a\nb\nc\nd\n");
        File.WriteAllText(PathOf("three.txt"), "a\nb\nc\n");
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output, Arguments(PathOf("three.txt"), PathOf("four.txt"))) { CollectionLimit = 3 };

        session.Run("write_line read_lines argument_at 0");
        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run("write_line read_lines argument_at 1")).Errors);

        Assert.Equal("A list cannot hold more than 3 items.", error.Message);
        Assert.Equal("[\"a\", \"b\", \"c\"]\n", output.ToString());
    }

    [Fact]
    public void ListFilesGivesSortedFileNamesOnly()
    {
        Directory.CreateDirectory(PathOf("saves"));
        foreach (var name in new[] { "b.save", "a.txt", "ž.save", "_x" })
        {
            File.WriteAllText(Path.Combine(PathOf("saves"), name), "");
        }

        Directory.CreateDirectory(Path.Combine(PathOf("saves"), "nested"));
        File.WriteAllText(Path.Combine(PathOf("saves"), "nested", "deep.save"), "");

        Assert.Equal("[\"_x\", \"a.txt\", \"b.save\", \"ž.save\"]\n", RunWith("write_line list_files argument_at 0", "saves"));
    }

    [Fact]
    public void ListFilesSortsOrdinally()
    {
        Directory.CreateDirectory(PathOf("mixed"));
        foreach (var name in new[] { "b", "A", "a1", "Z", "10", "9", "ä" })
        {
            File.WriteAllText(Path.Combine(PathOf("mixed"), name), "");
        }

        Assert.Equal("[\"10\", \"9\", \"A\", \"Z\", \"a1\", \"b\", \"ä\"]\n", RunWith("write_line list_files argument_at 0", "mixed"));
    }

    [Fact]
    public void ListFilesIncludesHiddenFiles()
    {
        Directory.CreateDirectory(PathOf("hidden"));
        File.WriteAllText(Path.Combine(PathOf("hidden"), ".secret"), "");
        File.SetAttributes(Path.Combine(PathOf("hidden"), ".secret"), FileAttributes.Hidden);

        Assert.Equal("[\".secret\"]\n", RunWith("write_line list_files argument_at 0", "hidden"));
    }

    [Fact]
    public void ListFilesOfAnEmptyDirectoryIsEmpty()
    {
        Directory.CreateDirectory(PathOf("nothing"));

        Assert.Equal("[]\n", RunWith("write_line list_files argument_at 0", "nothing"));
    }

    [Fact]
    public void ListFilesWorksWithFor()
    {
        Directory.CreateDirectory(PathOf("saves"));
        File.WriteAllText(Path.Combine(PathOf("saves"), "one.save"), "");
        File.WriteAllText(Path.Combine(PathOf("saves"), "notes.txt"), "");

        Assert.Equal(
            "one.save\n",
            RunWith("for string name in list_files argument_at 0 if ends_with name \".save\" write_line name", "saves"));
    }

    [Fact]
    public void ListFilesRejectsAMissingDirectory()
    {
        var error = FailWith("write_line list_files argument_at 0", "nowhere");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"Directory \"{PathOf("nowhere")}\" does not exist.", error.Message);
    }

    [Fact]
    public void ListFilesRejectsAFile()
    {
        File.WriteAllText(PathOf("notes.txt"), "");

        Assert.Equal($"\"{PathOf("notes.txt")}\" is a file, not a directory.", FailWith("write_line list_files argument_at 0", "notes.txt").Message);
    }

    [Theory]
    [InlineData("write_line list_files \"\"", "A directory path cannot be empty.")]
    [InlineData("write_line list_files \"a\\tb\"", "\"a\tb\" is not a valid directory path; it contains a control character.")]
    public void ListFilesRejectsInvalidPaths(string source, string message)
    {
        Assert.Equal(message, RuntimeFailure(source).Message);
    }

    [Fact]
    public void ListFilesRejectsAPathTheSystemCannotUse()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("\"a*b\" is not a valid directory path.", RuntimeFailure("write_line list_files \"a*b\"").Message);
    }

    [Fact]
    public void ListFilesRespectsTheSizeLimit()
    {
        Directory.CreateDirectory(PathOf("many"));
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            File.WriteAllText(Path.Combine(PathOf("many"), name), "");
        }

        var session = new PyropSession(new StringReader(""), new StringOutput(), Arguments(PathOf("many"))) { CollectionLimit = 3 };
        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run("write_line list_files argument_at 0")).Errors);

        Assert.Equal("A list cannot hold more than 3 items.", error.Message);
    }

    [Theory]
    [InlineData("split")]
    [InlineData("join")]
    [InlineData("read_lines")]
    [InlineData("list_files")]
    [InlineData("write_error")]
    [InlineData("write_error_line")]
    public void ReservesTheNewNames(string name)
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure($"def string {name} \"x\"").Stage);
    }
}
