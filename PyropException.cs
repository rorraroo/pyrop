namespace Pyrop;

public sealed class PyropException : Exception
{
    public PyropException(IReadOnlyList<PyropError> errors, bool isIncompleteInput = false)
        : base(string.Join(Environment.NewLine, errors.Select(error => $"{error.Position}: {error.Message}")))
    {
        Errors = errors;
        IsIncompleteInput = isIncompleteInput;
    }

    public PyropException(ErrorStage stage, string message, SourcePosition position, bool isIncompleteInput = false)
        : this([new PyropError(stage, message, position)], isIncompleteInput)
    {
    }

    public IReadOnlyList<PyropError> Errors { get; }

    public bool IsIncompleteInput { get; }
}
