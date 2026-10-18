using System.Text;
using Pyrop.Parsing;

namespace Pyrop.Bundling;

public sealed record BundledFile(string Name, string Text);

public sealed class Bundle
{
    private const int FormatVersion = 1;
    private const int TrailerLength = sizeof(long) + 8;

    private static readonly byte[] Magic = "PYROPBDL"u8.ToArray();

    public Bundle(string entryName, IReadOnlyList<BundledFile> files)
    {
        if (files.Select(file => file.Name).Distinct(SourceLoader.PathComparer).Count() != files.Count)
        {
            throw new InvalidDataException("The bundle names a file more than once.");
        }

        EntryName = entryName;
        Files = files;
        Entry = files.SingleOrDefault(file => file.Name == entryName)
            ?? throw new InvalidDataException("The bundle does not contain its entry file.");
    }

    public static string VirtualRoot { get; } =
        Path.GetFullPath(OperatingSystem.IsWindows() ? @"B:\pyrop-bundle\program" : "/pyrop-bundle/program");

    public string EntryName { get; }

    public IReadOnlyList<BundledFile> Files { get; }

    public BundledFile Entry { get; }

    public static IEnumerable<Bundle> Candidates(string entryFullPath, string entryText, IEnumerable<SourceFile> modules)
    {
        var texts = new Dictionary<string, string>(SourceLoader.PathComparer) { [entryFullPath] = entryText };
        foreach (var file in modules.Where(file => !SourceLoader.PathComparer.Equals(file.FullPath, entryFullPath)))
        {
            texts[file.FullPath] = file.Text;
        }

        for (var root = CommonDirectory(texts.Keys); root is not null; root = Path.GetDirectoryName(root))
        {
            var files = texts
                .Select(pair => new BundledFile(Path.GetRelativePath(root, pair.Key), pair.Value))
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToList();
            yield return new Bundle(Path.GetRelativePath(root, entryFullPath), files);
        }
    }

    public void WriteExecutable(string baseExecutable, string outputPath)
    {
        var temporary = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(baseExecutable, temporary);
            using (var stream = new FileStream(temporary, FileMode.Append, FileAccess.Write))
            {
                AppendTo(stream);
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void AppendTo(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var start = stream.Position;
        writer.Write(FormatVersion);
        writer.Write(EntryName);
        writer.Write(Files.Count);
        foreach (var file in Files)
        {
            writer.Write(file.Name);
            writer.Write(file.Text);
        }

        writer.Write(stream.Position - start);
        writer.Write(Magic);
    }

    public static Bundle? ReadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < TrailerLength)
        {
            return null;
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Seek(-TrailerLength, SeekOrigin.End);
        var length = reader.ReadInt64();
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            return null;
        }

        try
        {
            return ReadArchive(stream, reader, length);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException("The bundle cannot be read.", exception);
        }
    }

    private static Bundle ReadArchive(FileStream stream, BinaryReader reader, long length)
    {
        var end = stream.Length - TrailerLength;
        if (length <= 0 || length > end)
        {
            throw new InvalidDataException("The bundle trailer has an invalid length.");
        }

        stream.Seek(end - length, SeekOrigin.Begin);
        if (reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException("The bundle was written in an unknown format.");
        }

        var entryName = reader.ReadString();
        var count = reader.ReadInt32();
        if (count < 1 || count > length)
        {
            throw new InvalidDataException("The bundle has an invalid file count.");
        }

        var files = new BundledFile[count];
        for (var index = 0; index < files.Length; index++)
        {
            files[index] = new BundledFile(reader.ReadString(), reader.ReadString());
        }

        if (stream.Position != end)
        {
            throw new InvalidDataException("The bundle does not end where its trailer says.");
        }

        return new Bundle(entryName, files);
    }

    private static string CommonDirectory(IEnumerable<string> fullPaths)
    {
        var paths = fullPaths.ToList();
        var root = Path.GetDirectoryName(paths[0]);
        while (root is not null && !paths.All(path => IsInside(path, root)))
        {
            root = Path.GetDirectoryName(root);
        }

        return root ?? throw new BuildException("The program's files have no common directory, for example because they are on different drives.");
    }

    private static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }
}

public sealed class BuildException(string message) : Exception(message);

public sealed class BundleSourceStore : ISourceStore
{
    private readonly Dictionary<string, BundledFile> _files = new(SourceLoader.PathComparer);

    public BundleSourceStore(Bundle bundle)
    {
        foreach (var file in bundle.Files)
        {
            _files[FullPathOf(file.Name)] = file;
        }
    }

    public static string FullPathOf(string name) => Path.GetFullPath(Path.Combine(Bundle.VirtualRoot, name));

    public StoredSource Read(string fullPath) =>
        _files.TryGetValue(fullPath, out var file)
            ? new StoredSource(file.Text, file.Name)
            : throw new FileNotFoundException($"The bundle contains no file at {fullPath}.", fullPath);

    public string DisplayNameOf(string fullPath) =>
        _files.TryGetValue(fullPath, out var file) ? file.Name : Path.GetRelativePath(Bundle.VirtualRoot, fullPath);
}
