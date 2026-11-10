namespace Pyrop.Parsing;

public static class Suggestions
{
    private const int ShortestComparedWord = 2;
    private const int LongestComparedWord = 40;
    private const int MostChanges = 2;

    public static string? Closest(string word, IEnumerable<string> candidates)
    {
        if (word.Length is < ShortestComparedWord or > LongestComparedWord)
        {
            return null;
        }

        var allowed = Math.Min(MostChanges, Math.Max(1, word.Length / 3));
        var lowered = word.ToLowerInvariant();
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates.Order(StringComparer.Ordinal))
        {
            if (candidate == word || Math.Abs(candidate.Length - word.Length) > allowed)
            {
                continue;
            }

            var distance = Distance(lowered, candidate.ToLowerInvariant());
            if (distance <= allowed && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Distance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (var column = 0; column <= second.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var replaced = previous[column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1);
                current[column] = Math.Min(replaced, Math.Min(previous[column], current[column - 1]) + 1);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
