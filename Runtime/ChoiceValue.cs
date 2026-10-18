namespace Pyrop.Runtime;

public sealed record ChoiceValue(ChoiceType Choice, int Index) : Value
{
    public override PyropType Type => Choice;

    public string Name => Choice.Variants[Index].Name;

    public override string ToText() => Name;
}
