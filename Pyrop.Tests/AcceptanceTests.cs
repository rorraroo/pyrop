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
        string source, string path, string? fullPath, Parsing.ISourceStore? store, string[] arguments, string input = "", ICommandRunner? commands = null)
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(input), output, new HostContext(arguments, _ => null) { Commands = commands ?? NoCommandRunner.Instance }, store);
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
            "Choose a number from 1 to 5: \nSave before you leave? [y/n] \nGoodbye.\n");
        Assert.EndsWith("Save before you leave? [y/n] \nGoodbye.\n", first);
        Assert.Equal(
            "pyrop adventure save 2\nPlayer place study moves 5 inventory [lamp]\n",
            File.ReadAllText(PathOf("game.save")));

        var (second, secondErrors, secondExit) = Play(built, "2", "2", "3", "2", "1", "1", "1", "2", "1", "2", "2");

        Assert.Equal(0, secondExit);
        Assert.Equal("", secondErrors);
        AssertInOrder(
            second,
            "Choose a number from 1 to 3: \nYour saved game is restored after 5 moves.\n\n<emphasis>Study\n</emphasis>",
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
        Assert.EndsWith("Save before you leave? [y/n] \n<success>The game is saved.\n</success>Goodbye.\n", output);
        Assert.Equal("pyrop adventure save 2\nPlayer place study moves 1 inventory []\n", File.ReadAllText(PathOf("game.save")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartsANewGameWhenThereIsNoSave(bool built)
    {
        var (output, _, exitCode) = Play(built, "2", "6", "n");

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Choose a number from 1 to 3: \n<warning>There is no saved game yet, so a new game begins.\n</warning>\n<emphasis>Great Hall\n</emphasis>",
            output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a save\n")]
    [InlineData("pyrop adventure save 1\nPlayer place hall moves 1 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place attic moves 1 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place Hall moves 1 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves -1 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves many inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 1000001 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 9223372036854775807 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 99999999999999999999 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2 inventory [sword]\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2 inventory [lamp, lamp]\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2\n")]
    [InlineData("pyrop adventure save 2\nPlayer moves 3 place hall inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place  hall moves 2 inventory []\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2 inventory []\n\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2 inventory [] health 10\n")]
    [InlineData("pyrop adventure save 2\nPlayer place hall moves 2 inventory []\nPlayer place hall moves 2 inventory []\n")]
    [InlineData("pyrop adventure save 2\n")]
    public void StartsANewGameWhenTheSaveIsDamaged(string save)
    {
        File.WriteAllText(PathOf("game.save"), save);

        foreach (var built in new[] { false, true })
        {
            var (output, errors, exitCode) = Play(built, "2", "6", "n");

            Assert.Equal(0, exitCode);
            Assert.Equal("", errors);
            Assert.Contains(
                "Choose a number from 1 to 3: \n<warning>The saved game is damaged, so a new game begins.\n</warning>\n<emphasis>Great Hall\n</emphasis>",
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
        File.WriteAllText(PathOf("game.save"), "pyrop adventure save 2\r\nPlayer place tower moves 7 inventory [key]\r\n");

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
        File.WriteAllText(PathOf("game.save"), "pyrop adventure save 2\nPlayer place garden moves 1000000 inventory [crown]\n");

        var (output, errors, exitCode) = Play(built, "2");

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.EndsWith(
            "Choose a number from 1 to 3: \nYour saved game is restored after 1000000 moves.\n\n" +
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
        Assert.Contains("Choose a number from 1 to 6: \n<warning>The game could not be saved.\n</warning>", output);
        Assert.False(File.Exists(unwritable));
    }

    [Fact]
    public void LeavesAtTheFirstMenu()
    {
        var (output, _, exitCode) = Play(false, "3");

        Assert.Equal(0, exitCode);
        Assert.EndsWith("3. Leave\nChoose a number from 1 to 3: \n", output);
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
        Choose a number from 1 to 6: 
        You go north.

        <emphasis>Study
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        You see the lamp.
        What do you do?
        1. Go east
        2. Go south
        3. Take the lamp
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: 
        You take the lamp.

        <emphasis>Study
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: 
        You go south.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: 
        You go south.

        <emphasis>Cellar
        </emphasis>Barrels and broken crates fill the damp cellar.
        You see the key.
        What do you do?
        1. Go north
        2. Take the key
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: 
        You take the key.

        <emphasis>Cellar
        </emphasis>Barrels and broken crates fill the damp cellar.
        What do you do?
        1. Go north
        2. Show the inventory
        3. Save the game
        4. Quit
        Choose a number from 1 to 4: 
        You go north.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: 
        You go north.

        <emphasis>Study
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: 
        You unlock the door with the key.
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
        Choose a number from 1 to 5: 
        You take the crown.

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
        Choose a number from 1 to 4: 
        You go west.

        <emphasis>Study
        </emphasis>Shelves of rotting books lean against each other. A narrow door leads east.
        What do you do?
        1. Go east
        2. Go south
        3. Show the inventory
        4. Save the game
        5. Quit
        Choose a number from 1 to 5: 
        You go south.

        <emphasis>Great Hall
        </emphasis>Dust lies thick on a long table. Doors lead north, south and east.
        What do you do?
        1. Go north
        2. Go east
        3. Go south
        4. Show the inventory
        5. Save the game
        6. Quit
        Choose a number from 1 to 6: 
        You go east.

        <emphasis>Garden
        </emphasis>Sunlight falls on an overgrown garden. The gate to the world stands open.
        <success>You carry the crown out into the sunlight. You won in 12 moves.
        </success>
        """;

    private static string Rogue => Path.Combine(Samples, "rogue", "rogue.prp");

    private static string Logwatch => Path.Combine(Samples, "logwatch", "logwatch.prp");

    private static string ServerLog => Path.Combine(Samples, "logwatch", "server.log");

    private string ProgressFile => PathOf("rogue.progress");

    private const string ToDepthThree =
        "right right right right right right right right right right right right right right right right " +
        "down down down down down down down down right right right right right right right up up up " +
        "down down right right right right right right right right down down right right " +
        "up right right right right up " +
        "right right right right right right right right right right right right right right down down";

    private const string Escape = ToDepthThree + " i enter escape down down down down down down down";

    private const string Death = ToDepthThree + " down down down down down down";

    private const string FirstScreen = """
        The crypt
        ########################
        #@.......r..$..........#
        #.#...#................#
        #......................#
        #...#...#.....#.......>#
        #.......!r.............#
        #.#.#.#.#.....#........#
        #.................$....#
        ########################
        Hero
        Health: 10 of 10
        Depth 1   Gold 0   Attack 2
        Log
        You enter the crypt. Find the stairs down.
        <muted>arrows move   space wait   i inventory   q quit
        </muted>

        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaysTheRogueliteToAnEscape(bool built)
    {
        var (output, errors, exitCode) = PlayRogue(built, ["7", ProgressFile], Keys(Escape));

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.StartsWith(FirstScreen, output);
        AssertInOrder(
            output,
            "You hit the rat for 1.\nThe rat hits you for 1.\nYou slay the rat.\n",
            "You pick up 2 gold.\n",
            "You climb down to depth 2.\n",
            "Depth 3   Gold 22   Attack 2\n",
            "Inventory\nhealing draught (selected)\nItem\nRestores 8 health. Tastes of copper.\n<muted>up/down choose   enter use   escape back\n</muted>\n",
            "Inventory\nItem\nYour pack is empty.\n",
            "You drink the draught and feel better.\n",
            "Health: 2 of 10\nDepth 3   Gold 22   Attack 2\n<danger>You are badly hurt.\n</danger>",
            "You slay the orc.\n");
        Assert.EndsWith(
            "\nThe run is over\nYou slay the orc.\nYou find the crypt's back door and escape!\nYou escaped from the crypt!\n" +
            "Gold this run 22   Gold banked 22\nRuns 1   Escapes 1   Deepest 3\n",
            output);
        Assert.Equal(78, CountOf(output, "q quit\n</muted>\n"));
        Assert.Equal(71, CountOf(output, "\nThe crypt\n") + 1);
        Assert.Equal("Progress runs 1 escapes 1 best_depth 3 gold_banked 22\n", File.ReadAllText(ProgressFile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiesInTheRogueliteAndStillBanksTheGold(bool built)
    {
        File.WriteAllText(ProgressFile, "Progress runs 2 escapes 1 best_depth 3 gold_banked 20\n");

        var (output, errors, exitCode) = PlayRogue(built, ["7", ProgressFile], Keys(Death));

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.EndsWith(
            "\nThe run is over\nYou hit the orc for 1.\nThe orc kills you.\nYou died on depth 3.\n" +
            "Gold this run 22   Gold banked 42\nRuns 3   Escapes 1   Deepest 3\n",
            output);
        Assert.Equal("Progress runs 3 escapes 1 best_depth 3 gold_banked 42\n", File.ReadAllText(ProgressFile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpendsBankedGoldOnExtraHealthInTheNextRun(bool built)
    {
        File.WriteAllText(ProgressFile, "Progress runs 4 escapes 1 best_depth 2 gold_banked 60\n");

        var (output, errors, exitCode) = PlayRogue(built, ["7", ProgressFile], "q\n");

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.Contains("Hero\nHealth: 12 of 12\n", output);
        Assert.Contains("Log\nYou enter the crypt. Find the stairs down.\nYour banked gold buys you 2 extra health.\n", output);
        Assert.EndsWith("Gold this run 0   Gold banked 60\nRuns 5   Escapes 1   Deepest 2\n", output);
        Assert.Equal("Progress runs 5 escapes 1 best_depth 2 gold_banked 60\n", File.ReadAllText(ProgressFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a progress file\n")]
    [InlineData("Progress runs 1 escapes 0 best_depth 1\n")]
    [InlineData("Progress escapes 0 runs 1 best_depth 1 gold_banked 0\n")]
    [InlineData("Progress runs -1 escapes 0 best_depth 1 gold_banked 0\n")]
    [InlineData("Progress runs 1 escapes 2 best_depth 1 gold_banked 0\n")]
    [InlineData("Progress runs 1 escapes 0 best_depth 9 gold_banked 0\n")]
    [InlineData("Progress runs 1 escapes 0 best_depth 1 gold_banked 9223372036854775807\n")]
    [InlineData("Progress runs 9223372036854775807 escapes 0 best_depth 1 gold_banked 0\n")]
    [InlineData("Progress runs 1 escapes 0 best_depth 1 gold_banked 99999999999999999999\n")]
    [InlineData("Progress runs 1 escapes 0 best_depth 1 gold_banked 0\nProgress runs 1 escapes 0 best_depth 1 gold_banked 0\n")]
    public void StartsTheRogueliteFreshWhenTheProgressIsDamaged(string progress)
    {
        File.WriteAllText(ProgressFile, progress);

        foreach (var built in new[] { false, true })
        {
            var (output, errors, exitCode) = PlayRogue(built, ["7", ProgressFile], "q\n");

            Assert.Equal(0, exitCode);
            Assert.Equal("", errors);
            Assert.Equal(FirstScreen + FreshEnding, output);
            Assert.Equal("Progress runs 1 escapes 0 best_depth 1 gold_banked 0\n", File.ReadAllText(ProgressFile));
            File.WriteAllText(ProgressFile, progress);
        }
    }

    [Fact]
    public void StartsTheRogueliteFreshWithoutAProgressFileOrWhenItIsNotText()
    {
        Assert.Equal((FirstScreen + FreshEnding, "", 0), PlayRogue(false, ["7", ProgressFile], "q\n"));

        File.WriteAllBytes(ProgressFile, [0x50, 0xFF, 0xFE, 0x0A]);
        Assert.Equal((FirstScreen + FreshEnding, "", 0), PlayRogue(false, ["7", ProgressFile], "q\n"));
        Assert.Equal("Progress runs 1 escapes 0 best_depth 1 gold_banked 0\n", File.ReadAllText(ProgressFile));
    }

    [Fact]
    public void ARunDependsOnlyOnTheSeedAndTheKeys()
    {
        var first = PlayRogue(false, ["7", PathOf("first.progress")], Keys(Death));
        var second = PlayRogue(true, ["7", PathOf("second.progress")], Keys(Death));
        var other = PlayRogue(false, ["8", PathOf("other.progress")], "q\n");

        Assert.Equal(first, second);
        Assert.False(other.Output.StartsWith(FirstScreen, StringComparison.Ordinal));
        Assert.StartsWith("The crypt\n########################\n#@", other.Output);
    }

    [Fact]
    public void ExplainsTheRoguelitesUsageForABadSeed()
    {
        Assert.Equal(("", "usage: rogue [seed] [progress file]\n", 2), PlayRogue(false, ["seven", ProgressFile], ""));
        Assert.False(File.Exists(ProgressFile));
    }

    [Fact]
    public void ReportsProgressThatCannotBeSaved()
    {
        var unwritable = Path.Combine(_directory, "missing", "rogue.progress");

        var (output, errors, exitCode) = PlayRogue(false, ["7", unwritable], "q\n");

        Assert.Equal(0, exitCode);
        Assert.Equal(FirstScreen + FreshEnding, output);
        Assert.Equal($"Could not save the progress to {unwritable}\n", errors);
    }

    [Fact]
    public void BundlesEveryModuleOfTheRoguelite()
    {
        var bundle = BuildSample(Rogue, "rogue");

        Assert.Equal(
            ["dungeon.prp", "progress.prp", "rogue.prp", "rules.prp"],
            bundle.Files.Select(file => file.Name).Order(StringComparer.Ordinal));
        Assert.Equal("rogue.prp", bundle.EntryName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WatchesALogUntilQIsPressed(bool built)
    {
        var (output, errors, exitCode) = PlaySample(built, Logwatch, "logwatch", [ServerLog], "\n\ndown\nq\n");

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        Assert.Equal(
            Screens(
                Dashboard(ServerLog, "watching", 11, 6, 2, 1, 0),
                Dashboard(ServerLog, "watching", 11, 6, 2, 2, 0),
                Dashboard(ServerLog, "watching", 11, 6, 2, 3, 0),
                Dashboard(ServerLog, "watching", 11, 6, 2, 4, 1)),
            output);
    }

    [Fact]
    public void SeesLinesAddedToTheLogBetweenRefreshes()
    {
        var log = PathOf("live.log");
        File.Copy(ServerLog, log);
        var keys = new KeysThatWriteToALog(["", "end", "q"], () =>
            File.AppendAllText(log, "ERROR 09:20:00 Database connection lost\nINFO  09:20:05 Reconnected\n"));
        var output = new StringOutput();
        var session = new PyropSession(keys, output, new HostContext([log], _ => null));

        var exitCode = session.Run(File.ReadAllText(Logwatch), Logwatch) ?? 0;

        Assert.Equal(0, exitCode);
        Assert.Equal(
            Screens(
                Dashboard(log, "watching", 11, 6, 2, 1, 0),
                Dashboard(log, "watching", 13, 7, 2, 2, 0, "ERROR 09:20:00 Database connection lost"),
                Dashboard(log, "watching", 13, 7, 2, 3, 3, "ERROR 09:20:00 Database connection lost")),
            output.ToString());
    }

    [Fact]
    public void WaitsForALogThatDoesNotExistYet()
    {
        var log = PathOf("later.log");

        var (output, errors, exitCode) = PlaySample(false, Logwatch, "logwatch", [log], "down\nq\n");

        Assert.Equal(0, exitCode);
        Assert.Equal("", errors);
        string[] Empty(int refreshes) =>
        [
            "Levels\nInfo: 0 of 0\n<warning>Warnings: 0 of 0\n</warning><danger>Errors: 0 of 0\n</danger>",
            $"File\nField      Value\nName       {Path.GetFileName(log)}\nState      missing\nLines      0\nRefreshes  {refreshes}\n",
            "Errors\n",
            "<muted>up/down select   q quit\n</muted>",
        ];
        Assert.Equal(Screens(Empty(1), Empty(2)), output);
    }

    [Fact]
    public void ExplainsTheLogWatchersUsage()
    {
        Assert.Equal(("", "usage: logwatch <log file>\n", 2), PlaySample(false, Logwatch, "logwatch", [], ""));
    }

    [Fact]
    public void BundlesBothFilesOfTheLogWatcher()
    {
        Assert.Equal(["logwatch.prp", "tally.prp"], BuildSample(Logwatch, "logwatch").Files.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    private const string FreshEnding = """
        The run is over
        You enter the crypt. Find the stairs down.
        You leave the crypt with what you found.
        Gold this run 0   Gold banked 0
        Runs 1   Escapes 0   Deepest 1

        """;

    private static string Screens(params string[][] screens)
    {
        var written = "";
        string[]? previous = null;
        foreach (var screen in screens)
        {
            for (var index = 0; index < screen.Length; index++)
            {
                if (previous is null || index == screen.Length - 1 || screen[index] != previous[index])
                {
                    written += screen[index];
                }
            }

            written += "\n";
            previous = screen;
        }

        return written;
    }

    private static string[] Dashboard(string path, string state, int lines, int infos, int warnings, int refreshes, int selected, params string[] moreErrors)
    {
        string[] errors =
        [
            "ERROR 09:14:02 Disk quota exceeded on /var",
            "ERROR 09:15:40 Upstream timed out after 30 s",
            "ERROR 09:17:11 Worker 3 crashed",
            .. moreErrors,
        ];
        var menu = string.Concat(errors.Select((error, index) => index == selected ? $"{error} (selected)\n" : $"{error}\n"));
        return
        [
            $"Levels\nInfo: {infos} of {lines}\n<warning>Warnings: {warnings} of {lines}\n</warning><danger>Errors: {errors.Length} of {lines}\n</danger>",
            $"File\nField      Value\nName       {Path.GetFileName(path)}\nState      {state}\nLines      {lines}\nRefreshes  {refreshes}\n",
            $"Errors\n{menu}",
            "<muted>up/down select   q quit\n</muted>",
        ];
    }

    private sealed class KeysThatWriteToALog(string[] keys, Action write) : TextReader
    {
        private int _read;

        public override string? ReadLine()
        {
            if (_read == 0)
            {
                write();
            }

            return _read < keys.Length ? keys[_read++] : null;
        }
    }

    private (string Output, string Errors, int ExitCode) PlayRogue(bool built, string[] arguments, string keys) =>
        PlaySample(built, Rogue, "rogue", arguments, keys);

    private static string GitSummary => Path.Combine(Samples, "gitsummary", "gitsummary.prp");

    private (string Output, string Errors, int ExitCode) RunGitSummary(bool built, string[] arguments, ICommandRunner commands)
    {
        if (!built)
        {
            return RunSource(File.ReadAllText(GitSummary), GitSummary, null, null, arguments, commands: commands);
        }

        var bundle = BuildSample(GitSummary, "gitsummary");
        return RunSource(bundle.Entry.Text, bundle.EntryName, BundleSourceStore.FullPathOf(bundle.EntryName), new BundleSourceStore(bundle), arguments, commands: commands);
    }

    private static void Commit(string repository, string author) =>
        ProcessCommandRunner.Instance.Capture("git", ["-C", repository, "-c", $"user.name={author}", "-c", "user.email=a@example.com", "commit", "--allow-empty", "-m", "change"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GitSummaryCountsCommitsByAuthor(bool built)
    {
        var repository = PathOf("repository");
        ProcessCommandRunner.Instance.Capture("git", ["init", repository]);
        Commit(repository, "Ann");
        Commit(repository, "Bob");
        Commit(repository, "Ann");

        var (output, errors, exitCode) = RunGitSummary(built, [repository], ProcessCommandRunner.Instance);

        Assert.Equal(("", 0), (errors, exitCode));
        Assert.Contains("Ann", output);
        Assert.Contains("Bob", output);
        Assert.EndsWith("Commits 3   Authors 2\n", output);

        foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void GitSummaryReportsAFolderThatIsNotARepository()
    {
        var (output, errors, exitCode) = RunGitSummary(false, [_directory], ProcessCommandRunner.Instance);

        Assert.Equal(("", 1), (output, exitCode));
        Assert.NotEqual("", errors);
    }

    [Fact]
    public void GitSummarySaysWhenGitCannotBeStarted()
    {
        Assert.Equal(("", "gitsummary needs git, which could not be started.\n", 2), RunGitSummary(false, [], NoCommandRunner.Instance));
    }

    private (string Output, string Errors, int ExitCode) PlaySample(bool built, string script, string name, string[] arguments, string keys)
    {
        if (!built)
        {
            return RunSource(File.ReadAllText(script), script, null, null, arguments, keys);
        }

        var bundle = BuildSample(script, name);
        return RunSource(
            bundle.Entry.Text,
            bundle.EntryName,
            BundleSourceStore.FullPathOf(bundle.EntryName),
            new BundleSourceStore(bundle),
            arguments,
            keys);
    }

    private Bundle BuildSample(string script, string name)
    {
        var baseExecutable = PathOf("pyrop-base");
        File.WriteAllBytes(baseExecutable, "base"u8.ToArray());
        var executable = PathOf(OperatingSystem.IsWindows() ? $"{name}.exe" : name);
        Assert.Equal(0, BuildCommand.Build(script, executable, baseExecutable));
        return Bundle.ReadFrom(executable)!;
    }

    private static string Keys(string keys) => string.Concat(keys.Split(' ').Select(key => key + "\n"));

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var found = text.IndexOf(part, StringComparison.Ordinal); found >= 0; found = text.IndexOf(part, found + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

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
