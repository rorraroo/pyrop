namespace Pyrop.Runtime;

public sealed record OptionValue : Value
{
    private OptionValue(PyropType type, Value? content)
    {
        Type = type;
        Content = content;
    }

    public static OptionValue Some(Value content) => new(PyropTypes.OptionOf(content.Type), content);

    public static OptionValue None(PyropType optionType) => new(optionType, null);

    public override PyropType Type { get; }

    public Value? Content { get; }

    public override string ToText() => TextForm.Of(this, message => new InvalidOperationException(message));

    public bool Equals(OptionValue? other) =>
        ReferenceEquals(this, other)
        || (other is not null && Type == other.Type && Equals(Content, other.Content));

    public override int GetHashCode() => HashCode.Combine(Type, Content);
}
