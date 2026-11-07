using Pyrop.Bundling;
using Pyrop.Cli;
using Pyrop.LanguageServer;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class LibraryTests : IDisposable
{
    private static readonly byte[] BaseBytes = "not really an executable"u8.ToArray();

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-library-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public LibraryTests()
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

    [Fact]
    public void ALibraryOperationIsCalledQualified()
    {
        Assert.Equal("3\n1024\n", Run.Program("use math library\nwrite_line from math round 2.5\nwrite_line from math power 2 10"));
    }

    [Fact]
    public void AnImportedLibraryOperationIsCalledByItsName()
    {
        Assert.Equal("5\n2\n", Run.Program("use math library\nimport from math abs round_down end\nwrite_line abs -5\nwrite_line round_down 2.7"));
    }

    [Fact]
    public void ALibraryCanBeUsedBelowItsCalls()
    {
        Assert.Equal("5\n", Run.Program("write_line from math abs -5\nuse math library"));
    }

    [Fact]
    public void AVoidLibraryOperationIsAStatement()
    {
        Assert.Equal(
            Run.Program("use random library\nfrom random seed 7\nwrite_line from random roll 1 100"),
            Run.Program("use random library\nimport from random seed roll end\nseed 7\nwrite_line roll 1 100"));
    }

    [Fact]
    public void AVoidLibraryOperationIsNotAValue()
    {
        var error = Run.Failure("use random library\nwrite_line from random seed 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains("Operation 'seed' returns void and cannot be used as a value.", error.Message);
    }

    [Fact]
    public void ArgumentsAreAListOfStrings()
    {
        var host = new HostContext(["a", "b"], _ => null);

        Assert.Equal(
            "2\nb\n",
            Run.Program("use program library\ndef list string given from program arguments\nwrite_line length_of given\nwrite_line get given 1", host: host));
    }

    [Fact]
    public void AnUnknownLibraryListsTheLibraries()
    {
        var error = Run.Failure("use termnal library");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("There is no built-in library 'termnal'; the libraries are: files, math, program, random, terminal.", error.Message);
        Assert.Equal(5, error.Position.Column);
    }

    [Fact]
    public void AHostCanOfferFewerLibraries()
    {
        var host = HostContext.Isolated with { Libraries = new BuiltinLibraries([LibraryOperations.Math]) };

        Assert.Equal("3\n", Run.Program("use math library\nwrite_line from math round 2.5", host: host));
        Assert.Equal(
            "There is no built-in library 'files'; the libraries are: math.",
            Run.Failure("use files library", host: host).Message);
        Assert.Equal(
            "There is no built-in library 'math'; this host offers none.",
            Run.Failure("use math library", host: HostContext.Isolated with { Libraries = BuiltinLibraries.None }).Message);
    }

    [Fact]
    public void UseTakesAPathOrTheWordLibrary()
    {
        var error = Run.Failure("use math books");

        Assert.Contains("a string literal path or the word library", error.Message);
    }

    [Fact]
    public void LibraryIsAReservedWord()
    {
        Assert.Contains("reserved word", Run.Failure("def int library 1").Message);
        Assert.Contains("reserved word", Run.Failure("use library library").Message);
    }

    [Fact]
    public void ALibraryAndAFileCannotShareAModuleName()
    {
        WriteFile("math.prp", "export function int twice int x return multiply x 2");

        var error = FailFile("main.prp", "use math \"math.prp\"\nuse math library\nwrite_line from math twice 2");

        Assert.Equal("Module name 'math' is already used in this file.", error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void AFileMayUseItsOwnFileUnderALibrarysName()
    {
        WriteFile("math.prp", "export function int twice int x return multiply x 2");

        Assert.Equal("4\n", RunFile("main.prp", "use math \"math.prp\"\nwrite_line from math twice 2"));
    }

    [Fact]
    public void AUsedModuleCanUseALibrary()
    {
        WriteFile("dice.prp", "use random library\nimport from random roll seed end\nexport function int d6 return roll 1 6\nseed 3");

        var output = RunFile("main.prp", "use dice \"dice.prp\"\ndef int first from dice d6\nwrite_line and at_least first 1 at_most first 6");

        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ALibraryIsNotPassedOnByAModuleThatUsesIt()
    {
        WriteFile("dice.prp", "use random library\nexport function int d6 return from random roll 1 6");

        var error = FailFile("main.prp", "use dice \"dice.prp\"\nwrite_line from random roll 1 6");

        Assert.Equal("'random' is not a module of this file; load the library with use random library.", error.Message);
    }

    [Fact]
    public void ANameALibraryDoesNotHaveIsReported()
    {
        Assert.Equal(
            "The library math has no 'floor'; it offers abs, sign, power, square_root, round, round_down.",
            Run.Failure("use math library\nwrite_line from math floor 2.5").Message);
        Assert.Equal(
            "The library random has no 'roll_dice'; it is now called 'roll'.",
            Run.Failure("use random library\nwrite_line from random roll_dice 1 6").Message);
        Assert.Contains(
            "The library math has no 'floor'",
            Run.Failure("use math library\nimport from math floor end").Message);
    }

    [Fact]
    public void ALibraryOperationIsNotAType()
    {
        Assert.Equal(
            "'round' of the library math is an operation, not a type.",
            Run.Failure("use math library\ndef from math round x 1").Message);
    }

    [Fact]
    public void ImportingAnOperationTakesItsNameInTheFile()
    {
        Assert.Contains(
            "'round' is an imported operation and cannot be used as a variable name.",
            Run.Failure("use math library\nimport from math round end\ndef int round 1").Message);
        Assert.Contains(
            "'round' is a function of this file and cannot also be imported.",
            Run.Failure("use math library\nimport from math round end\nfunction int round return 1").Message);
    }

    [Fact]
    public void ImportingAnOperationTwiceIsReported()
    {
        Assert.Contains(
            "'round' is named twice",
            Run.Failure("use math library\nimport from math round round end").Message);
    }

    [Fact]
    public void AttemptTakesAnImportedLibraryOperation()
    {
        var missing = Path.Combine(_directory, "missing.txt").Replace("\\", "\\\\");

        Assert.Equal(
            "none\n",
            Run.Program($"use files library\nimport from files load end\nwrite_line attempt load \"{missing}\""));
    }

    [Fact]
    public void AttemptTakesAQualifiedLibraryOperation()
    {
        var saved = Path.Combine(_directory, "saved.txt");
        var quoted = saved.Replace("\\", "\\\\");

        Assert.Equal(
            "true\nsome \"hi\"\nnone\n",
            Run.Program(
                $"use files library\nwrite_line attempt from files save \"{quoted}\" \"hi\"\n" +
                $"write_line attempt from files load \"{quoted}\"\n" +
                "write_line attempt from random between 2.0 1.0\nuse random library"));
    }

    [Fact]
    public void AttemptRejectsAQualifiedFunction()
    {
        WriteFile("tools.prp", "export function int one return 1");

        var error = FailFile("main.prp", "use tools \"tools.prp\"\ndef option int x attempt from tools one");

        Assert.Contains("found the function 'one' of module 'tools'; a function's own failures are runtime errors", error.Message);
    }

    [Fact]
    public void AttemptOnAnUnusedLibraryNamesTheUse()
    {
        Assert.Equal(
            "'program' is not a module of this file; load the library with use program library.",
            Run.Failure("def option string home attempt from program environment \"HOME\"").Message);
    }

    [Fact]
    public void AttemptOnAnUnimportedLibraryOperationGivesTheHint()
    {
        Assert.EndsWith(
            "found 'load'. 'load' comes from the library files: add use files library and import from files load end, or write from files load.",
            Run.Failure("def option string text attempt load \"notes.txt\"").Message);
    }

    [Fact]
    public void AnOldNameAtTheStartOfAStatementSaysWhereItWent()
    {
        var error = Run.Failure("seed_random 42");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(
            "'seed_random' is now 'seed' in the library random: add use random library and import from random seed end, or write from random seed.",
            error.Message);
    }

    [Fact]
    public void AnOldNameUsedAsAValueSaysWhereItWent()
    {
        var error = Run.Failure("write_line argument_count");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal(
            "'argument_count' is now 'arguments' in the library program: add use program library and import from program arguments end, or write from program arguments.",
            error.Message);
    }

    [Fact]
    public void AnOldNameGivenArgumentsGetsTheHintAfterTheStrayValue()
    {
        var error = Run.Failure("write_line roll_dice 1 6");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.EndsWith(
            "If 'roll_dice' was meant as an operation: 'roll_dice' is now 'roll' in the library random: add use random library and import from random roll end, or write from random roll.",
            error.Message);
    }

    [Fact]
    public void AnUnimportedLibraryNameSaysWhereItComesFrom()
    {
        Assert.Equal(
            "'round' comes from the library math: add use math library and import from math round end, or write from math round.",
            Run.Failure("def float x 2.5\nwrite_line round").Message);
    }

    [Fact]
    public void NoHintPointsAtALibraryWhoseNameAFileModuleTakes()
    {
        WriteFile("math.prp", "export function int twice int x return multiply x 2");

        Assert.Equal("Undeclared variable 'round'.", FailFile("main.prp", "use math \"math.prp\"\nwrite_line round").Message);
    }

    [Fact]
    public void AttemptOnAVariableNamedLikeALibraryOperationKeepsTheGeneralMessage()
    {
        var error = Run.Failure("def string load \"x\"\ndef option string y attempt load");

        Assert.StartsWith("'attempt' takes a built-in operation or a convert", error.Message);
        Assert.EndsWith("'load' comes from the library files: add use files library and import from files load end, or write from files load.", error.Message);
    }

    [Fact]
    public void TheHintLeavesOutTheUseWhenTheFileHasIt()
    {
        Assert.Equal(
            "'round' comes from the library math: add import from math round end, or write from math round.",
            Run.Failure("use math library\nwrite_line round").Message);
    }

    [Fact]
    public void OldAndLibraryNamesAreOrdinaryNames()
    {
        Assert.Equal(
            "3\n4\n5\n",
            Run.Program("def int round 3\ndef int roll_dice 4\nfunction int seed return 5\nwrite_line round\nwrite_line roll_dice\nwrite_line seed"));
    }

    [Fact]
    public void AFileThatDoesNotImportANameKeepsItWhileUsingTheLibrary()
    {
        Assert.Equal("7\n3\n", Run.Program("use math library\ndef int round 7\nwrite_line round\nwrite_line from math round 2.5"));
    }

    [Fact]
    public void TheReplRepeatsAUseWithoutComplaint()
    {
        var session = new PyropSession(new StringReader(""), _output);

        session.Run("use math library\nimport from math abs end");
        session.Run("use math library");
        session.Run("write_line abs -2");
        session.Run("write_line from math power 3 2");

        Assert.Equal("2\n9\n", _output.ToString());
    }

    [Fact]
    public void AFailedReplInputLoadsNoLibrary()
    {
        var session = new PyropSession(new StringReader(""), _output);

        Assert.Throws<PyropException>(() => session.Run("use math library\nwrite_line undefined_name"));

        Assert.Contains("is not a module of this file", Assert.Throws<PyropException>(() => session.Run("write_line from math abs -1")).Message);
    }

    [Fact]
    public void TheTreeShowsLibraryCalls()
    {
        var inspected = _session.Inspect(
            "use math library\nimport from math abs end\nwrite_line add abs -1 from math round 2.5",
            Path.Combine(_directory, "tree.prp"));

        Assert.Null(inspected.Error);
        Assert.Equal(
            "use math library\nimport from math abs end\nwrite_line (add (abs -1) (from math round 2.5))\n",
            inspected.Text);
    }

    [Fact]
    public void ABuiltProgramBundlesNoFileForALibrary()
    {
        var entry = WriteFile("dice.prp", "use random library\nuse program library\nwrite_line get from program arguments 0\nwrite_line from random roll 2 2");
        var basePath = Path.Combine(_directory, "pyrop-base");
        File.WriteAllBytes(basePath, BaseBytes);
        var builtPath = Path.Combine(_directory, OperatingSystem.IsWindows() ? "dice-built.exe" : "dice-built");

        Assert.Equal(0, BuildCommand.Build(entry, Path.Combine(_directory, "dice-built"), basePath));

        var bundle = Bundle.ReadFrom(builtPath)!;
        Assert.Equal(["dice.prp"], bundle.Files.Select(file => file.Name));
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output, new HostContext(["hi"], _ => null), new BundleSourceStore(bundle));
        session.Run(bundle.Entry.Text, bundle.EntryName, BundleSourceStore.FullPathOf(bundle.EntryName));
        Assert.Equal("hi\n2\n", output.ToString());
    }

    [Fact]
    public void SemanticTokensMarkLibraryOperationsAsBuiltIn()
    {
        const string source = "use math library\nimport from math abs power end\nwrite_line abs from math round 2.5\nwrite_line length_of \"x\"";
        var analyzed = _session.Analyze(source, Path.Combine(_directory, "tokens.prp"))!;

        var tokens = SemanticClassifier.Classify(analyzed);

        (SemanticKind, SemanticModifiers) At(int line, int start) =>
            tokens.Where(token => token.Line == line && token.Start == start).Select(token => (token.Kind, token.Modifiers)).Single();

        Assert.Equal((SemanticKind.Module, SemanticModifiers.None), At(0, 4));
        Assert.Equal((SemanticKind.Function, SemanticModifiers.BuiltIn | SemanticModifiers.Imported), At(1, 17));
        Assert.Equal((SemanticKind.Function, SemanticModifiers.BuiltIn | SemanticModifiers.Imported), At(1, 21));
        Assert.Equal((SemanticKind.Function, SemanticModifiers.BuiltIn | SemanticModifiers.Imported), At(2, 11));
        Assert.Equal((SemanticKind.Module, SemanticModifiers.None), At(2, 20));
        Assert.Equal((SemanticKind.Function, SemanticModifiers.BuiltIn), At(2, 25));
        Assert.Equal((SemanticKind.Function, SemanticModifiers.BuiltIn), At(3, 11));
    }
}
