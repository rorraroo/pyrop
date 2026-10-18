using System.Collections.Immutable;

namespace Pyrop.Runtime;

public sealed record RecordValue : Value
{
    public RecordValue(RecordType record, ImmutableArray<Value> fields)
    {
        Record = record;
        Fields = fields;
    }

    public RecordType Record { get; }

    public ImmutableArray<Value> Fields { get; }

    public override PyropType Type => Record;

    public RecordValue With(int index, Value value) => new(Record, Fields.SetItem(index, value));

    public override string ToText() => TextForm.Of(this, message => new InvalidOperationException(message));

    public bool Equals(RecordValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Record != other.Record)
        {
            return false;
        }

        return Fields.SequenceEqual(other.Fields);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Record);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }
}
