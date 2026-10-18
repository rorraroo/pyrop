namespace Pyrop.Tests;

public sealed class ChoiceModuleTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-choice-").FullName;
    private readonly StringOutput _output = new();
    private readonly PyropSession _session;

    public ChoiceModuleTests()
    {
        _session = new PyropSession(new StringReader(""), _output);
        WriteFile("items.prp", Items);
        WriteFile("tools.prp", "export choice Tool sword hammer end\n");
        WriteFile("relay.prp", "use items \"items.prp\"\nimport from items Item\nexport function Item first return sword\n");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Items =
        "export choice Item sword shield lamp end\n" +
        "choice Secret hidden end\n" +
        "export function Item best return sword\n" +
        "export function string describe Item i return convert string i\n";

    private string WriteFile(string relativePath, string text)
    {
        var path = Path.Combine(_directory, relativePath);
        File.WriteAllText(path, text);
        return path;
    }

    private string RunFile(string text)
    {
        _session.Run(text, WriteFile("main.prp", text));
        return _output.ToString();
    }

    private PyropError FailFile(string text)
    {
        var path = WriteFile("main.prp", text);
        return Assert.Single(Assert.Throws<PyropException>(() => _session.Run(text, path)).Errors);
    }

    [Fact]
    public void QualifiedChoicesAndVariantsWork()
    {
        const string source = """
            use items "items.prp"
            def from items Item a from items sword
            write_line a
            write_line equal a from items best
            write_line from items describe from items lamp
            def map from items Item int counts put empty map from items Item int from items shield 2
            write_line counts
            write_line convert from items Item " lamp "
            function from items Item pick from items Item x return x
            write_line pick from items shield
            """;

        Assert.Equal("sword\ntrue\nlamp\n{shield: 2}\nlamp\nshield\n", RunFile(source));
    }

    [Fact]
    public void ImportingAChoiceImportsItsVariants()
    {
        const string source = """
            def Item a lamp
            use items "items.prp"
            import from items Item
            write_line list_of Item sword shield lamp end
            write_line equal a from items lamp
            write_line equal from items best sword
            """;

        Assert.Equal("[sword, shield, lamp]\ntrue\ntrue\n", RunFile(source));
    }

    [Fact]
    public void ChoicesOfTwoModulesAreDifferentTypes()
    {
        var error = FailFile("choice Item sword end\nuse items \"items.prp\"\nwrite_line equal from items sword sword\n");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.StartsWith("'equal' cannot take Item (declared in ", error.Message);
    }

    [Fact]
    public void EveryFileIsNamedTheSameWayHoweverItsPathWasWritten()
    {
        const string source = "choice Item sword end\nuse items \"items.prp\"\nwrite_line equal from items sword sword\n";
        var main = WriteFile("main.prp", source);
        var written = main.Replace('\\', '/');

        var error = Assert.Single(Assert.Throws<PyropException>(() => _session.Run(source, written)).Errors);

        var items = Path.Combine(_directory, "items.prp");
        Assert.Equal(Path.GetFullPath(main), error.Position.Source);
        Assert.Equal(
            $"'equal' cannot take Item (declared in {items}) Item (declared in {Path.GetFullPath(main)}). " +
            "It accepts: T T (T stands for any type, the same wherever it appears).",
            error.Message);
    }

    [Fact]
    public void AVariantOfOneModuleMayShareItsNameWithAVariantOfAnother()
    {
        const string source = """
            choice Weapon sword end
            use tools "tools.prp"
            write_line list_of Weapon sword end
            write_line list_of from tools Tool from tools sword from tools hammer end
            """;

        Assert.Equal("[sword]\n[sword, hammer]\n", RunFile(source));
    }

    [Theory]
    [InlineData("use items \"items.prp\"\nwrite_line from items hidden", 2, 23, "Variant 'hidden' of module 'items' belongs to the choice 'Secret', which is not exported; mark its declaration in ")]
    [InlineData("use items \"items.prp\"\nimport from items sword", 2, 19, "'sword' is a variant of the choice 'Item'; import the choice with import from items Item, which imports all its variants.")]
    [InlineData("use items \"items.prp\"\nfunction int sword return 1\nimport from items Item", 3, 19, "Importing 'Item' would also import its variant 'sword', but 'sword' is a function of this file.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\nchoice Mine lamp end", 2, 19, "Importing 'Item' would also import its variant 'lamp', but 'lamp' is a variant of this file.")]
    [InlineData("use items \"items.prp\"\nuse tools \"tools.prp\"\nimport from items Item\nimport from tools Tool", 4, 19, "Importing 'Tool' would also import its variant 'sword', but 'sword' is a variant of the imported choice 'Item'.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\nwrite_line sword\nuse sword \"tools.prp\"", 2, 19, "Importing 'Item' would also import its variant 'sword', but 'sword' is a module name in this file.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\nimport from items Item", 3, 19, "'Item' is already imported in this file.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\ndef int lamp 1", 3, 9, "'lamp' is an imported variant and cannot be used as a variable name.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\nfunction void f Item lamp write_line lamp", 3, 22, "'lamp' is an imported variant and cannot be used as a parameter name.")]
    [InlineData("use items \"items.prp\"\nimport from items Item\nfunction int lamp return 1", 2, 19, "Importing 'Item' would also import its variant 'lamp', but 'lamp' is a function of this file.")]
    [InlineData("use items \"items.prp\"\nwrite_line from items Item", 2, 12, "'Item' is a choice, not a value; name one of its variants, such as from items sword.")]
    [InlineData("use items \"items.prp\"\nfrom items Item", 2, 1, "A statement cannot start with the choice 'Item': a choice is a type, and no statement may start with a type name. Use one of its variants as a value, as in def from items Item <name> from items sword.")]
    [InlineData("use items \"items.prp\"\nfrom items sword", 2, 1, "'from items sword' cannot stand alone as a statement.")]
    [InlineData("use items \"items.prp\"\ndef from items sword x 1", 2, 16, "'sword' of module 'items' is a variant of the choice 'Item', not a type.")]
    [InlineData("use items \"items.prp\"\ndef from items Secret x 1", 2, 16, "Choice 'Secret' of module 'items' is not exported")]
    [InlineData("use items \"items.prp\"\ndef int x from items axe", 2, 22, "Module 'items' has no function, record, choice or variant 'axe'.")]
    [InlineData("use items \"items.prp\"\ndef from items Axe x 1", 2, 16, "Module 'items' has no record or choice 'Axe'.")]
    [InlineData("use relay \"relay.prp\"\nwrite_line from relay sword", 2, 23, "Module 'relay' only imports 'sword' and cannot pass it on; use the module that exports it.")]
    [InlineData("use relay \"relay.prp\"\ndef from relay Item x from relay first", 2, 16, "Module 'relay' only imports 'Item' and cannot pass it on; use the module that exports it.")]
    public void RejectsInvalidUsesOfOtherModulesChoices(string source, int line, int column, string message)
    {
        var error = FailFile(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.StartsWith(message, error.Message);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
    }

    [Theory]
    [InlineData("choice Mood calm end\nexport function Mood feel return calm\n", 2, 22, "Exported function 'feel' uses the choice 'Mood', but 'Mood' is not exported; export it too.")]
    [InlineData("choice Mood calm end\nexport record Face Mood mood\n", 2, 15, "Exported record 'Face' uses the choice 'Mood' in its field 'mood', but 'Mood' is not exported; export it too.")]
    [InlineData("choice Mood calm end\nexport function void show list Mood moods write_line moods\n", 2, 22, "Exported function 'show' uses the choice 'Mood', but 'Mood' is not exported; export it too.")]
    public void AnExportedDeclarationCannotMentionAPrivateChoice(string module, int line, int column, string message)
    {
        var path = WriteFile("mood.prp", module);

        var error = FailFile("use mood \"mood.prp\"\nwrite_line 1\n");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal((path, line, column), (error.Position.Source, error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void TheMainScriptMayExportAFunctionThatUsesAPrivateChoice()
    {
        Assert.Equal("calm\n", RunFile("choice Mood calm end\nexport function Mood feel return calm\nwrite_line feel\n"));
    }

    [Fact]
    public void ImportedChoicesPersistAcrossReplInputs()
    {
        var main = Path.Combine(_directory, "main.prp");
        _session.Run("use items \"items.prp\"", main);
        _session.Run("import from items Item", main);
        _session.Run("def Item i shield", main);
        _session.Run("write_line equal i from items shield", main);
        var error = Assert.Single(Assert.Throws<PyropException>(() => _session.Run("choice Foo lamp end", main)).Errors);
        _session.Run("import from items Item", main);
        _session.Run("write_line lamp", main);

        Assert.Equal("'lamp' is imported in this file and cannot also be a variant.", error.Message);
        Assert.Equal("true\nlamp\n", _output.ToString());
    }

    [Fact]
    public void PrintsQualifiedVariantsInTheTree()
    {
        const string source = """
            use items "items.prp"
            def from items Item a from items sword
            write_line equal a from items lamp
            """;
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, WriteFile("main.prp", source));

        Assert.Null(inspected.Error);
        Assert.Equal(
            "use items \"items.prp\"\ndef from items Item a from items sword\nwrite_line (equal a from items lamp)\n",
            inspected.Text);
    }
}
