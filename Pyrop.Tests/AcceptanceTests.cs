using Pyrop.Bundling;
using Pyrop.Cli;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class AcceptanceTests : IDisposable
{
    private static readonly string Samples = FindSamples();

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-acceptance-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Script => Path.Combine(Samples, "logcheck", "logcheck.prp");

    private static string ExampleLog => Path.Combine(Samples, "logcheck", "example.log");

    private static string Adventure => Path.Combine(Samples, "adventure", "adventure.prp");

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static (string Output, string Errors, int ExitCode) Run(string[] arguments) =>
        RunSource(File.ReadAllText(Script), Script, null, null, arguments);

    private static (string Output, string Errors, int ExitCode) RunSource(
        string source, string path, string? fullPath, Parsing.ISourceStore? store, string[] arguments, string input = "")
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(input), output, new HostContext(arguments, _ => null), store);
        var exitCode = session.Run(source, path, fullPath) ?? 0;
        return (output.ToString(), output.Errors, exitCode);
    }

    [Fact]
    public void ReportsTheErrorsInALog()
    {
        var (output, errors, exitCode) = Run([ExampleLog, PathOf("report.txt")]);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"8 lines, 2 errors, 2 warnings\n<danger>Errors found; see {PathOf("report.txt")}.\n</danger>",
            output);
        Assert.Equal("", errors);
        Assert.Equal(
            $"Errors in {ExampleLog}\n\n" +
            "    5: ERROR 2026-10-12 09:02:11 Payment gateway timed out\n" +
            "    7: error 2026-10-12 09:02:20 Payment gateway refused the request\n" +
            "\n8 lines, 2 errors, 2 warnings\n",
            File.ReadAllText(PathOf("report.txt")));
    }

    [Fact]
    public void ExitsWithZeroForACleanLog()
    {
        File.WriteAllText(PathOf("clean.log"), "INFO started\r\n\r\nWARN slow\r\n");

        var (output, errors, exitCode) = Run([PathOf("clean.log"), PathOf("report.txt")]);

        Assert.Equal(0, exitCode);
        Assert.Equal("2 lines, 0 errors, 1 warning\n<success>No errors.\n</success>", output);
        Assert.Equal("", errors);
        Assert.Equal($"Errors in {PathOf("clean.log")}\n\n\n2 lines, 0 errors, 1 warning\n", File.ReadAllText(PathOf("report.txt")));
    }

    [Fact]
    public void NumbersLinesWhetherOrNotTheLastOneEndsWithANewline()
    {
        File.WriteAllText(PathOf("short.log"), "\n  error first\n\nERROR last");

        var (output, _, exitCode) = Run([PathOf("short.log"), PathOf("report.txt")]);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("2 lines, 2 errors, 0 warnings\n", output);
        Assert.Equal(
            $"Errors in {PathOf("short.log")}\n\n    2: error first\n    4: ERROR last\n\n2 lines, 2 errors, 0 warnings\n",
            File.ReadAllText(PathOf("report.txt")));
    }

    [Fact]
    public void ExplainsItsUsageOnStandardError()
    {
        Assert.Equal(("", "usage: logcheck <log file> [report file]\n", 2), Run([]));
    }

    [Fact]
    public void ReportsAMissingLogOnStandardError()
    {
        Assert.Equal(("", "<danger>No such file: missing.log\n</danger>", 3), Run(["missing.log"]));
    }

    [Fact]
    public void ShipsAsAnExecutableThatBehavesTheSame()
    {
        var baseExecutable = PathOf("pyrop-base");
        File.WriteAllBytes(baseExecutable, "base"u8.ToArray());
        var executable = PathOf(OperatingSystem.IsWindows() ? "logcheck.exe" : "logcheck");

        Assert.Equal(0, BuildCommand.Build(Script, executable, baseExecutable));

        var bundle = Bundle.ReadFrom(executable)!;
        Assert.Equal(["lines.prp", "logcheck.prp"], bundle.Files.Select(file => file.Name));
        var (output, errors, exitCode) = RunSource(
            bundle.Entry.Text,
            bundle.EntryName,
            BundleSourceStore.FullPathOf(bundle.EntryName),
            new BundleSourceStore(bundle),
            [ExampleLog, PathOf("built-report.txt")]);
        Assert.Equal(1, exitCode);
        Assert.StartsWith("8 lines, 2 errors, 2 warnings\n", output);
        Assert.Equal("", errors);
        Assert.True(File.Exists(PathOf("built-report.txt")));
        Assert.Equal(
            ("", "<danger>No such file: missing.log\n</danger>", 3),
            RunSource(
                bundle.Entry.Text,
                bundle.EntryName,
                BundleSourceStore.FullPathOf(bundle.EntryName),
                new BundleSourceStore(bundle),
                ["missing.log"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaysTheAdventureToItsEnding(bool built)
    {
        var (output, errors, exitCode) = Play(built, FullPlaythrough);

        Assert.Equal(Transcript, output);
        Assert.Equal("", errors);
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(PathOf("game.save")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavesQuitsLoadsAndFinishesTheAdventure(bool built)
    {
        var (first, firstErrors, firstExit) = Play(built, "1", "3", "1", "1", "1", "3", "4", "5", "n");

        Assert.Equal(0, firstExit);
        Assert.Equal("", firstErrors);
        AssertInOrder(
            first,
            "<emphasis>Cellar\n</emphasis>It is pitch dark. You cannot see a thing.\nWhat do you do?\n1. Go north\n2. Show the inventory\n",
            "The door is locked. You need the key.\n",
            "You take the lamp.\n",
            "<success>The game is saved.\n</success>",
            "Choose a number from 1 to 5: Save before you leave? [y/n] Goodbye.\n");
        Assert.EndsWith("Save before you leave? [y/n] Goodbye.\n", first);
        Assert.Equal(
            "pyrop adventure save 1\nplace library\nmoves 5\ncarrying lamp\n",
            File.ReadAllText(PathOf("game.save")));

        var (second, secondErrors, secondExit) = Play(built, "2", "2", "3", "2", "1", "1", "1", "2", "1", "2", "2");

        Assert.Equal(0, secondExit);
        Assert.Equal("", secondErrors);
        AssertInOrder(
            second,
            "Choose a number from 1 to 3: Your saved game is restored after 5 moves.\n\n<emphasis>Library\n</emphasis>",
            "You see the key.\n",
            "You unlock the door with the key.\n",
            "<success>You carry the crown out into the sunlight. You won in 15 moves.\n</success>");
        Assert.EndsWith("You won in 15 moves.\n</success>", second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffersToSaveWhenQuitting(bool built)
    {
        var (output, _, exitCode) = Play(built, "1", "1", "6", "y");

        Assert.Equal(0, exitCode);
        Assert.EndsWith("Save before you leave? [y/n] <success>The game is saved.\n</success>Goodbye.\n", output);
        Assert.Equal("pyrop adventure save 1\nplace library\nmoves 1\n", File.ReadAllText(PathOf("game.save")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartsANewGameWhenThereIsNoSave(bool built)
    {
        var (output, _, exitCode) = Play(built, "2", "6", "n");

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Choose a number from 1 to 3: <warning>There is no saved game yet, so a new game begins.\n</warning>\n<emphasis>Great Hall\n</emphasis>",
            output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a save\n")]
    [InlineData("pyrop adventure save 2\nplace hall\nmoves 1\n")]
    [InlineData("pyrop adventure save 1\nplace attic\nmoves 1\n")]
    [InlineData("pyrop adventure save 1\nplace Hall\nmoves 1\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves -1\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves many\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 1000001\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 9223372036854775807\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 2\ncarrying sword\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 2\ncarrying lamp\ncarrying lamp\n")]
    [InlineData("pyrop adventure save 1\nplace hall\n")]
    [InlineData("pyrop adventure save 1\nmoves 3\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nplace tower\nmoves 2\n")]
    [InlineData("pyrop adventure save 1\nplace  hall\nmoves 2\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 2\n\n")]
    [InlineData("pyrop adventure save 1\nplace hall\nmoves 2\nhealth 10\n")]
    public void StartsANewGameWhenTheSaveIsDamaged(string save)
    {
        File.WriteAllText(PathOf("game.save"), save);

        foreach (var built in new[] { false, true })
        {
            var (output, errors, exitCode) = Play(built, "2", "6", "n");

            Assert.Equal(0, exitCode);
            Assert.Equal("", errors);
            Assert.Contains(
                "Choose a number from 1 to 3: <warning>The saved game is damaged, so a new game begins.\n</warning>\n<emphasis>Great Hall\n</emphasis>",
                output);
        }
    }

    [Fact]
    public void StartsANewGameWhenTheSaveIsNotText()
    {
        File.WriteAllBytes(PathOf("game.save"), [0x70, 0xFF, 0xFE, 0x0A]);

        var (output, _, exitCode) = Play(false, "2", "6", "n");

        Assert.Equal(0, exitCode);
        Assert.Contains("<warning>The saved game is damaged, so a new game begins.\n</warning>", output);
    }

    [Fact]
    public void AcceptsASaveWithWindowsLineEndings()
    {
        File.WriteAllText(PathOf("game.save"), "pyrop adventure save 1\r\nplace tower\r\nmoves 7\r\ncarrying key\r\n");

        var (output, _, exitCode) = Play(false, "2", "5", "n");

        Assert.Equal(0, exitCode);
        Assert.Contains("Your saved game is restored after 7 moves.\n\n<emphasis>Tower\n</emphasis>", output);
        Assert.Contains("You see the crown.\n", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WinsAtOnceWhenTheLoadedSaveIsAlreadyWon(bool built)
    {
        File.WriteAllText(PathOf("game.save"), "pyrop adventure save 1\nplace garden\nmoves 1000000\ncarrying crown\n");

        var (output, errors, exitCode) = Play(built, "2");

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.EndsWith(
            "Choose a number from 1 to 3: Your saved game is restored after 1000000 moves.\n\n" +
            "<emphasis>Garden\n</emphasis>Sunlight falls on an overgrown garden. The gate to the world stands open.\n" +
            "<success>You carry the crown out into the sunlight. You won in 1000000 moves.\n</success>",
            output);
        Assert.DoesNotContain("What do you do?", output);
    }

    [Fact]
    public void ReportsASaveThatCannotBeWritten()
    {
        var unwritable = Path.Combine(_directory, "missing", "game.save");

        var (output, errors, exitCode) = RunSource(
            File.ReadAllText(Adventure), Adventure, null, null, [unwritable], Answers(["1", "5", "6", "n"]));

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.Contains("Choose a number from 1 to 6: <warning>The game could not be saved.\n</warning>", output);
        Assert.False(File.Exists(unwritable));
    }

    [Fact]
    public void LeavesAtTheFirstMenu()
    {
        var (output, _, exitCode) = Play(false, "3");

        Assert.Equal(0, exitCode);
        Assert.EndsWith("3. Leave\nChoose a number from 1 to 3: ", output);
    }

    [Fact]
    public void BundlesEveryModuleOfTheAdventure()
    {
        var bundle = BuildAdventure();

        Assert.Equal(
            ["adventure.prp", "items.prp", "saving.prp", "state.prp", "world.prp"],
            bundle.Files.Select(file => file.Name).Order(StringComparer.Ordinal));
        Assert.Equal("adventure.prp", bundle.EntryName);
    }

    private (string Output, string Errors, int ExitCode) Play(bool built, params string[] answers)
    {
        if (!built)
        {
            return RunSource(File.ReadAllText(Adventure), Adventure, null, null, [PathOf("game.save")], Answers(answers));
        }

        var bundle = BuildAdventure();
        return RunSource(
            bundle.Entry.Text,
            bundle.EntryName,
            BundleSourceStore.FullPathOf(bundle.EntryName),
            new BundleSourceStore(bundle),
            [PathOf("game.save")],
            Answers(answers));
    }

    private Bundle BuildAdventure()
    {
        var baseExecutable = PathOf("pyrop-base");
        File.WriteAllBytes(baseExecutable, "base"u8.ToArray());
        var executable = PathOf(OperatingSystem.IsWindows() ? "adventure.exe" : "adventure");
        Assert.Equal(0, BuildCommand.Build(Adventure, executable, baseExecutable));
        return Bundle.ReadFrom(executable)!;
    }

    private static string Answers(string[] answers) => string.Concat(answers.Select(answer => answer + "\n"));

    private static void AssertInOrder(string text, params string[] parts)
    {
        var position = 0;
        foreach (var part in parts)
        {
            var found = text.IndexOf(part, position, StringComparison.Ordinal);
            Assert.True(found >= 0, $"Expected, after position {position}:\n{part}\nin:\n{text}");
            position = found + part.Length;
        }
    }

    private static readonly string[] FullPlaythrough = ["1", "1", "3", "2", "3", "2", "1", "1", "1", "2", "2", "1", "2", "2"];

    private static readonly string Transcript = """
        <emphasis>The Crown of Pyrop
        </emphasis>Find the lost crown and carry it out into the garden.
        How do you begin?
        1. Start a new game
        2. Load the saved game
        3. Leave
        Choose a number from 1 to 3: 
        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: You go north.

        <emphasis>Library
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        You see the lamp.
        What do you do?
        1. Go east
        2. Go south
        3. Take the lamp
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: You take the lamp.

        <emphasis>Library
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: You go south.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: You go south.

        <emphasis>Cellar
        </emphasis>Barrels and broken crates fill the damp cellar.
        You see the key.
        What do you do?
        1. Go north
        2. Take the key
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: You take the key.

        <emphasis>Cellar
        </emphasis>Barrels and broken crates fill the damp cellar.
        What do you do?
        1. Go north
        2. Show the inventory
        3. Save the game
        4. Quit
        Choose a number from 1 to 4: You go north.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: You go north.

        <emphasis>Library
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: You unlock the door with the key.
        You go east.

        <emphasis>Tower
        </emphasis>Wind howls through the arrow slits of the round tower room.
        You see the crown.
        What do you do?
        1. Go west
        2. Take the crown
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: You take the crown.

        <emphasis>Tower
        </emphasis>Wind howls through the arrow slits of the round tower room.
        What do you do?
        1. Go west
        2. Show the inventory
        3. Save the game
        4. Quit
        Choose a number from 1 to 4: 
        Item   Description
        lamp   An oil lamp, still half full.
        key    A heavy iron key with a crown on its bow.
        crown  The lost crown of Pyrop.

        <emphasis>Tower
        </emphasis>Wind howls through the arrow slits of the round tower room.
        What do you do?
        1. Go west
        2. Show the inventory
        3. Save the game
        4. Quit
        Choose a number from 1 to 4: You go west.

        <emphasis>Library
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: You go south.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: You go east.

        <emphasis>Garden
        </emphasis>Sunlight falls on an overgrown garden. The gate to the world stands open.
        <success>You carry the crown out into the sunlight. You won in 12 moves.
        </success>
        """;

    private static string FindSamples()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var samples = Path.Combine(directory.FullName, "samples");
            if (File.Exists(Path.Combine(samples, "logcheck", "logcheck.prp")))
            {
                return samples;
            }
        }

        throw new InvalidOperationException("The samples directory was not found above the test output.");
    }
}
