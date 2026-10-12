using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public abstract record Statement(SourcePosition Position);

public sealed record DefStatement(PyropType Type, string Name, Expression Value, SourcePosition Position) : Statement(Position);

public sealed record SetStatement(string Name, Expression Value, SourcePosition Position) : Statement(Position);

public sealed record IfStatement(Expression Condition, Statement Then, Statement? Else, SourcePosition Position) : Statement(Position);

public sealed record WhileStatement(Expression Condition, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record BreakStatement(SourcePosition Position) : Statement(Position);

public sealed record ContinueStatement(SourcePosition Position) : Statement(Position);

public sealed record FunctionStatement(FunctionSignature Signature, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record ReturnStatement(Expression? Value, SourcePosition Position) : Statement(Position);

public sealed record WriteStatement(Expression Value, bool AppendNewLine, SourcePosition Position) : Statement(Position);

public sealed record ExitStatement(Expression Code, SourcePosition Position) : Statement(Position);

public sealed record WithStyleStatement(Expression Style, Statement Body, SourcePosition Position) : Statement(Position);

public sealed record BlockStatement(IReadOnlyList<Statement> Statements, SourcePosition Position) : Statement(Position);

public sealed record CallStatement(Expression Call, SourcePosition Position) : Statement(Position);

public sealed record IncludeStatement(string Path, SyntaxTree? Included, SourcePosition Position) : Statement(Position);

public sealed record SyntaxTree(IReadOnlyList<Statement> Statements)
{
    public IEnumerable<Statement> TopLevel =>
        Statements.SelectMany(statement => statement is IncludeStatement { Included: { } included }
            ? included.TopLevel
            : [statement]);

    public IEnumerable<FunctionStatement> Functions => TopLevel.OfType<FunctionStatement>();

    public IEnumerable<DefStatement> Globals => TopLevel.OfType<DefStatement>();
}
