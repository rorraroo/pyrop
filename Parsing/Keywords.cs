using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class Keywords
{
    public const string Def = "def";
    public const string Set = "set";
    public const string If = "if";
    public const string Else = "else";
    public const string While = "while";
    public const string For = "for";
    public const string In = "in";
    public const string Break = "break";
    public const string Continue = "continue";
    public const string Block = "block";
    public const string End = "end";
    public const string Function = "function";
    public const string Return = "return";
    public const string Write = "write";
    public const string WriteLine = "write_line";
    public const string WriteError = "write_error";
    public const string WriteErrorLine = "write_error_line";
    public const string Convert = "convert";
    public const string ListOf = "list_of";
    public const string MapOf = "map_of";
    public const string VariantsOf = "variants_of";
    public const string Exit = "exit";
    public const string Styled = "styled";
    public const string Use = "use";
    public const string Import = "import";
    public const string Export = "export";
    public const string From = "from";
    public const string Library = "library";
    public const string Record = "record";
    public const string Choice = "choice";
    public const string The = "the";
    public const string Of = "of";
    public const string Copy = "copy";
    public const string With = "with";
    public const string Attempt = "attempt";
    public const string IfSome = "if_some";
    public const string Match = "match";
    public const string When = "when";
    public const string None = "none";
    public const string True = "true";
    public const string False = "false";

    private static readonly Dictionary<string, string> Retired = new()
    {
        ["empty"] = "write an empty list as list_of int end and an empty map as map_of string int end",
        ["item_at"] = "use get <list> <index>",
        ["value_of"] = "use get <map> <key>",
        ["char_at"] = "use get <text> <position>",
        ["contains"] = "use has <list> <item>",
        ["has_key"] = "use has <map> <key>",
        ["remove_at"] = "use remove <list> <index>",
        ["remove_key"] = "use remove <map> <key>",
        ["replace_at"] = "use put <list> <index> <item>",
        ["negate"] = "write subtract 0 <number>, or subtract 0.0 <number> for a float",
        ["get_field"] = "write the <field> of <record>",
        ["with_field"] = "write copy <record> with <field> <value>",
        ["with_style"] = "it is now called styled, as in styled \"warning\" write_line \"Careful\"",
    };

    public static IReadOnlyList<string> LanguageWords { get; } =
    [
        Def, Set, If, Else, While, For, In, Break, Continue, Block, End, Function, Return, Write, WriteLine,
        WriteError, WriteErrorLine, Convert, ListOf, MapOf, VariantsOf, Exit, Styled, Use, Import, Export, From, Library, Record, Choice,
        The, Of, Copy, With, Attempt, IfSome, Match, When, None, True, False,
    ];

    private static readonly HashSet<string> Reserved =
    [
        .. LanguageWords,
        .. PyropTypes.Names,
        .. Builtins.All.Keys,
        .. Retired.Keys,
    ];

    private static readonly HashSet<string> Statements =
    [
        Def, Set, If, Else, While, For, Break, Continue, Block, End, Function, Return, Write, WriteLine,
        WriteError, WriteErrorLine, Exit, Styled, Use, Import, Export, Record, Choice, IfSome, Match, When,
    ];

    public static bool IsReserved(string word) => Reserved.Contains(word);

    public static bool IsStatement(string word) => Statements.Contains(word);

    public static bool StartsDefinition(string word) => word is Function or Record or Choice;

    public static string? RetiredHint(string word) =>
        Retired.TryGetValue(word, out var hint) ? $"'{word}' is no longer part of Pyrop; {hint}." : null;

    public static string? UnderscoredSpelling(string word) =>
        Reserved.FirstOrDefault(reserved => reserved != word && !Retired.ContainsKey(reserved) && reserved.Replace("_", "") == word);

    public static string UnderscoreHint(string word, string spelling) =>
        $"'{word}' is not a Pyrop word; did you mean '{spelling}'? Names made of several words are joined with underscores.";
}
