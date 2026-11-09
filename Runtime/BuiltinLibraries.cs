using Pyrop.Parsing;

namespace Pyrop.Runtime;

public sealed class BuiltinLibrary
{
    public BuiltinLibrary(
        string name,
        IReadOnlyList<BuiltinOperation> operations,
        IReadOnlyDictionary<string, string> formerNames,
        Module? module = null,
        IReadOnlyList<ChoiceType>? choices = null,
        IReadOnlyList<RecordType>? records = null)
    {
        Name = name;
        Module = module ?? ModuleFor(name);
        Operations = operations.ToDictionary(operation => operation.Name);
        FormerNames = formerNames;
        Choices = choices ?? [];
        Records = records ?? [];
        Loaded = new LoadedModule(Module, ModuleNamespace.ForLibrary(Module, Operations).With([], [], [], Records, Choices), null);
    }

    public static Module ModuleFor(string name) => new(null, $"the library {name}");

    public string Name { get; }

    public Module Module { get; }

    public IReadOnlyDictionary<string, BuiltinOperation> Operations { get; }

    public IReadOnlyDictionary<string, string> FormerNames { get; }

    public IReadOnlyList<ChoiceType> Choices { get; }

    public IReadOnlyList<RecordType> Records { get; }

    public LoadedModule Loaded { get; }

    public bool Exports(string name) => ImportNameFor(name) is not null;

    public string? ImportNameFor(string name) =>
        Operations.ContainsKey(name) || Choices.Any(choice => choice.Name == name) || Records.Any(record => record.Name == name) ? name
        : Choices.FirstOrDefault(choice => choice.FindVariant(name) is not null)?.Name;

    public string KindOf(string name) =>
        Operations.ContainsKey(name) ? "an operation"
        : Choices.Any(choice => choice.Name == name) || Records.Any(record => record.Name == name) ? "a type"
        : "a variant";
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
        LibraryOperations.Terminal,
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

    public string KindOf(string word) => All.FirstOrDefault(library => library.Exports(word))?.KindOf(word) ?? "an operation";

    private static string HowToReach(BuiltinLibrary library, string name, ModuleNamespace names)
    {
        var importName = library.ImportNameFor(name) ?? name;
        if (importName != name && names.Imports.TryGetValue(importName, out var imported) && imported.Module == library.Name)
        {
            importName = name;
        }

        var import = $"{Keywords.Import} {Keywords.From} {library.Name} {importName} {Keywords.End}";
        var qualified = $"{Keywords.From} {library.Name} {name}";
        return names.Uses.TryGetValue(library.Name, out var use) && use.Target.Module == library.Module
            ? $"add {import}, or write {qualified}"
            : $"add {Keywords.Use} {library.Name} {Keywords.Library} and {import}, or write {qualified}";
    }
}
