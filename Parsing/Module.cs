using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public sealed class Module(string? fullPath, string displayName)
{
    public string? FullPath { get; } = fullPath;

    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}

public sealed record LoadedModule(Module Module, ModuleNamespace Names, SourceFile? File)
{
    public bool IsLibrary => File is null;
}

public sealed record ModuleUse(string Name, LoadedModule Target, SourcePosition Position, Token Statement);

public sealed record ModuleImport(string Module, string Name, object Target, SourcePosition Position, Token Statement)
{
    public FunctionSignature? Function => Target as FunctionSignature;

    public RecordType? Record => Target as RecordType;

    public ChoiceType? Choice => Target as ChoiceType;

    public BuiltinOperation? Operation => Target as BuiltinOperation;

    public string Kind => Target switch
    {
        RecordType => "record",
        ChoiceType => "choice",
        BuiltinOperation => "operation",
        _ => "function",
    };
}

public sealed class ModuleFailures
{
    private readonly Dictionary<Token, PyropException> _statements = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, PyropException> _names = new();

    public static ModuleFailures None { get; } = new();

    public bool IsEmpty => _statements.Count == 0;

    public void Add(Token statement, string? name, PyropException error)
    {
        _statements.TryAdd(statement, error);
        if (name is not null)
        {
            _names.TryAdd(name, error);
        }
    }

    public PyropException? OfStatement(Token statement) => _statements.GetValueOrDefault(statement);

    public PyropException? OfName(string name) => _names.GetValueOrDefault(name);
}

public sealed class ModuleNamespace
{
    private static readonly IReadOnlyDictionary<string, ModuleUse> NoUses = new Dictionary<string, ModuleUse>();
    private static readonly IReadOnlyDictionary<string, ModuleImport> NoImports = new Dictionary<string, ModuleImport>();
    private static readonly IReadOnlyDictionary<string, RecordType> NoRecords = new Dictionary<string, RecordType>();
    private static readonly IReadOnlyDictionary<string, ChoiceType> NoChoices = new Dictionary<string, ChoiceType>();
    private static readonly IReadOnlyDictionary<string, ChoiceVariant> NoVariants = new Dictionary<string, ChoiceVariant>();
    private static readonly IReadOnlyDictionary<string, BuiltinOperation> NoOperations = new Dictionary<string, BuiltinOperation>();

    private ModuleNamespace(
        Module module,
        FunctionTable functions,
        IReadOnlyDictionary<string, RecordType> records,
        IReadOnlyDictionary<string, ChoiceType> choices,
        IReadOnlyDictionary<string, ChoiceVariant> variants,
        IReadOnlyDictionary<string, ModuleUse> uses,
        IReadOnlyDictionary<string, ModuleImport> imports,
        ModuleFailures failures,
        IReadOnlyDictionary<string, BuiltinOperation> operations,
        BuiltinLibraries libraries)
    {
        Module = module;
        Operations = operations;
        Libraries = libraries;
        Functions = functions;
        Records = records;
        Choices = choices;
        Variants = variants;
        Uses = uses;
        Imports = imports;
        Failures = failures;
    }

    public static ModuleNamespace For(Module module, BuiltinLibraries? libraries = null) =>
        new(module, FunctionTable.Empty, NoRecords, NoChoices, NoVariants, NoUses, NoImports, ModuleFailures.None, NoOperations,
            libraries ?? BuiltinLibraries.Standard);

    public static ModuleNamespace ForLibrary(Module module, IReadOnlyDictionary<string, BuiltinOperation> operations) =>
        new(module, FunctionTable.Empty, NoRecords, NoChoices, NoVariants, NoUses, NoImports, ModuleFailures.None, operations,
            new BuiltinLibraries([]));

    public Module Module { get; }

    public FunctionTable Functions { get; }

    public IReadOnlyDictionary<string, RecordType> Records { get; }

    public IReadOnlyDictionary<string, ChoiceType> Choices { get; }

    public IReadOnlyDictionary<string, ChoiceVariant> Variants { get; }

    public IReadOnlyDictionary<string, ModuleUse> Uses { get; }

    public IReadOnlyDictionary<string, ModuleImport> Imports { get; }

    public ModuleFailures Failures { get; }

    public IReadOnlyDictionary<string, BuiltinOperation> Operations { get; }

    public BuiltinLibraries Libraries { get; }

    public FunctionSignature? FindCallable(string name) =>
        Functions.Find(name) ?? Imports.GetValueOrDefault(name)?.Function;

    public BuiltinOperation? FindOperation(string name) =>
        Builtins.All.GetValueOrDefault(name) ?? Imports.GetValueOrDefault(name)?.Operation;

    public string? LibraryHint(string name) => Libraries.Hint(name, this);

    public RecordType? FindRecord(string name) =>
        Records.GetValueOrDefault(name) ?? Imports.GetValueOrDefault(name)?.Record;

    public ChoiceType? FindChoice(string name) =>
        Choices.GetValueOrDefault(name) ?? Imports.GetValueOrDefault(name)?.Choice;

    public DeclaredType? FindType(string name) => (DeclaredType?)FindRecord(name) ?? FindChoice(name);

    public ChoiceVariant? FindVariant(string name) => Variants.GetValueOrDefault(name);

    public ChoiceVariant? OwnVariant(string name) =>
        Variants.TryGetValue(name, out var variant) && variant.Choice.Module == Module ? variant : null;

    public string? Describe(string name) =>
        Functions.Contains(name) ? "a function"
        : Records.ContainsKey(name) ? "a record"
        : Choices.ContainsKey(name) ? "a choice"
        : Imports.TryGetValue(name, out var import) ? $"an imported {import.Kind}"
        : Variants.TryGetValue(name, out var variant) ? (variant.Choice.Module == Module ? "a variant" : "an imported variant")
        : Uses.ContainsKey(name) ? "a module"
        : null;

    public ModuleNamespace With(
        IEnumerable<FunctionSignature> functions,
        IEnumerable<ModuleUse> uses,
        IEnumerable<ModuleImport> imports,
        IEnumerable<RecordType>? records = null,
        IEnumerable<ChoiceType>? choices = null)
    {
        var combinedUses = new Dictionary<string, ModuleUse>(Uses);
        foreach (var use in uses)
        {
            combinedUses.Add(use.Name, use);
        }

        var addedImports = imports.ToList();
        var combinedImports = new Dictionary<string, ModuleImport>(Imports);
        foreach (var import in addedImports)
        {
            combinedImports.Add(import.Name, import);
        }

        var combinedRecords = new Dictionary<string, RecordType>(Records);
        foreach (var record in records ?? [])
        {
            combinedRecords.Add(record.Name, record);
        }

        var addedChoices = (choices ?? []).ToList();
        var combinedChoices = new Dictionary<string, ChoiceType>(Choices);
        foreach (var choice in addedChoices)
        {
            combinedChoices.Add(choice.Name, choice);
        }

        var combinedVariants = new Dictionary<string, ChoiceVariant>(Variants);
        foreach (var variant in addedChoices.SelectMany(choice => choice.Variants))
        {
            combinedVariants[variant.Name] = variant;
        }

        foreach (var variant in addedImports.SelectMany(import => import.Choice?.Variants ?? []))
        {
            combinedVariants.TryAdd(variant.Name, variant);
        }

        return new ModuleNamespace(
            Module,
            Functions.With(functions),
            combinedRecords,
            combinedChoices,
            combinedVariants,
            combinedUses,
            combinedImports,
            ModuleFailures.None,
            Operations,
            Libraries);
    }

    public ModuleNamespace WithFailures(ModuleFailures failures) =>
        new(Module, Functions, Records, Choices, Variants, Uses, Imports, failures, Operations, Libraries);
}
