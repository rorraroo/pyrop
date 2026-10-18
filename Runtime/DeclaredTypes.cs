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

public sealed record ChoiceVariant(ChoiceType Choice, string Name, int Index, SourcePosition Position);

public sealed class ChoiceType : DeclaredType
{
    private readonly ChoiceValue[] _values;
    private readonly Dictionary<string, ChoiceVariant> _byName;

    public ChoiceType(
        string name, Module module, SourcePosition position, bool isExported, IReadOnlyList<(string Name, SourcePosition Position)> variants)
        : base(name, module, position, isExported)
    {
        Variants = variants.Select((variant, index) => new ChoiceVariant(this, variant.Name, index, variant.Position)).ToList();
        _values = Variants.Select(variant => new ChoiceValue(this, variant.Index)).ToArray();
        _byName = Variants.ToDictionary(variant => variant.Name);
    }

    public IReadOnlyList<ChoiceVariant> Variants { get; }

    public ChoiceValue ValueOf(int index) => _values[index];

    public ChoiceVariant? FindVariant(string name) => _byName.GetValueOrDefault(name);

    public bool HasSameVariants(IReadOnlyList<string> names) =>
        Variants.Select(variant => variant.Name).SequenceEqual(names);

    public string DescribeVariants() => string.Join(" ", Variants.Select(variant => variant.Name));

    public string ListVariants()
    {
        var names = Variants.Select(variant => variant.Name).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names.SkipLast(1))} and {names[^1]}";
    }
}
