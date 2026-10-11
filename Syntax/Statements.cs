using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public abstract record Statement(SourcePosition Position);

public sealed record DefStatement(PyropType Type, string Name, SourcePosition Position) : Statement(Position);

public sealed record SetStatement(string Name, Expression Value, SourcePosition Position) : Statement(Position);

public sealed record IfStatement(Expression Condition, Statement Then, Statement? Else, SourcePosition Position) : Statement(Position);

public sealed record WhileStatement(Expression Condition, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record BreakStatement(SourcePosition Position) : Statement(Position);

public sealed record ContinueStatement(SourcePosition Position) : Statement(Position);

public sealed record FunctionStatement(FunctionSignature Signature, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record ReturnStatement(Expression? Value, SourcePosition Position) : Statement(Position);

public sealed record WriteStatement(Expression Value, bool AppendNewLine, SourcePosition Position) : Statement(Position);

public sealed record BlockStatement(IReadOnlyList<Statement> Statements, SourcePosition Position) : Statement(Position);

public sealed record CallStatement(Expression Call, SourcePosition Position) : Statement(Position);

public sealed record SyntaxTree(IReadOnlyList<Statement> Statements)
{
    public IEnumerable<FunctionStatement> Functions => Statements.OfType<FunctionStatement>();

    public IEnumerable<DefStatement> Globals => Statements.OfType<DefStatement>();
}
