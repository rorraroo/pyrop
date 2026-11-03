using Pyrop.Parsing;

namespace Pyrop.Runtime;

public sealed class BuiltinLibrary
{
    public BuiltinLibrary(string name, IReadOnlyList<BuiltinOperation> operations, IReadOnlyDictionary<string, string> formerNames)
    {
        Name = name;
        Module = new Module(null, $"the library {name}");
        Operations = operations.ToDictionary(operation => operation.Name);
        FormerNames = formerNames;
        Loaded = new LoadedModule(Module, ModuleNamespace.ForLibrary(Module, Operations), null);
    }

    public string Name { get; }

    public Module Module { get; }

    public IReadOnlyDictionary<string, BuiltinOperation> Operations { get; }

    public IReadOnlyDictionary<string, string> FormerNames { get; }

    public LoadedModule Loaded { get; }

    public bool Exports(string name) => Operations.ContainsKey(name);
}

public sealed class BuiltinLibraries
{
    public BuiltinLibraries(IEnumerable<BuiltinLibrary> libraries)
    {
        All = libraries.OrderBy(library => library.Name, StringComparer.Ordinal).ToList();
    }

    public static BuiltinLibraries Standard { get; } = new(
    [
        LibraryOperations.Files,
        LibraryOperations.Program,
        LibraryOperations.Math,
        LibraryOperations.Random,
    ]);

    public static BuiltinLibraries None { get; } = new([]);

    public IReadOnlyList<BuiltinLibrary> All { get; }

    public BuiltinLibrary? Find(string name) => All.FirstOrDefault(library => library.Name == name);

    public string Unknown(string name) =>
        All.Count == 0
            ? $"There is no built-in library '{name}'; this host offers none."
            : $"There is no built-in library '{name}'; the libraries are: {string.Join(", ", All.Select(library => library.Name))}.";

    public string? Hint(string word, ModuleNamespace names)
    {
        foreach (var library in All)
        {
            if (names.Uses.TryGetValue(library.Name, out var taken) && taken.Target.Module != library.Module)
            {
                continue;
            }

            if (library.Exports(word))
            {
                return $"'{word}' comes from the library {library.Name}: {HowToReach(library, word, names)}.";
            }

            if (library.FormerNames.TryGetValue(word, out var current))
            {
                return $"'{word}' is now '{current}' in the library {library.Name}: {HowToReach(library, current, names)}.";
            }
        }

        return null;
    }

    private static string HowToReach(BuiltinLibrary library, string name, ModuleNamespace names)
    {
        var import = $"{Keywords.Import} {Keywords.From} {library.Name} {name} {Keywords.End}";
        var qualified = $"{Keywords.From} {library.Name} {name}";
        return names.Uses.TryGetValue(library.Name, out var use) && use.Target.Module == library.Module
            ? $"add {import}, or write {qualified}"
            : $"add {Keywords.Use} {library.Name} {Keywords.Library} and {import}, or write {qualified}";
    }
}
