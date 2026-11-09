using System.Runtime.CompilerServices;

namespace Pyrop.Runtime;

public static class ValueEquality
{
    public const int MaxNesting = 100_000;

    public static bool Equal(Value left, Value right, int nestingLimit, Func<Exception> tooDeep) =>
        Equal(left, right, nestingLimit, 0, tooDeep);

    public static int Hash(Value value, int nestingLimit, Func<Exception> tooDeep) =>
        Hash(value, nestingLimit, 0, tooDeep);

    private static int Hash(Value value, int nestingLimit, int depth, Func<Exception> tooDeep)
    {
        if (value is not (ListValue or MapValue or RecordValue or OptionValue or ChoiceValue { HasData: true }))
        {
            return value.GetHashCode();
        }

        if (depth >= nestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw tooDeep();
        }

        var hash = new HashCode();
        switch (value)
        {
            case ListValue list:
                hash.Add(list.Type);
                foreach (var item in list.Items)
                {
                    hash.Add(Hash(item, nestingLimit, depth + 1, tooDeep));
                }

                break;
            case MapValue map:
                var contents = 0;
                foreach (var (key, stored) in map.Entries)
                {
                    contents = unchecked(contents + HashCode.Combine(
                        Hash(key, nestingLimit, depth + 1, tooDeep), Hash(stored, nestingLimit, depth + 1, tooDeep)));
                }

                hash.Add(map.Type);
                hash.Add(contents);
                break;
            case RecordValue record:
                hash.Add(record.Record);
                foreach (var field in record.Fields)
                {
                    hash.Add(Hash(field, nestingLimit, depth + 1, tooDeep));
                }

                break;
            case ChoiceValue choice:
                hash.Add(choice.Choice);
                hash.Add(choice.Index);
                foreach (var data in choice.Data)
                {
                    hash.Add(Hash(data, nestingLimit, depth + 1, tooDeep));
                }

                break;
            case OptionValue option:
                hash.Add(option.Type);
                hash.Add(option.Content is { } content ? Hash(content, nestingLimit, depth + 1, tooDeep) : 0);
                break;
        }

        return hash.ToHashCode();
    }

    private static bool Equal(Value left, Value right, int nestingLimit, int depth, Func<Exception> tooDeep)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is not (ListValue or MapValue or RecordValue or OptionValue or ChoiceValue { HasData: true }))
        {
            return left.Equals(right);
        }

        if (depth >= nestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw tooDeep();
        }

        switch (left, right)
        {
            case (ListValue first, ListValue second):
                if (first.Type != second.Type || first.Count != second.Count)
                {
                    return false;
                }

                foreach (var (item, other) in first.Items.Zip(second.Items))
                {
                    if (!Equal(item, other, nestingLimit, depth + 1, tooDeep))
                    {
                        return false;
                    }
                }

                return true;
            case (MapValue first, MapValue second):
                if (first.Type != second.Type || first.Count != second.Count)
                {
                    return false;
                }

                foreach (var (key, value) in first.Entries)
                {
                    if (second.Find(key) is not { } other || !Equal(value, other, nestingLimit, depth + 1, tooDeep))
                    {
                        return false;
                    }
                }

                return true;
            case (RecordValue first, RecordValue second):
                if (first.Record != second.Record)
                {
                    return false;
                }

                for (var index = 0; index < first.Fields.Length; index++)
                {
                    if (!Equal(first.Fields[index], second.Fields[index], nestingLimit, depth + 1, tooDeep))
                    {
                        return false;
                    }
                }

                return true;
            case (ChoiceValue first, ChoiceValue second):
                if (first.Choice != second.Choice || first.Index != second.Index)
                {
                    return false;
                }

                for (var index = 0; index < first.Data.Length; index++)
                {
                    if (!Equal(first.Data[index], second.Data[index], nestingLimit, depth + 1, tooDeep))
                    {
                        return false;
                    }
                }

                return true;
            case (OptionValue first, OptionValue second):
                if (first.Type != second.Type)
                {
                    return false;
                }

                return (first.Content, second.Content) switch
                {
                    (null, null) => true,
                    ({ } one, { } other) => Equal(one, other, nestingLimit, depth + 1, tooDeep),
                    _ => false,
                };
            default:
                return false;
        }
    }
}
