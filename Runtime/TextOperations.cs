using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pyrop.Runtime;

public static class TextOperations
{
    public const int MaxLength = 100_000_000;

    private const int CachedFromLength = 256;

    private static readonly ConditionalWeakTable<string, CharacterMap> MapCache = new();

    public static long LengthOf(string text) => MapOf(text).Count;

    public static string Substring(IBuiltinInvocation invocation, string text, long start, long count)
    {
        var map = MapOf(text);
        if (start < 0 || start > map.Count)
        {
            throw invocation.Fail($"Position {start} is out of range for a {map.Count}-character text, numbered from 0.");
        }

        if (count < 0)
        {
            throw invocation.Fail($"'substring' cannot take a negative count ({count}).");
        }

        if (count > map.Count - start)
        {
            throw invocation.Fail($"Cannot take {count} characters from position {start} of a {map.Count}-character text.");
        }

        return text[map.OffsetOf(start)..map.OffsetOf(start + count)];
    }

    public static string CharAt(IBuiltinInvocation invocation, string text, long index)
    {
        var map = MapOf(text);
        if (index < 0 || index >= map.Count)
        {
            throw invocation.Fail($"Position {index} is out of range for a {map.Count}-character text, numbered from 0.");
        }

        return text[map.OffsetOf(index)..map.OffsetOf(index + 1)];
    }

    public static long Find(string text, string needle) => FindAt(text, needle, MapOf(text), 0);

    public static long FindFrom(IBuiltinInvocation invocation, string text, string needle, long start)
    {
        var map = MapOf(text);
        if (start < 0 || start > map.Count)
        {
            throw invocation.Fail($"Position {start} is out of range for a {map.Count}-character text, numbered from 0.");
        }

        return FindAt(text, needle, map, map.OffsetOf(start));
    }

    private static long FindAt(string text, string needle, CharacterMap map, int from)
    {
        var offset = AlignedMatches(text, needle, map, from).DefaultIfEmpty(-1).First();
        return offset < 0 ? -1 : map.IndexAt(offset);
    }

    public static string Replace(IBuiltinInvocation invocation, string text, string old, string replacement)
    {
        if (old.Length == 0)
        {
            throw invocation.Fail("'replace' cannot search for an empty text.");
        }

        var builder = new StringBuilder();
        var copied = 0;
        foreach (var offset in AlignedMatches(text, old, MapOf(text)))
        {
            if (offset < copied)
            {
                continue;
            }

            if ((long)builder.Length + (offset - copied) + replacement.Length + (text.Length - offset - old.Length) > MaxLength)
            {
                throw TooLong(invocation, "replace");
            }

            builder.Append(text, copied, offset - copied).Append(replacement);
            copied = offset + old.Length;
        }

        return builder.Append(text, copied, text.Length - copied).ToString();
    }

    public static bool StartsWith(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) && MapOf(text).IsBoundary(prefix.Length);

    public static bool EndsWith(string text, string suffix) =>
        text.EndsWith(suffix, StringComparison.Ordinal) && MapOf(text).IsBoundary(text.Length - suffix.Length);

    public static string Trim(string text)
    {
        var map = MapOf(text);
        var first = 0;
        while (first < map.Count && IsWhitespace(text, map, first))
        {
            first++;
        }

        var last = map.Count;
        while (last > first && IsWhitespace(text, map, last - 1))
        {
            last--;
        }

        return text[map.OffsetOf(first)..map.OffsetOf(last)];
    }

    public static string Repeat(IBuiltinInvocation invocation, string text, long count)
    {
        if (count < 0)
        {
            throw invocation.Fail($"'repeat' cannot take a negative count ({count}).");
        }

        if (text.Length == 0 || count == 0)
        {
            return "";
        }

        if (count > MaxLength / text.Length)
        {
            throw TooLong(invocation, "repeat");
        }

        return new StringBuilder(text.Length * (int)count).Insert(0, text, (int)count).ToString();
    }

    public static string Pad(IBuiltinInvocation invocation, string name, string text, long width, string fill, bool left)
    {
        if (LengthOf(fill) != 1)
        {
            throw invocation.Fail($"The fill for '{name}' must be exactly one character, found \"{fill}\".");
        }

        var length = LengthOf(text);
        if (width <= length)
        {
            return text;
        }

        var missing = width - length;
        if (missing > (MaxLength - text.Length) / fill.Length)
        {
            throw TooLong(invocation, name);
        }

        var padding = new StringBuilder(fill.Length * (int)missing).Insert(0, fill, (int)missing).ToString();
        var result = left ? padding + text : text + padding;
        return LengthOf(result) == width
            ? result
            : throw invocation.Fail($"The fill \"{fill}\" for '{name}' merges with the characters next to it, so the result would not be {width} characters long.");
    }

    private static bool IsWhitespace(string text, CharacterMap map, long index)
    {
        for (var offset = map.OffsetOf(index); offset < map.OffsetOf(index + 1); offset++)
        {
            if (!char.IsWhiteSpace(text[offset]))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<int> AlignedMatches(string text, string needle, CharacterMap map, int from = 0)
    {
        while (from <= text.Length)
        {
            var offset = text.IndexOf(needle, from, StringComparison.Ordinal);
            if (offset < 0)
            {
                yield break;
            }

            if (map.IsBoundary(offset) && map.IsBoundary(offset + needle.Length))
            {
                yield return offset;
            }

            from = offset + 1;
        }
    }

    private static CharacterMap MapOf(string text) =>
        text.Length < CachedFromLength ? CharacterMap.Of(text) : MapCache.GetValue(text, CharacterMap.Of);

    private static Exception TooLong(IBuiltinInvocation invocation, string name) =>
        invocation.Fail($"The result of '{name}' would exceed the largest allowed text size.");

    private sealed class CharacterMap(int[]? offsets, int length)
    {
        public long Count => offsets is null ? length : offsets.Length - 1;

        public static CharacterMap Of(string text)
        {
            if (Ascii.IsValid(text) && !text.Contains('\r'))
            {
                return new CharacterMap(null, text.Length);
            }

            var boundaries = new int[text.Length + 1];
            var count = 0;
            var offset = 0;
            while (offset < text.Length)
            {
                offset += StringInfo.GetNextTextElementLength(text, offset);
                boundaries[++count] = offset;
            }

            return new CharacterMap(count == text.Length ? null : boundaries[..(count + 1)], text.Length);
        }

        public int OffsetOf(long index) => offsets is null ? (int)index : offsets[index];

        public int IndexAt(int offset) => offsets is null ? offset : Array.BinarySearch(offsets, offset);

        public bool IsBoundary(int offset) => IndexAt(offset) >= 0;
    }
}
