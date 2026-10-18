using Pyrop.Lexing;

namespace Pyrop.Parsing;

public static class ChoiceDeclarations
{
    public static bool CanStartChoice(TokenCursor cursor)
    {
        var name = cursor.Current;
        if (name.Kind != TokenKind.Word || Keywords.IsReserved(name.Text))
        {
            return false;
        }

        var next = cursor.Peek(1);
        return RecordDeclarations.IsTypeName(name.Text)
            || (next.Kind == TokenKind.Word && (next.IsWord(Keywords.End) || !Keywords.IsStatement(next.Text)));
    }

    public static Token ReadName(TokenCursor cursor) => RecordDeclarations.ReadName(cursor, Keywords.Choice, "Direction");

    public static IReadOnlyList<Token> ReadVariants(TokenCursor cursor, Token start, string choice)
    {
        var variants = new List<Token>();
        var names = new HashSet<string>();
        while (!cursor.Current.IsWord(Keywords.End))
        {
            var token = cursor.Current;
            if (cursor.IsAtEnd)
            {
                throw cursor.Error($"Expected '{Keywords.End}' to close the choice started at {start.Position}.");
            }

            if (token.Kind != TokenKind.Word
                || Keywords.IsStatement(token.Text)
                || token.IsWord(Keywords.True)
                || token.IsWord(Keywords.False))
            {
                throw cursor.Error(
                    $"Expected a variant name or '{Keywords.End}' to close the choice started at {start.Position}, found {token.Describe()}.");
            }

            if (Keywords.IsReserved(token.Text))
            {
                throw cursor.Error($"'{token.Text}' is a reserved word and cannot be used as a variant name.");
            }

            if (!names.Add(token.Text))
            {
                throw cursor.Error($"Variant '{token.Text}' is declared twice in choice '{choice}'.");
            }

            variants.Add(cursor.Advance());
        }

        if (variants.Count == 0)
        {
            throw cursor.Error(
                $"Choice '{choice}' needs at least one variant, such as {Keywords.Choice} {choice} first {Keywords.End}.");
        }

        cursor.Advance();
        return variants;
    }
}
