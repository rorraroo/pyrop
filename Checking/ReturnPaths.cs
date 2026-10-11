using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Checking;

public static class ReturnPaths
{
    public static bool AlwaysReturns(Statement statement) => statement switch
    {
        ReturnStatement => true,
        BlockStatement block => block.Statements.Any(AlwaysReturns),
        IfStatement { Else: { } otherwise } conditional => AlwaysReturns(conditional.Then) && AlwaysReturns(otherwise),
        WhileStatement { Condition: LiteralExpression { Value: BoolValue { Value: true } } } loop => !BreaksOut(loop.Body),
        _ => false,
    };

    private static bool BreaksOut(Statement statement) => statement switch
    {
        BreakStatement => true,
        BlockStatement block => block.Statements.Any(BreaksOut),
        IfStatement conditional => BreaksOut(conditional.Then) || (conditional.Else is { } otherwise && BreaksOut(otherwise)),
        _ => false,
    };
}
