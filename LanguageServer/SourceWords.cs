using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.LanguageServer;

internal static class SourceWords
{
    public static int? WordAt(IReadOnlyList<Token> tokens, int line, int column)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.Word && token.Position.Line == line
                && token.Position.Column <= column && column < token.Position.Column + token.Text.Length)
            {
                return index;
            }
        }

        return null;
    }

    public static IEnumerable<RecordType> RecordsOf(ModuleNamespace names) =>
        names.Records.Values.Concat(names.Imports.Values.Select(import => import.Record).OfType<RecordType>());

    public static IEnumerable<ChoiceType> ChoicesOf(ModuleNamespace names) =>
        names.Choices.Values.Concat(names.Imports.Values.Select(import => import.Choice).OfType<ChoiceType>());

    public static string Overloads(BuiltinOperation operation, ModuleNamespace names, BuiltinOverload? chosen = null)
    {
        var ordered = chosen is null ? operation.Overloads : operation.Overloads.OrderBy(overload => overload == chosen ? 0 : 1).ToList();
        return string.Join("\n", ordered.Select(overload =>
            string.Join(" ", overload.Parameters.Select(parameter => TreePrinter.Type(parameter, names)).Prepend(operation.Name))
            + $" → {TreePrinter.Type(overload.Result, names)}"));
    }

    public static string VariantLine(ChoiceVariant variant, ModuleNamespace names) =>
        string.Join(" ", variant.Data.Select(type => TreePrinter.Type(type, names)).Prepend(variant.Name))
        + $" → {TreePrinter.Type(variant.Choice, names)}";

    public static string Code(string text) => $"```pyrop\n{text}\n```";
}
