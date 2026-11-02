using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Checking;

public static class ReturnPaths
{
    public static bool AlwaysReturns(Statement statement) => statement switch
    {
        ReturnStatement or ExitStatement => true,
        BlockStatement block => block.Statements.Any(AlwaysReturns),
        IfStatement { Else: { } otherwise } conditional => AlwaysReturns(conditional.Then) && AlwaysReturns(otherwise),
        IfSomeStatement { Else: { } otherwise } unwrap => AlwaysReturns(unwrap.Then) && AlwaysReturns(otherwise),
        MatchStatement match => match.Branches.All(branch => AlwaysReturns(branch.Body)),
        WithStyleStatement styled => AlwaysReturns(styled.Body),
        WhileStatement { Condition: LiteralExpression { Value: BoolValue { Value: true } } } loop => !BreaksOut(loop.Body),
        _ => false,
    };

    private static bool BreaksOut(Statement statement) => statement switch
    {
        BreakStatement => true,
        BlockStatement block => block.Statements.Any(BreaksOut),
        IfStatement conditional => BreaksOut(conditional.Then) || (conditional.Else is { } otherwise && BreaksOut(otherwise)),
        IfSomeStatement unwrap => BreaksOut(unwrap.Then) || (unwrap.Else is { } otherwise && BreaksOut(otherwise)),
        MatchStatement match => match.Branches.Any(branch => BreaksOut(branch.Body)),
        WithStyleStatement styled => BreaksOut(styled.Body),
        _ => false,
    };
}
