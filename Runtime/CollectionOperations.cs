using System.Collections.Immutable;
using System.Globalization;

namespace Pyrop.Runtime;

public static class CollectionOperations
{
    public const int MaxCount = 10_000_000;

    public static Value ItemAt(IBuiltinInvocation invocation, ListValue list, long index) =>
        list.Items[CheckIndex(invocation, list, index)];

    public static ListValue Push(IBuiltinInvocation invocation, ListValue list, Value item)
    {
        CheckRoom(list.Count + 1, invocation.CollectionLimit, "list", invocation.Fail);
        return list.With(list.Items.Add(item));
    }

    public static ListValue ReplaceAt(IBuiltinInvocation invocation, ListValue list, long index, Value item) =>
        list.With(list.Items.SetItem(CheckIndex(invocation, list, index), item));

    public static ListValue RemoveAt(IBuiltinInvocation invocation, ListValue list, long index) =>
        list.With(list.Items.RemoveAt(CheckIndex(invocation, list, index)));

    public static bool Contains(IBuiltinInvocation invocation, ListValue list, Value item) =>
        list.Items.Any(candidate => ValueEquality.Equal(candidate, item, invocation.NestingLimit, () => TooDeep(invocation, "has")));

    public static Exception TooDeep(IBuiltinInvocation invocation, string operation) =>
        invocation.FailAtLimit($"'{operation}' cannot compare values nested this deeply.");

    public static Value ValueOf(IBuiltinInvocation invocation, MapValue map, Value key) =>
        map.Find(key) ?? throw MissingKey(invocation, key);

    public static MapValue Put(IBuiltinInvocation invocation, MapValue map, Value key, Value value)
    {
        if (!map.ContainsKey(key))
        {
            CheckRoom(map.Count + 1, invocation.CollectionLimit, "map", invocation.Fail);
        }

        return map.Put(key, value);
    }

    public static MapValue RemoveKey(IBuiltinInvocation invocation, MapValue map, Value key) =>
        map.ContainsKey(key) ? map.Remove(key) : throw MissingKey(invocation, key);

    public static ListValue KeysOf(MapValue map) =>
        new(PyropTypes.ListOf(map.KeyType), map.Keys.ToImmutableList());

    public static ListValue PositionsOf(ListValue list) =>
        new(PyropTypes.ListOf(PyropType.Int), Enumerable.Range(0, list.Count).Select(index => (Value)new IntValue(index)).ToImmutableList());

    public static ListValue Slice(IBuiltinInvocation invocation, ListValue list, long start, long count)
    {
        if (start < 0 || start > list.Count)
        {
            throw invocation.Fail($"Position {start} is out of range for {Describe(list.Count)}, numbered from 0.");
        }

        if (count < 0)
        {
            throw invocation.Fail($"'slice' cannot take a negative count ({count}).");
        }

        if (count > list.Count - start)
        {
            var items = count == 1 ? "1 item" : $"{count} items";
            throw invocation.Fail($"Cannot take {items} from position {start} of {Describe(list.Count)}.");
        }

        return list.With(list.Items.GetRange((int)start, (int)count));
    }

    public static ListValue ListOf(PyropType elementType, IReadOnlyList<Value> items, int limit, Func<string, Exception> fail)
    {
        CheckRoom(items.Count, limit, "list", fail);
        return new ListValue(PyropTypes.ListOf(elementType), items.ToImmutableList());
    }

    public static MapValue MapOf(PyropType type, IEnumerable<(Value Key, Value Value)> entries, int limit, Func<string, Exception> fail)
    {
        var map = MapValue.Empty(type);
        foreach (var (key, value) in entries)
        {
            if (!map.ContainsKey(key))
            {
                CheckRoom(map.Count + 1, limit, "map", fail);
            }

            map = map.Put(key, value);
        }

        return map;
    }

    public static void CheckRoom(int count, int limit, string kind, Func<string, Exception> fail)
    {
        if (count > limit)
        {
            throw fail($"A {kind} cannot hold more than {limit.ToString("N0", CultureInfo.InvariantCulture)} items.");
        }
    }

    private static int CheckIndex(IBuiltinInvocation invocation, ListValue list, long index)
    {
        if (index < 0 || index >= list.Count)
        {
            var size = list.Count == 0 ? "an empty list" : $"{Describe(list.Count)}, numbered from 0";
            throw invocation.Fail($"List index {index} is out of range for {size}.");
        }

        return (int)index;
    }

    private static string Describe(int count) => count switch
    {
        0 => "an empty list",
        1 => "a list of 1 item",
        _ => $"a list of {count} items",
    };

    private static Exception MissingKey(IBuiltinInvocation invocation, Value key) =>
        invocation.Fail($"The map has no key {TextForm.OfItem(key)}.");
}
