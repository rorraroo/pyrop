using System.Collections.Immutable;

namespace Pyrop.Runtime;

public sealed record ChoiceValue : Value
{
    public ChoiceValue(ChoiceType choice, int index, ImmutableArray<Value> data = default)
    {
        Choice = choice;
        Index = index;
        Data = data.IsDefault ? [] : data;
    }

    public ChoiceType Choice { get; }

    public int Index { get; }

    public ImmutableArray<Value> Data { get; }

    public bool HasData => Data.Length > 0;

    public override PyropType Type => Choice;

    public string Name => Choice.Variants[Index].Name;

    public override string ToText() => HasData ? TextForm.Of(this, message => new InvalidOperationException(message)) : Name;

    public bool Equals(ChoiceValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Choice != other.Choice || Index != other.Index)
        {
            return false;
        }

        return Data.SequenceEqual(other.Data);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Choice);
        hash.Add(Index);
        foreach (var value in Data)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
