using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class ChoiceDeclarations
{
    public static bool CanStartChoice(TokenCursor cursor)
    {
        var name = cursor.Current;
        if (name.Kind != TokenKind.Word || (Keywords.IsReserved(name.Text) && !Keywords.IsReservedForLater(name.Text)))
        {
            return false;
        }

        var next = cursor.Peek(1);
        return next.Kind == TokenKind.EndOfInput
            || (next.Kind == TokenKind.Word && (next.IsWord(Keywords.End) || !Keywords.IsStatement(next.Text)));
    }

    public static Token ReadName(TokenCursor cursor) => RecordDeclarations.ReadName(cursor, Keywords.Choice);

    public sealed record ReadVariant(Token Name, IReadOnlyList<PyropType> Data, int Arity)
    {
        public VariantDeclaration Declaration => new(Name.Text, Name.Position, Arity);
    }

    public static IReadOnlyList<ReadVariant> ReadVariants(TokenCursor cursor, Token start, string choice, KnownTypes types) =>
        ReadVariants(cursor, start, choice, current => StartsKnownType(current, types), current =>
        {
            SkipKnownType(current, types);
            return null;
        });

    public static IReadOnlyList<ReadVariant> ReadVariants(TokenCursor cursor, Token start, string choice, ModuleNamespace names) =>
        ReadVariants(
            cursor,
            start,
            choice,
            current => TypeReader.StartsValueType(current, names),
            current => TypeReader.ReadValueType(current, names, "variant's data"));

    private static bool StartsKnownType(TokenCursor cursor, KnownTypes types)
    {
        var token = cursor.Current;
        if (token.Kind != TokenKind.Word)
        {
            return false;
        }

        return token.IsWord(Keywords.From)
            ? cursor.Peek(1).Kind == TokenKind.Word && cursor.Peek(2).Kind == TokenKind.Word
                && (types.IsQualifiedType(cursor.Peek(1).Text, cursor.Peek(2).Text) || types.Names.Failures.OfName(cursor.Peek(1).Text) is not null)
            : types.IsTypeWord(token.Text) || types.Names.Failures.OfName(token.Text) is not null;
    }

    private static void SkipKnownType(TokenCursor cursor, KnownTypes types)
    {
        if (cursor.Current.IsWord(Keywords.From))
        {
            cursor.Advance();
            cursor.Advance();
            cursor.Advance();
            return;
        }

        var token = cursor.Advance();
        if (!PyropTypes.ValueConstructors.TryGetValue(token.Text, out var constructor))
        {
            return;
        }

        for (var argument = 0; argument < constructor.Arity && (StartsKnownType(cursor, types) || cursor.Current.IsWord(PyropType.Void.Name)); argument++)
        {
            SkipKnownType(cursor, types);
        }
    }

    private static IReadOnlyList<ReadVariant> ReadVariants(
        TokenCursor cursor, Token start, string choice, Func<TokenCursor, bool> startsType, Func<TokenCursor, PyropType?> readType)
    {
        var variants = new List<ReadVariant>();
        var names = new HashSet<string>();
        Token? current = null;
        var data = new List<PyropType>();
        var arity = 0;
        while (!cursor.Current.IsWord(Keywords.End))
        {
            var token = cursor.Current;
            if (cursor.IsAtEnd)
            {
                throw cursor.Error($"Expected '{Keywords.End}' to close the choice started at {start.Position.Spoken}.");
            }

            if (token.IsWord(PyropType.Void.Name))
            {
                throw cursor.Error(
                    current is null
                        ? $"'{token.Text}' is a reserved word and cannot be used as a variant name."
                        : $"The variant '{current.Text}' cannot carry void; void has no values.");
            }

            if (current is not null && startsType(cursor))
            {
                if (readType(cursor) is { } type)
                {
                    data.Add(type);
                }

                arity++;
                continue;
            }

            if (token.Kind != TokenKind.Word
                || Keywords.IsStatement(token.Text)
                || token.IsWord(Keywords.True)
                || token.IsWord(Keywords.False))
            {
                throw cursor.Error(
                    $"Expected a variant name or '{Keywords.End}' to close the choice started at {start.Position.Spoken}, found {token.Describe()}.");
            }

            if (Keywords.IsReserved(token.Text))
            {
                throw cursor.Error(Keywords.NotAName(token.Text, "variant name"));
            }

            if (!names.Add(token.Text))
            {
                throw cursor.Error($"Variant '{token.Text}' is declared twice in choice '{choice}'.");
            }

            if (current is not null)
            {
                variants.Add(new ReadVariant(current, data, arity));
            }

            current = cursor.Advance();
            data = [];
            arity = 0;
        }

        if (current is null)
        {
            throw cursor.Error(
                $"Choice '{choice}' needs at least one variant, such as {Keywords.Choice} {choice} first {Keywords.End}.");
        }

        variants.Add(new ReadVariant(current, data, arity));
        cursor.Advance();
        return variants;
    }
}
