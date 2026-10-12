using Pyrop.Bundling;
using Pyrop.Cli;

namespace Pyrop.Tests;

public sealed class BundleTests : IDisposable
{
    private static readonly byte[] BaseBytes = "not really an executable"u8.ToArray();

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-bundle-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private string Write(string name, string text)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string BaseExecutable()
    {
        var path = PathOf("pyrop-base");
        File.WriteAllBytes(path, BaseBytes);
        return path;
    }

    private void WriteProgram()
    {
        Write(Path.Combine("lib", "text.prp"), "function string shout string text return add upper text \"!\"");
        Write(Path.Combine("tools", "filter.prp"), "include \"../lib/text.prp\"\nwrite_line shout argument_at 0\nwrite_line divide 1 0");
    }

    [Fact]
    public void BuildAppendsTheProgramAndItsIncludesToTheBaseExecutable()
    {
        WriteProgram();

        Assert.Equal(0, BuildCommand.Build(PathOf(Path.Combine("tools", "filter.prp")), PathOf("filter-built"), BaseExecutable()));

        var built = File.ReadAllBytes(PathOf(OperatingSystem.IsWindows() ? "filter-built.exe" : "filter-built"));
        Assert.Equal(BaseBytes, built[..BaseBytes.Length]);
        var bundle = Bundle.ReadFrom(PathOf(OperatingSystem.IsWindows() ? "filter-built.exe" : "filter-built"))!;
        Assert.Equal(Path.Combine("tools", "filter.prp"), bundle.EntryName);
        Assert.Equal(
            [Path.Combine("lib", "text.prp"), Path.Combine("tools", "filter.prp")],
            bundle.Files.Select(file => file.Name));
    }

    [Fact]
    public void ABundledProgramRunsWithItsIncludesAndNamesItsFilesInErrors()
    {
        WriteProgram();
        var entry = PathOf(Path.Combine("tools", "filter.prp"));
        var bundle = Bundle.Candidates(entry, File.ReadAllText(entry), [IncludedFile("lib/text.prp")]).First();
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output, new Runtime.HostContext(["hi"], _ => null), new BundleSourceStore(bundle));

        var exception = Assert.Throws<PyropException>(() =>
            session.Run(bundle.Entry.Text, bundle.EntryName, BundleSourceStore.FullPathOf(bundle.EntryName)));

        Assert.Equal("HI!\n", output.ToString());
        var error = Assert.Single(exception.Errors);
        Assert.Equal(Path.Combine("tools", "filter.prp"), error.Position.Source);
        Assert.Equal(bundle.Entry.Text, session.SourceText(error.Position.Source));
    }

    [Fact]
    public void AFileWithoutATrailerHasNoBundle()
    {
        Assert.Null(Bundle.ReadFrom(BaseExecutable()));
        Assert.Null(Bundle.ReadFrom(Write("short", "x")));
    }

    [Fact]
    public void ADamagedBundleIsRejected()
    {
        var path = BaseExecutable();
        using (var stream = new FileStream(path, FileMode.Append))
        {
            stream.Write(BitConverter.GetBytes(long.MaxValue));
            stream.Write("PYROPBDL"u8);
        }

        Assert.Throws<InvalidDataException>(() => Bundle.ReadFrom(path));
    }

    public static TheoryData<byte[]> DamagedArchives => new()
    {
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(-1); }),
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(0); }),
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(int.MaxValue); }),
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(1); writer.Write("b"); writer.Write("x"); }),
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(2); writer.Write("a"); writer.Write("x"); writer.Write("a"); writer.Write("y"); }),
        Archive(writer => { writer.Write(1); writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }); }),
        Archive(writer => { writer.Write(1); writer.Write("a"); writer.Write(1); writer.Write("a"); writer.Write("x"); writer.Write((byte)0); }),
        Archive(writer => { writer.Write(2); writer.Write("a"); writer.Write(1); writer.Write("a"); writer.Write("x"); }),
    };

    [Theory]
    [MemberData(nameof(DamagedArchives))]
    public void EveryKindOfDamageIsReportedAsInvalidData(byte[] archive)
    {
        var path = BaseExecutable();
        using (var stream = new FileStream(path, FileMode.Append))
        {
            stream.Write(archive);
            stream.Write(BitConverter.GetBytes((long)archive.Length));
            stream.Write("PYROPBDL"u8);
        }

        Assert.Throws<InvalidDataException>(() => Bundle.ReadFrom(path));
    }

    [Fact]
    public void AnEntrySpelledDifferentlyFromItsCyclicIncludeIsBuilt()
    {
        Write("lib.prp", "include \"main.prp\"\nfunction int twice int x return multiply x 2");
        Write("main.prp", "include \"lib.prp\"\nwrite_line twice 2");
        var differentCase = OperatingSystem.IsWindows() ? "MAIN.prp" : "main.prp";

        Assert.Equal(0, BuildCommand.Build(PathOf(differentCase), PathOf("cyclic"), BaseExecutable()));
    }

    [Fact]
    public void ARelativeIncludeThroughTheParentDirectoryIsBuilt()
    {
        Write(Path.Combine("tools", "a.prp"), "def int answer 42");
        var script = Write(Path.Combine("tools", "e.prp"), "include \"../tools/a.prp\"\nwrite_line answer");

        Assert.Equal(0, BuildCommand.Build(script, PathOf("through-parent"), BaseExecutable()));

        var bundle = Bundle.ReadFrom(PathOf(OperatingSystem.IsWindows() ? "through-parent.exe" : "through-parent"))!;
        var output = new StringOutput();
        new PyropSession(new StringReader(""), output, store: new BundleSourceStore(bundle))
            .Run(bundle.Entry.Text, bundle.EntryName, BundleSourceStore.FullPathOf(bundle.EntryName));
        Assert.Equal("42\n", output.ToString());
    }

    [Fact]
    public void AnOutputThatIsADirectoryIsRejected()
    {
        var script = Write("main.prp", "write_line 1");
        Directory.CreateDirectory(PathOf("out"));

        Assert.Equal(1, BuildCommand.Build(script, PathOf("out"), BaseExecutable()));
        Assert.Equal(1, BuildCommand.Build(script, PathOf("out") + Path.DirectorySeparatorChar, BaseExecutable()));
        Assert.Empty(Directory.GetFileSystemEntries(PathOf("out")));
        Assert.Empty(Directory.GetFiles(_directory, "out*"));
    }

    [Fact]
    public void AnExistingOutputIsReplaced()
    {
        var script = Write("main.prp", "write_line 1");
        var output = PathOf(OperatingSystem.IsWindows() ? "main-built.exe" : "main-built");
        File.WriteAllText(output, "old");

        Assert.Equal(0, BuildCommand.Build(script, output, BaseExecutable()));
        Assert.Equal("write_line 1", Bundle.ReadFrom(output)!.Entry.Text);
    }

    [Fact]
    public void AProgramWithErrorsIsNotBuilt()
    {
        var script = Write("bad.prp", "write_line add 1 \"a\"");

        Assert.Equal(1, BuildCommand.Build(script, PathOf("bad-built"), BaseExecutable()));
        Assert.Empty(Directory.GetFiles(_directory, "bad-built*"));
    }

    [Fact]
    public void AnAbsoluteIncludeIsNotBuilt()
    {
        var library = Write("library.prp", "def int answer 42");
        var script = Write("main.prp", $"include \"{library.Replace("\\", "/")}\"\nwrite_line answer");

        Assert.Equal(1, BuildCommand.Build(script, PathOf("main-built"), BaseExecutable()));
        Assert.Empty(Directory.GetFiles(_directory, "main-built*"));
    }

    [Fact]
    public void ABuildNeverReplacesItsOwnSources()
    {
        var script = Write("main.prp", "write_line 1");

        Assert.Equal(1, BuildCommand.Build(script, script, BaseExecutable()));
        Assert.Equal("write_line 1", File.ReadAllText(script));
    }

    private static byte[] Archive(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            write(writer);
        }

        return stream.ToArray();
    }

    private Parsing.SourceFile IncludedFile(string relativePath)
    {
        var fullPath = PathOf(relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(fullPath);
        return new Parsing.SourceFile(fullPath, relativePath, text, Lexing.Lexer.Tokenize(text, relativePath));
    }
}
