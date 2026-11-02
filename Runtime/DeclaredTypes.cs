using System.Runtime.CompilerServices;
using Pyrop.Parsing;

namespace Pyrop.Runtime;

public abstract class DeclaredType(string name, Module module, SourcePosition position, bool isExported) : PyropType
{
    public override string Name { get; } = name;

    public Module Module { get; } = module;

    public SourcePosition Position { get; } = position;

    public bool IsExported { get; } = isExported;

    public string QualifiedName => $"{Name} (declared in {Module.DisplayName})";

    public override bool IsConcrete => true;

    public override string NameQualifying(IReadOnlySet<DeclaredType> ambiguous) =>
        ambiguous.Contains(this) ? QualifiedName : Name;

    public override bool Equals(PyropType? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public sealed record RecordField(PyropType Type, string Name, SourcePosition Position);

public sealed class RecordType(string name, Module module, SourcePosition position, bool isExported)
    : DeclaredType(name, module, position, isExported)
{
    private IReadOnlyList<RecordField> _fields = [];

    public IReadOnlyList<RecordField> Fields => _fields;

    public void Define(IReadOnlyList<RecordField> fields) => _fields = fields;

    public int IndexOf(string field)
    {
        for (var index = 0; index < _fields.Count; index++)
        {
            if (_fields[index].Name == field)
            {
                return index;
            }
        }

        return -1;
    }

    public RecordField? FindField(string field) => IndexOf(field) is var index and >= 0 ? _fields[index] : null;

    public bool HasSameFields(IReadOnlyList<RecordField> fields) =>
        _fields.Count == fields.Count
        && _fields.Zip(fields).All(pair => pair.First.Name == pair.Second.Name && pair.First.Type == pair.Second.Type);

    public string DescribeFields() =>
        string.Join(" ", _fields.Select(field => $"{field.Type.Name} {field.Name}"));
}

public sealed class ChoiceVariant(ChoiceType choice, string name, int index, SourcePosition position, int arity)
{
    private IReadOnlyList<PyropType> _data = [];

    public ChoiceType Choice { get; } = choice;

    public string Name { get; } = name;

    public int Index { get; } = index;

    public SourcePosition Position { get; } = position;

    public int Arity { get; } = arity;

    public IReadOnlyList<PyropType> Data => _data;

    public void Define(IReadOnlyList<PyropType> data) => _data = data;

    public string Example => Arity == 0 ? Name : $"{Name} …";

    public string Describe() => string.Join(" ", _data.Select(type => type.Name).Prepend(Name));
}

public sealed record VariantDeclaration(string Name, SourcePosition Position, int Arity);

public sealed class ChoiceType : DeclaredType
{
    private readonly ChoiceValue?[] _values;
    private readonly Dictionary<string, ChoiceVariant> _byName;

    public ChoiceType(
        string name, Module module, SourcePosition position, bool isExported, IReadOnlyList<VariantDeclaration> variants)
        : base(name, module, position, isExported)
    {
        Variants = variants.Select((variant, index) => new ChoiceVariant(this, variant.Name, index, variant.Position, variant.Arity)).ToList();
        _values = Variants.Select(variant => variant.Arity == 0 ? new ChoiceValue(this, variant.Index) : null).ToArray();
        _byName = Variants.ToDictionary(variant => variant.Name);
        HasData = Variants.Any(variant => variant.Arity > 0);
    }

    public IReadOnlyList<ChoiceVariant> Variants { get; }

    public bool HasData { get; }

    public ChoiceValue ValueOf(int index) =>
        _values[index] ?? throw new InvalidOperationException($"Variant '{Variants[index].Name}' carries data.");

    public ChoiceVariant? FindVariant(string name) => _byName.GetValueOrDefault(name);

    public bool HasSameVariants(IReadOnlyList<VariantDeclaration> variants) =>
        Variants.Select(variant => (variant.Name, variant.Arity)).SequenceEqual(variants.Select(variant => (variant.Name, variant.Arity)));

    public bool HasSameData(IReadOnlyList<IReadOnlyList<PyropType>> data) =>
        Variants.Zip(data).All(pair => pair.First.Data.SequenceEqual(pair.Second));

    public string DescribeVariants() => string.Join(" ", Variants.Select(variant => variant.Describe()));

    public string ListVariants()
    {
        var names = Variants.Select(variant => variant.Name).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names.SkipLast(1))} and {names[^1]}";
    }
}
