using Pyrop.Runtime;

namespace Pyrop.Tests;

[CollectionDefinition(nameof(FileOperationTests), DisableParallelization = true)]
public sealed class FileOperationCollection;

[Collection(nameof(FileOperationTests))]
public sealed class FileOperationTests : IDisposable
{
    private const string Imports =
        "use files library\nimport from files delete_file copy_file move_file make_folder delete_folder join_path extension_of listing_all current_folder folder_exists folders_in modified_at exists load save end\n";

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-files-").FullName;
    private readonly string _previous = Environment.CurrentDirectory;

    public FileOperationTests() => Environment.CurrentDirectory = _directory;

    public void Dispose()
    {
        Environment.CurrentDirectory = _previous;
        RemoveLinks(_directory);
        Directory.Delete(_directory, recursive: true);
    }

    private static void RemoveLinks(string folder)
    {
        foreach (var inner in new DirectoryInfo(folder).GetDirectories())
        {
            if (inner.LinkTarget is not null)
            {
                inner.Delete();
            }
            else
            {
                RemoveLinks(inner.FullName);
            }
        }
    }

    private static string Program(string source) => Run.Program(Imports + source);

    private static PyropError Failure(string source)
    {
        var error = Run.Failure(Imports + source);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        return error;
    }

    [Fact]
    public void DeletesAFile()
    {
        File.WriteAllText("a.txt", "x");

        Program("delete_file \"a.txt\"");

        Assert.False(File.Exists("a.txt"));
    }

    [Fact]
    public void DeleteFileFailsPlainly()
    {
        Directory.CreateDirectory("folder");

        Assert.Equal("File \"a.txt\" does not exist.", Failure("delete_file \"a.txt\"").Message);
        Assert.Equal("\"folder\" is a directory, not a file.", Failure("delete_file \"folder\"").Message);
        Assert.True(Directory.Exists("folder"));
    }

    [Fact]
    public void CopiesBytesAndKeepsTheTime()
    {
        byte[] bytes = [0, 255, 13, 10, 200];
        File.WriteAllBytes("a.bin", bytes);
        var changed = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc("a.bin", changed);

        Program("copy_file \"a.bin\" \"b.bin\"");

        Assert.Equal(bytes, File.ReadAllBytes("b.bin"));
        Assert.Equal(bytes, File.ReadAllBytes("a.bin"));
        Assert.Equal(changed, File.GetLastWriteTimeUtc("b.bin"));
    }

    [Fact]
    public void CopyAndMoveReplaceTheDestination()
    {
        File.WriteAllText("a.txt", "new");
        File.WriteAllText("b.txt", "old");
        File.WriteAllText("c.txt", "old");

        Program("copy_file \"a.txt\" \"b.txt\"\nmove_file \"a.txt\" \"c.txt\"");

        Assert.Equal("new", File.ReadAllText("b.txt"));
        Assert.Equal("new", File.ReadAllText("c.txt"));
        Assert.False(File.Exists("a.txt"));
    }

    [Theory]
    [InlineData("copy_file", "copied")]
    [InlineData("move_file", "moved")]
    public void TransfersFailPlainly(string operation, string done)
    {
        File.WriteAllText("a.txt", "x");

        Assert.Equal("File \"gone.txt\" does not exist.", Failure($"{operation} \"gone.txt\" \"b.txt\"").Message);
        Assert.Equal($"\"a.txt\" cannot be {done} onto itself.", Failure($"{operation} \"a.txt\" \"./a.txt\"").Message);
        Assert.Equal(
            "Directory \"no\" does not exist, so \"no/b.txt\" cannot be written.",
            Failure($"{operation} \"a.txt\" \"no/b.txt\"").Message);
        Assert.Equal("x", File.ReadAllText("a.txt"));
    }

    [Fact]
    public void MakesAFolderWithItsParentsAndAgain()
    {
        Program("make_folder \"a/b/c\"\nmake_folder \"a/b/c\"");

        Assert.True(Directory.Exists("a/b/c"));
    }

    [Fact]
    public void MakeFolderRefusesAFile()
    {
        File.WriteAllText("a", "x");

        Assert.Equal("\"a\" is a file, not a folder.", Failure("make_folder \"a\"").Message);
    }

    [Fact]
    public void DeletesAWholeFolder()
    {
        Directory.CreateDirectory("a/b");
        File.WriteAllText("a/b/c.txt", "x");

        Program("delete_folder \"a\"");

        Assert.False(Directory.Exists("a"));
    }

    [Fact]
    public void DeleteFolderFailsPlainly()
    {
        File.WriteAllText("a.txt", "x");

        Assert.Equal("Folder \"no\" does not exist.", Failure("delete_folder \"no\"").Message);
        Assert.Equal("\"a.txt\" is a file, not a folder.", Failure("delete_folder \"a.txt\"").Message);
        Assert.True(File.Exists("a.txt"));
    }

    [Fact]
    public void DeleteFolderRefusesARootAndTheFoldersAroundTheProgram()
    {
        File.WriteAllText("keep.txt", "x");
        var root = Path.GetPathRoot(_directory)!.Replace("\\", "\\\\");

        Assert.Equal("\".\" holds the folder the program is running in, so it cannot be deleted.", Failure("delete_folder \".\"").Message);
        Assert.Equal("\"..\" holds the folder the program is running in, so it cannot be deleted.", Failure("delete_folder \"..\"").Message);
        Assert.EndsWith("is a root folder, so it cannot be deleted.", Failure($"delete_folder \"{root}\"").Message);
        Assert.True(File.Exists("keep.txt"));
    }

    [Theory]
    [InlineData("\"a\" \"b.txt\"", "a/b.txt")]
    [InlineData("\"a/\" \"b.txt\"", "a/b.txt")]
    [InlineData("\"a\\\\\" \"b.txt\"", "a\\b.txt")]
    [InlineData("\"\" \"b.txt\"", "b.txt")]
    [InlineData("\"a\" \"\"", "a")]
    [InlineData("\"a\" \"b/c\"", "a/b/c")]
    public void JoinsPathsOnTheTextAlone(string arguments, string expected)
    {
        Assert.Equal(expected + "\n", Program($"write_line join_path {arguments}"));
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("\\\\server")]
    [InlineData("C:\\\\x")]
    public void JoinPathRefusesAFullSecondPart(string second)
    {
        Assert.StartsWith("'join_path' cannot add the full path", Failure($"write_line join_path \"a\" \"{second}\"").Message);
    }

    [Theory]
    [InlineData("a.txt", ".txt")]
    [InlineData("a.tar.gz", ".gz")]
    [InlineData("logs/a.log", ".log")]
    [InlineData("logs.d/a", "")]
    [InlineData(".bashrc", "")]
    [InlineData("a.", "")]
    [InlineData("noextension", "")]
    [InlineData("", "")]
    public void GivesAPathsExtension(string path, string expected)
    {
        Assert.Equal(expected + "\n", Program($"write_line extension_of \"{path}\""));
    }

    [Fact]
    public void ListsAllFilesBelowAFolderSorted()
    {
        Directory.CreateDirectory("top/sub/deep");
        Directory.CreateDirectory("top/empty");
        File.WriteAllText("top/b.txt", "");
        File.WriteAllText("top/A.txt", "");
        File.WriteAllText("top/sub/c.txt", "");
        File.WriteAllText("top/sub/deep/d.txt", "");

        Assert.Equal(
            "[\"A.txt\", \"b.txt\", \"sub/c.txt\", \"sub/deep/d.txt\"]\n",
            Program("write_line listing_all \"top\""));
    }

    [Fact]
    public void ListingAllAcceptsATrailingSeparator()
    {
        Directory.CreateDirectory("top/sub");
        File.WriteAllText("top/sub/c.txt", "");

        Assert.Equal("[\"sub/c.txt\"]\n", Program("write_line listing_all \"top/\""));
    }

    [Fact]
    public void ListsFoldersOnly()
    {
        Directory.CreateDirectory("top/zeta/inner");
        Directory.CreateDirectory("top/Alpha");
        File.WriteAllText("top/file.txt", "");

        Assert.Equal("[\"Alpha\", \"zeta\"]\n", Program("write_line folders_in \"top\""));
    }

    [Theory]
    [InlineData("listing_all")]
    [InlineData("folders_in")]
    public void FolderListingsFailPlainly(string operation)
    {
        File.WriteAllText("a.txt", "");

        Assert.Equal("Folder \"no\" does not exist.", Failure($"write_line {operation} \"no\"").Message);
        Assert.Equal("\"a.txt\" is a file, not a folder.", Failure($"write_line {operation} \"a.txt\"").Message);
        Assert.Equal("A folder path cannot be empty.", Failure($"write_line {operation} \"\"").Message);
    }

    [Fact]
    public void KnowsTheCurrentFolderAndWhichFoldersExist()
    {
        Directory.CreateDirectory("here");
        File.WriteAllText("a.txt", "");

        Assert.Equal(
            $"{Environment.CurrentDirectory}\ntrue\nfalse\nfalse\ntrue\n",
            Program("write_line current_folder\nwrite_line folder_exists \"here\"\nwrite_line folder_exists \"a.txt\"\nwrite_line folder_exists \"no\"\nwrite_line exists \"a.txt\""));
    }

    [Fact]
    public void GivesWhenAFileChangedAsALocalMoment()
    {
        File.WriteAllText("a.txt", "");
        File.SetLastWriteTimeUtc("a.txt", new DateTime(2026, 3, 4, 10, 0, 0, 500, DateTimeKind.Utc));
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(2)));
        const string source =
            "use time library\nimport from time Moment end\ndef Moment m modified_at \"a.txt\"\n" +
            "write_line list_of int the year of m the month of m the day of m the hour of m the minute of m the second of m the millisecond of m the offset_minutes of m end";

        Assert.Equal("[2026, 3, 4, 12, 0, 0, 500, 120]\n", Run.Program(Imports + source, host: HostContext.Isolated with { Clock = clock }));
    }

    [Fact]
    public void ModifiedAtFailsOnAMissingFile()
    {
        Assert.Equal("File \"no.txt\" does not exist.", Failure("write_line the year of modified_at \"no.txt\"").Message);
    }

    private static void Junction(string link, string target)
    {
        var outcome = ProcessCommandRunner.Instance.Capture("cmd", ["/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target)]);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void ListingAllDoesNotFollowLinks()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory("outside");
        File.WriteAllText("outside/keep.txt", "");
        Directory.CreateDirectory("tree/inner");
        File.WriteAllText("tree/inner/f.txt", "");
        Junction("tree/link", "outside");
        Junction("tree/inner/back", "tree");

        Assert.Equal("[\"inner/f.txt\"]\n", Program("write_line listing_all \"tree\""));
    }

    [Fact]
    public void DeleteFolderRemovesLinksButNotWhatTheyPointAt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory("outside");
        File.WriteAllText("outside/keep.txt", "x");
        Directory.CreateDirectory("tree/inner");
        File.WriteAllText("tree/inner/f.txt", "");
        Junction("tree/link", "outside");
        Junction("tree/inner/link", "outside");

        Program("delete_folder \"tree\"");

        Assert.False(Directory.Exists("tree"));
        Assert.True(File.Exists("outside/keep.txt"));
    }

    [Fact]
    public void DeleteFolderSeesThroughLinksToTheCurrentFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory("real/sub");
        File.WriteAllText("real/sub/here.txt", "x");
        Junction("link", "real");
        Environment.CurrentDirectory = Path.Combine(_directory, "real", "sub");

        Assert.Equal(
            "\"../../link/sub\" holds the folder the program is running in, so it cannot be deleted.",
            Failure("delete_folder \"../../link/sub\"").Message);
        Assert.True(File.Exists("here.txt"));
    }

    [Theory]
    [InlineData("//?/")]
    [InlineData("//./")]
    public void DeleteFolderRefusesDevicePaths(string prefix)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory("victim");
        File.WriteAllText("victim/keep.txt", "x");
        var path = prefix + Path.Combine(_directory, "victim").Replace('\\', '/');

        Assert.EndsWith("write it as a plain path to delete it.", Failure($"delete_folder \"{path}\"").Message);
        Assert.True(File.Exists("victim/keep.txt"));
    }

    [Fact]
    public void DeleteFolderRefusesAShareOnThisComputer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory("victim");
        File.WriteAllText("victim/keep.txt", "x");
        var full = Path.Combine(_directory, "victim").Replace('\\', '/');
        var path = $"//localhost/{full[0]}${full[2..]}";

        Assert.EndsWith("write it as a plain path to delete it.", Failure($"delete_folder \"{path}\"").Message);
        Assert.True(File.Exists("victim/keep.txt"));
    }

    [Fact]
    public void ATransferErrorNamesTheDestinationWhenThatIsTheProblem()
    {
        File.WriteAllText("src.txt", "x");
        File.WriteAllText("readonly.txt", "y");
        File.SetAttributes("readonly.txt", FileAttributes.ReadOnly);
        try
        {
            Assert.Equal("Access to \"readonly.txt\" was denied.", Failure("copy_file \"src.txt\" \"readonly.txt\"").Message);
        }
        finally
        {
            File.SetAttributes("readonly.txt", FileAttributes.Normal);
        }
    }

    [Fact]
    public void ATransferErrorNamesALockedSource()
    {
        File.WriteAllText("src.txt", "x");
        using var locked = new FileStream("src.txt", FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("Cannot move \"src.txt\" because another process is using it.", Failure("move_file \"src.txt\" \"b.txt\"").Message);
    }

    [Fact]
    public void AttemptCatchesAFailedDelete()
    {
        Assert.Equal("false\n", Program("write_line attempt delete_file \"no.txt\""));
    }
}
