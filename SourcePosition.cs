namespace Pyrop;

public readonly record struct SourcePosition(int Line, int Column, string? Source = null)
{
    public override string ToString() => $"{Line}:{Column}";
}
