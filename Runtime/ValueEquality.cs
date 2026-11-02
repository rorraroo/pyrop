using System.Runtime.CompilerServices;

namespace Pyrop.Runtime;

public static class ValueEquality
{
    public const int MaxNesting = 100_000;

    public static bool Equal(Value left, Value right, int nestingLimit, Func<Exception> tooDeep) =>
        Equal(left, right, nestingLimit, 0, tooDeep);

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
