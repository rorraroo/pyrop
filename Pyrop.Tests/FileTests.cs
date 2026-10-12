using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class FileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static HostContext Arguments(params string[] arguments) => new(arguments, _ => null);

    private string RunWith(string source, params string[] files) =>
        Run.Program(source, host: Arguments(files.Select(PathOf).ToArray()));

    private PyropError FailWith(string source, params string[] files) =>
        Run.Failure(source, host: Arguments(files.Select(PathOf).ToArray()));

    [Fact]
    public void WritesAFileAndReadsItBack()
    {
        var output = RunWith(
            "write_file argument_at 0 \"Příliš\\nžluťoučký\"\nwrite_line read_file argument_at 0",
            "notes.txt");

        Assert.Equal("Příliš\nžluťoučký\n", output);
        Assert.Equal(Encoding.UTF8.GetBytes("Příliš\nžluťoučký"), File.ReadAllBytes(PathOf("notes.txt")));
    }

    [Fact]
    public void WriteFileReplacesExistingContent()
    {
        File.WriteAllText(PathOf("log.txt"), "old content");

        RunWith("write_file argument_at 0 \"new\"", "log.txt");

        Assert.Equal("new", File.ReadAllText(PathOf("log.txt")));
    }

    [Fact]
    public void AppendFileCreatesTheFileAndAppendsToIt()
    {
        RunWith("append_file argument_at 0 \"žluť\\n\"\nappend_file argument_at 0 \"two\\n\"", "log.txt");

        Assert.Equal(Encoding.UTF8.GetBytes("žluť\ntwo\n"), File.ReadAllBytes(PathOf("log.txt")));
    }

    [Fact]
    public void WriteFileReplacesAHiddenFile()
    {
        File.WriteAllText(PathOf("hidden.txt"), "old content");
        File.SetAttributes(PathOf("hidden.txt"), FileAttributes.Hidden);

        RunWith("write_file argument_at 0 \"new\"", "hidden.txt");

        Assert.Equal("new", File.ReadAllText(PathOf("hidden.txt")));
    }

    [Fact]
    public void ReadFileNormalizesLineEndings()
    {
        File.WriteAllBytes(PathOf("mixed.txt"), Encoding.ASCII.GetBytes("a\r\nb\rc\nd"));

        Assert.Equal("a\nb\nc\nd", RunWith("write read_file argument_at 0", "mixed.txt"));
    }

    [Fact]
    public void ReadFileSkipsAByteOrderMark()
    {
        File.WriteAllBytes(PathOf("bom.txt"), [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        Assert.Equal("hi", RunWith("write read_file argument_at 0", "bom.txt"));
    }

    [Fact]
    public void RejectsAFileThatIsNotUtf8()
    {
        File.WriteAllBytes(PathOf("legacy.txt"), [(byte)'P', 0xF8, (byte)'i']);

        var error = FailWith("write_line read_file argument_at 0", "legacy.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"File \"{PathOf("legacy.txt")}\" is not valid UTF-8 text.", error.Message);
    }

    [Fact]
    public void RejectsReadingAMissingFile()
    {
        var error = FailWith("write_line read_file argument_at 0", "missing.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"File \"{PathOf("missing.txt")}\" does not exist.", error.Message);
    }

    [Fact]
    public void RejectsWritingIntoAMissingDirectoryWithoutCreatingIt()
    {
        var error = FailWith("write_file argument_at 0 \"x\"", Path.Combine("out", "report.txt"));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(
            $"Directory \"{PathOf("out")}\" does not exist, so \"{PathOf(Path.Combine("out", "report.txt"))}\" cannot be written.",
            error.Message);
        Assert.False(Directory.Exists(PathOf("out")));
    }

    [Theory]
    [InlineData("write_line read_file argument_at 0")]
    [InlineData("write_file argument_at 0 \"x\"")]
    [InlineData("append_file argument_at 0 \"x\"")]
    public void RejectsADirectoryAsAFile(string source)
    {
        Directory.CreateDirectory(PathOf("folder"));

        var error = FailWith(source, "folder");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{PathOf("folder")}\" is a directory, not a file.", error.Message);
    }

    [Theory]
    [InlineData("write_line read_file \"\"")]
    [InlineData("write_file \"\" \"x\"")]
    public void RejectsAnEmptyPath(string source)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("A file path cannot be empty.", error.Message);
    }

    [Fact]
    public void RejectsAPathContainingAControlCharacter()
    {
        var error = FailWith("write_file argument_at 0 \"x\"", "a\tb");

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

        var error = FailWith("write_file argument_at 0 \"x\"", "a*b.txt");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"\"{PathOf("a*b.txt")}\" is not a valid file path.", error.Message);
    }

    [Fact]
    public void NamesTheMissingDirectoryAsWritten()
    {
        var error = FailWith("write_file \"no_such_directory/inner/x.txt\" \"x\"");

        Assert.Equal("Directory \"no_such_directory/inner\" does not exist, so \"no_such_directory/inner/x.txt\" cannot be written.", error.Message);
    }

    [Fact]
    public void RejectsWritingBelowAFile()
    {
        File.WriteAllText(PathOf("plain.txt"), "");

        var error = FailWith("write_file argument_at 0 \"x\"", Path.Combine("plain.txt", "x.txt"));

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.StartsWith($"\"{PathOf("plain.txt")}\" is a file, not a directory", error.Message);
    }

    [Fact]
    public void FileExistsIsTrueOnlyForFiles()
    {
        File.WriteAllText(PathOf("present.txt"), "");
        Directory.CreateDirectory(PathOf("folder"));

        Assert.Equal(
            "true\nfalse\nfalse\nfalse\n",
            RunWith(
                "write_line file_exists argument_at 0\nwrite_line file_exists argument_at 1\n" +
                "write_line file_exists argument_at 2\nwrite_line file_exists \"\"",
                "present.txt", "absent.txt", "folder"));
    }

    [Theory]
    [InlineData("write_line write_file \"a\" \"b\"")]
    [InlineData("def string s append_file \"a\" \"b\"")]
    public void RejectsAVoidOperationUsedAsAValue(string source)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains("returns void and cannot be used as a value", error.Message);
    }

    [Fact]
    public void ChecksFileOperationOperandTypes()
    {
        var error = Run.Failure("write_file \"a\" 1");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'write_file' cannot take string int. It accepts: string string.", error.Message);
    }
}
