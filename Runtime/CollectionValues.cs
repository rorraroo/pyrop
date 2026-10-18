using System.Collections.Immutable;

namespace Pyrop.Runtime;

public sealed record ListValue : Value
{
    public ListValue(PyropType type, ImmutableList<Value> items)
    {
        Type = type;
        Items = items;
    }

    public static ListValue Empty(PyropType type) => new(type, ImmutableList<Value>.Empty);

    public override PyropType Type { get; }

    public ImmutableList<Value> Items { get; }

    public int Count => Items.Count;

    public ListValue With(ImmutableList<Value> items) => new(Type, items);

    public override string ToText() => TextForm.Of(this, message => new InvalidOperationException(message));

    public bool Equals(ListValue? other) =>
        ReferenceEquals(this, other)
        || (other is not null && Type == other.Type && Items.Count == other.Items.Count && Items.SequenceEqual(other.Items));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

public sealed record MapValue : Value
{
    private readonly ImmutableDictionary<Value, MapEntry> _entries;
    private readonly ImmutableSortedDictionary<long, Value> _order;
    private readonly long _nextOrder;

    private MapValue(
        PyropType type, ImmutableDictionary<Value, MapEntry> entries, ImmutableSortedDictionary<long, Value> order, long nextOrder)
    {
        Type = type;
        _entries = entries;
        _order = order;
        _nextOrder = nextOrder;
    }

    public static MapValue Empty(PyropType type) =>
        new(type, ImmutableDictionary<Value, MapEntry>.Empty, ImmutableSortedDictionary<long, Value>.Empty, 0);

    public override PyropType Type { get; }

    public PyropType KeyType => ((ConstructedType)Type).Arguments[0];

    public int Count => _entries.Count;

    public IEnumerable<Value> Keys => _order.Values;

    public IEnumerable<KeyValuePair<Value, Value>> Entries =>
        _order.Values.Select(key => new KeyValuePair<Value, Value>(key, _entries[key].Value));

    public Value? Find(Value key) => _entries.TryGetValue(key, out var entry) ? entry.Value : null;

    public bool ContainsKey(Value key) => _entries.ContainsKey(key);

    public MapValue Put(Value key, Value value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            return new MapValue(Type, _entries.SetItem(key, new MapEntry(value, entry.Order)), _order, _nextOrder);
        }

        return new MapValue(Type, _entries.Add(key, new MapEntry(value, _nextOrder)), _order.Add(_nextOrder, key), _nextOrder + 1);
    }

    public MapValue Remove(Value key) =>
        _entries.TryGetValue(key, out var entry)
            ? new MapValue(Type, _entries.Remove(key), _order.Remove(entry.Order), _nextOrder)
            : this;

    public override string ToText() => TextForm.Of(this, message => new InvalidOperationException(message));

    public bool Equals(MapValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Type != other.Type || Count != other.Count)
        {
            return false;
        }

        foreach (var (key, entry) in _entries)
        {
            if (!other._entries.TryGetValue(key, out var otherEntry) || entry.Value != otherEntry.Value)
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var contents = 0;
        foreach (var (key, entry) in _entries)
        {
            contents = unchecked(contents + HashCode.Combine(key, entry.Value));
        }

        return HashCode.Combine(Type, contents);
    }

    private sealed class MapEntry(Value value, long order)
    {
        public Value Value { get; } = value;

        public long Order { get; } = order;
    }
}
