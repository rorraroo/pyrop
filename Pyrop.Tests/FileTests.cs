using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class FileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Libraries =
        "use files library\nimport from files load save append file_exists end\n" +
        "use program library\nimport from program arguments end\n";

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static HostContext Arguments(params string[] arguments) => new(arguments, _ => null);

    private string RunWith(string source, params string[] files) =>
        Run.Program(Libraries + source, host: Arguments(files.Select(PathOf).ToArray()));

    private PyropError FailWith(string source, params string[] files) =>
        Run.Failure(Libraries + source, host: Arguments(files.Select(PathOf).ToArray()));

    [Fact]
    public void WritesAFileAndReadsItBack()
    {
        var output = RunWith(
            "save get arguments 0 \"Příliš\\nžluťoučký\"\nwrite_line load get arguments 0",
            "notes.txt");

        Assert.Equal("Příliš\nžluťoučký\n", output);
        Assert.Equal(Encoding.UTF8.GetBytes("Příliš\nžluťoučký"), File.ReadAllBytes(PathOf("notes.txt")));
    }

    [Fact]
    public void SaveReplacesExistingContent()
    {
        File.WriteAllText(PathOf("log.txt"), "old content");

        RunWith("save get arguments 0 \"new\"", "log.txt");

        Assert.Equal("new", File.ReadAllText(PathOf("log.txt")));
    }

    [Fact]
    public void AppendCreatesTheFileAndAppendsToIt()
    {
        RunWith("append get arguments 0 \"žluť\\n\"\nappend get arguments 0 \"two\\n\"", "log.txt");

        Assert.Equal(Encoding.UTF8.GetBytes("žluť\ntwo\n"), File.ReadAllBytes(PathOf("log.txt")));
    }

    [Fact]
    public void SaveReplacesAHiddenFile()
    {
        File.WriteAllText(PathOf("hidden.txt"), "old content");
        File.SetAttributes(PathOf("hidden.txt"), FileAttributes.Hidden);

        RunWith("save get arguments 0 \"new\"", "hidden.txt");

        Assert.Equal("new", File.ReadAllText(PathOf("hidden.txt")));
    }

    [Fact]
    public void LoadNormalizesLineEndings()
    {
        File.WriteAllBytes(PathOf("mixed.txt"), Encoding.ASCII.GetBytes("a\r\nb\rc\nd"));

        Assert.Equal("a\nb\nc\nd", RunWith("write load get arguments 0", "mixed.txt"));
    }

    [Fact]
    public void LoadSkipsAByteOrderMark()
    {
        File.WriteAllBytes(PathOf("bom.txt"), [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        Assert.Equal("hi", RunWith("write load get arguments 0", "bom.txt"));
    }

    [Fact]
    public void RejectsAFileThatIsNotUtf8()
    {
        File.WriteAllBytes(PathOf("legacy.txt"), [(byte)'P', 0xF8, (byte)'i']);

        var error = FailWith("write_line load get arguments 0", "legacy.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"File \"{PathOf("legacy.txt")}\" is not valid UTF-8 text.", error.Message);
    }

    [Fact]
    public void RejectsReadingAMissingFile()
    {
        var error = FailWith("write_line load get arguments 0", "missing.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"File \"{PathOf("missing.txt")}\" does not exist.", error.Message);
    }

    [Fact]
    public void RejectsWritingIntoAMissingDirectoryWithoutCreatingIt()
    {
        var error = FailWith("save get arguments 0 \"x\"", Path.Combine("out", "report.txt"));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(
            $"Directory \"{PathOf("out")}\" does not exist, so \"{PathOf(Path.Combine("out", "report.txt"))}\" cannot be written.",
            error.Message);
        Assert.False(Directory.Exists(PathOf("out")));
    }

    [Theory]
    [InlineData("write_line load get arguments 0")]
    [InlineData("save get arguments 0 \"x\"")]
    [InlineData("append get arguments 0 \"x\"")]
    public void RejectsADirectoryAsAFile(string source)
    {
        Directory.CreateDirectory(PathOf("folder"));

        var error = FailWith(source, "folder");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{PathOf("folder")}\" is a directory, not a file.", error.Message);
    }

    [Theory]
    [InlineData("write_line load \"\"")]
    [InlineData("save \"\" \"x\"")]
    public void RejectsAnEmptyPath(string source)
    {
        var error = Run.Failure(Libraries + source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("A file path cannot be empty.", error.Message);
    }

    [Fact]
    public void RejectsAPathContainingAControlCharacter()
    {
        var error = FailWith("save get arguments 0 \"x\"", "a\tb");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{PathOf("a\tb")}\" is not a valid file path; it contains a control character.", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void RejectsANameTheOperatingSystemDoesNotAllowWithoutItsMessage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var error = FailWith("save get arguments 0 \"x\"", "a*b.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{PathOf("a*b.txt")}\" is not a valid file path.", error.Message);
    }

    [Fact]
    public void NamesTheMissingDirectoryAsWritten()
    {
        var error = FailWith("save \"no_such_directory/inner/x.txt\" \"x\"");

        Assert.Equal("Directory \"no_such_directory/inner\" does not exist, so \"no_such_directory/inner/x.txt\" cannot be written.", error.Message);
    }

    [Fact]
    public void RejectsWritingBelowAFile()
    {
        File.WriteAllText(PathOf("plain.txt"), "");

        var error = FailWith("save get arguments 0 \"x\"", Path.Combine("plain.txt", "x.txt"));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.StartsWith($"\"{PathOf("plain.txt")}\" is a file, not a directory", error.Message);
    }

    [Fact]
    public void ExistsIsTrueOnlyForFiles()
    {
        File.WriteAllText(PathOf("present.txt"), "");
        Directory.CreateDirectory(PathOf("folder"));

        Assert.Equal(
            "true\nfalse\nfalse\nfalse\n",
            RunWith(
                "write_line file_exists get arguments 0\nwrite_line file_exists get arguments 1\n" +
                "write_line file_exists get arguments 2\nwrite_line file_exists \"\"",
                "present.txt", "absent.txt", "folder"));
    }

    [Theory]
    [InlineData("write_line save \"a\" \"b\"")]
    [InlineData("define string s append \"a\" \"b\"")]
    public void RejectsAVoidOperationUsedAsAValue(string source)
    {
        var error = Run.Failure(Libraries + source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains("returns void and cannot be used as a value", error.Message);
    }

    [Fact]
    public void ChecksFileOperationOperandTypes()
    {
        var error = Run.Failure(Libraries + "save \"a\" 1");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'save' cannot take string int. It accepts: string string.", error.Message);
    }

    [Theory]
    [InlineData("logs/server.log", "server.log")]
    [InlineData("C:\\\\logs\\\\server.log", "server.log")]
    [InlineData("a\\\\b/c.txt", "c.txt")]
    [InlineData("server.log", "server.log")]
    [InlineData("logs/", "")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData("no such directory/no such file", "no such file")]
    [InlineData("a\\tb", "a\tb")]
    public void NameOfGivesTheTextAfterTheLastSeparator(string path, string expected)
    {
        Assert.Equal($"[{expected}]\n", Run.Program($"use files library\nwrite_line fill \"[{{}}]\" list_of string from files name_of \"{path}\" end"));
    }
}
