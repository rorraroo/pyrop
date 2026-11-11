namespace Pyrop.Runtime;

public static class FileOperations
{
    private const string Folder = "folder";

    private static readonly char[] EverySeparator = ['/', '\\'];

    public static string NameOf(string path) => path[(path.LastIndexOfAny(EverySeparator) + 1)..];

    public static string FolderOf(string path)
    {
        var last = path.LastIndexOfAny(EverySeparator);
        if (last < 0)
        {
            return "";
        }

        var root = RootLengthOf(path);
        return last < root ? path[..root] : path[..last];
    }

    private static int RootLengthOf(string path)
    {
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && EverySeparator.Contains(path[2]))
        {
            return 3;
        }

        var leading = 0;
        while (leading < path.Length && EverySeparator.Contains(path[leading]))
        {
            leading++;
        }

        return leading;
    }

    public static string ExtensionOf(string path)
    {
        var name = NameOf(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? "" : name[dot..];
    }

    public static string JoinPath(IBuiltinInvocation invocation, string first, string second)
    {
        if (second.Length == 0)
        {
            return first;
        }

        if (second[0] is '/' or '\\' || (second.Length >= 2 && second[1] == ':' && char.IsAsciiLetter(second[0])))
        {
            throw invocation.Fail($"'join_path' cannot add the full path \"{second}\" to another path.");
        }

        return first.Length == 0 || first[^1] is '/' or '\\' ? first + second : $"{first}/{second}";
    }

    public static Value? DeleteFile(IBuiltinInvocation invocation, string path)
    {
        RequireExistingFile(invocation, path);
        Attempt(invocation, "delete", path, "file", () =>
        {
            AllowDeleting(path);
            File.Delete(path);
        });
        return null;
    }

    public static Value? CopyFile(IBuiltinInvocation invocation, string from, string to) =>
        Transfer(invocation, "copied", "copy", from, to, FileShare.ReadWrite | FileShare.Delete, () =>
        {
            AllowReplacing(to);
            File.Copy(from, to, overwrite: true);
        });

    public static Value? MoveFile(IBuiltinInvocation invocation, string from, string to) =>
        Transfer(invocation, "moved", "move", from, to, FileShare.None, () =>
        {
            AllowDeleting(to);
            File.Move(from, to, overwrite: true);
        });

    private static void AllowDeleting(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            AllowReplacing(path);
        }
    }

    private static void AllowReplacing(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    public static Value? MakeFolder(IBuiltinInvocation invocation, string path)
    {
        TextFiles.RequirePath(invocation, path, Folder);
        if (File.Exists(path))
        {
            throw invocation.Fail($"\"{path}\" is a file, not a folder.");
        }

        Attempt(invocation, "make", path, Folder, () => Directory.CreateDirectory(path));
        return null;
    }

    public static Value? DeleteFolder(IBuiltinInvocation invocation, string path)
    {
        TextFiles.RequirePath(invocation, path, Folder);
        if (File.Exists(path))
        {
            throw invocation.Fail($"\"{path}\" is a file, not a folder.");
        }

        if (!Directory.Exists(path))
        {
            throw invocation.Fail($"Folder \"{path}\" does not exist.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (IsDevicePath(full) || IsLocalShare(full))
        {
            throw invocation.Fail($"\"{path}\" is written as a device or a share on this computer; write it as a plain path to delete it.");
        }

        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), full, StringComparison.OrdinalIgnoreCase))
        {
            throw invocation.Fail($"\"{path}\" is a root folder, so it cannot be deleted.");
        }

        var towardsCurrent = Path.GetRelativePath(WithLinksResolved(full), WithLinksResolved(Environment.CurrentDirectory));
        if (!Path.IsPathRooted(towardsCurrent) && towardsCurrent.Split(Path.DirectorySeparatorChar)[0] != "..")
        {
            throw invocation.Fail($"\"{path}\" holds the folder the program is running in, so it cannot be deleted.");
        }

        Attempt(invocation, "delete", path, Folder, () =>
        {
            RemoveLinksBelow(path);
            Directory.Delete(path, recursive: true);
        });
        return null;
    }

    private static bool IsDevicePath(string full) =>
        OperatingSystem.IsWindows() && (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal));

    private static bool IsLocalShare(string full)
    {
        if (!OperatingSystem.IsWindows() || !full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var server = full[2..].Split('\\')[0];
        return server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || server is "127.0.0.1" or "::1" or "0.0.0.0" or "[::1]";
    }

    private static string WithLinksResolved(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var part in full[root.Length..].Split(EverySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    current = target.FullName;
                }
            }
            catch (Exception exception) when (TextFiles.IsFileSystemFailure(exception))
            {
                return current;
            }
        }

        return current;
    }

    private static void RemoveLinksBelow(string path)
    {
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false }))
        {
            AllowDeleting(file.FullName);
        }

        foreach (var entry in new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false }))
        {
            if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Delete();
            }
            else
            {
                RemoveLinksBelow(entry.FullName);
            }
        }
    }

    public static Value ModifiedAt(IBuiltinInvocation invocation, string path)
    {
        RequireExistingFile(invocation, path);
        var changed = DateTime.MinValue;
        Attempt(invocation, "read the time of", path, "file", () => changed = File.GetLastWriteTimeUtc(path));
        return TimeOperations.LocalMomentOf(invocation, changed);
    }

    private static bool CanOpen(string path, FileShare sharing)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, sharing);
            return true;
        }
        catch (Exception exception) when (TextFiles.IsFileSystemFailure(exception))
        {
            return false;
        }
    }

    private static Value? Transfer(IBuiltinInvocation invocation, string done, string verb, string from, string to, FileShare sharing, Action action)
    {
        RequireExistingFile(invocation, from);
        TextFiles.RequirePath(invocation, to, "file");
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw invocation.Fail($"\"{from}\" cannot be {done} onto itself.");
        }

        try
        {
            action();
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            throw invocation.Fail(TextFiles.MissingDirectory(to));
        }
        catch (Exception exception) when (TextFiles.IsFileSystemFailure(exception))
        {
            throw invocation.Fail(TextFiles.FileSystemFailure(verb, CanOpen(from, sharing) ? to : from, exception));
        }
    }

    private static void RequireExistingFile(IBuiltinInvocation invocation, string path)
    {
        TextFiles.RequirePath(invocation, path, "file");
        if (!File.Exists(path))
        {
            throw invocation.Fail($"File \"{path}\" does not exist.");
        }
    }

    private static void Attempt(IBuiltinInvocation invocation, string verb, string path, string kind, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (TextFiles.IsFileSystemFailure(exception))
        {
            throw invocation.Fail(TextFiles.FileSystemFailure(verb, path, exception, kind));
        }
    }
}
