namespace Pyrop;

public enum ErrorStage
{
    Lex,
    Parse,
    Type,
    Runtime,
    Internal,
}

public sealed record PyropError(ErrorStage Stage, string Message, SourcePosition Position);
