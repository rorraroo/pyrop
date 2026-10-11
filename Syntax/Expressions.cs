using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public abstract record Expression(SourcePosition Position);

public sealed record LiteralExpression(Value Value, SourcePosition Position) : Expression(Position);

public sealed record VariableExpression(string Name, SourcePosition Position) : Expression(Position);

public sealed record BuiltinCallExpression(BuiltinOperation Operation, IReadOnlyList<Expression> Arguments, SourcePosition Position)
    : Expression(Position);

public sealed record FunctionCallExpression(FunctionSignature Function, IReadOnlyList<Expression> Arguments, SourcePosition Position)
    : Expression(Position);

public sealed record ConvertExpression(PyropType Target, Expression Operand, SourcePosition Position) : Expression(Position);
