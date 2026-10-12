using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class Keywords
{
    public const string Def = "def";
    public const string Set = "set";
    public const string If = "if";
    public const string Else = "else";
    public const string While = "while";
    public const string Break = "break";
    public const string Continue = "continue";
    public const string Block = "block";
    public const string End = "end";
    public const string Function = "function";
    public const string Return = "return";
    public const string Write = "write";
    public const string WriteLine = "write_line";
    public const string Convert = "convert";
    public const string Include = "include";
    public const string Exit = "exit";
    public const string True = "true";
    public const string False = "false";

    private static readonly HashSet<string> Reserved =
    [
        Def, Set, If, Else, While, Break, Continue, Block, End, Function, Return, Write, WriteLine,
        Convert, Include, Exit, True, False,
        .. PyropTypes.Names,
        .. Builtins.All.Keys,
    ];

    private static readonly HashSet<string> Statements =
    [
        Def, Set, If, Else, While, Break, Continue, Block, End, Function, Return, Write, WriteLine, Include, Exit,
    ];

    public static bool IsReserved(string word) => Reserved.Contains(word);

    public static bool IsStatement(string word) => Statements.Contains(word);

    public static string? UnderscoredSpelling(string word) =>
        Reserved.FirstOrDefault(reserved => reserved != word && reserved.Replace("_", "") == word);

    public static string UnderscoreHint(string word, string spelling) =>
        $"'{word}' is not a Pyrop word; did you mean '{spelling}'? Names made of several words are joined with underscores.";
}
