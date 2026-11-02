using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public abstract record Statement(SourcePosition Position);

public sealed record DefStatement(PyropType Type, string Name, Expression Value, SourcePosition Position) : Statement(Position);

public sealed record SetStatement(string Name, Expression Value, SourcePosition Position) : Statement(Position);

public sealed record IfStatement(Expression Condition, Statement Then, Statement? Else, SourcePosition Position) : Statement(Position);

public sealed record IfSomeStatement(
    PyropType ValueType, string Name, Expression Option, Statement Then, Statement? Else, SourcePosition Position) : Statement(Position);

public sealed record MatchBranch(ChoiceVariant Variant, IReadOnlyList<string> Names, Statement Body, SourcePosition Position, string? Module = null);

public sealed record MatchStatement(Expression Value, IReadOnlyList<MatchBranch> Branches, SourcePosition Position) : Statement(Position);

public sealed record WhileStatement(Expression Condition, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record ForStatement(PyropType VariableType, string Variable, Expression List, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record BreakStatement(SourcePosition Position) : Statement(Position);

public sealed record ContinueStatement(SourcePosition Position) : Statement(Position);

public sealed record FunctionStatement(FunctionSignature Signature, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record ReturnStatement(Expression? Value, SourcePosition Position) : Statement(Position);

public sealed record WriteStatement(Expression Value, bool AppendNewLine, bool ToStandardError, SourcePosition Position) : Statement(Position);

public sealed record ExitStatement(Expression Code, SourcePosition Position) : Statement(Position);

public sealed record WithStyleStatement(Expression Style, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record BlockStatement(IReadOnlyList<Statement> Statements, SourcePosition Position) : Statement(Position);

public sealed record CallStatement(Expression Call, SourcePosition Position) : Statement(Position);

public sealed record UseStatement(string Name, string Path, SourcePosition Position) : Statement(Position);

public sealed record ImportStatement(string Module, IReadOnlyList<string> Names, SourcePosition Position) : Statement(Position);

public sealed record RecordStatement(RecordType Record, SourcePosition Position) : Statement(Position);

public sealed record ChoiceStatement(ChoiceType Choice, SourcePosition Position) : Statement(Position);

public sealed record SyntaxTree(IReadOnlyList<Statement> Statements)
{
    public IEnumerable<FunctionStatement> Functions => Statements.OfType<FunctionStatement>();

    public IEnumerable<DefStatement> Globals => Statements.OfType<DefStatement>();
}

public sealed record ModuleTree(Module Module, SyntaxTree Tree);

public sealed record ParsedProgram(IReadOnlyList<ModuleTree> Modules, ModuleTree Main)
{
    public IEnumerable<ModuleTree> All => Modules.Append(Main);
}
