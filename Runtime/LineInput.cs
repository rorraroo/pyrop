namespace Pyrop.Runtime;

public sealed class LineInput(TextReader reader)
{
    private string? _pending;
    private bool _hasPending;

    public string? ReadLine()
    {
        if (!_hasPending)
        {
            return reader.ReadLine();
        }

        var line = _pending;
        _pending = null;
        _hasPending = false;
        return line;
    }

    public bool HasEnded()
    {
        if (!_hasPending)
        {
            _pending = reader.ReadLine();
            _hasPending = true;
        }

        return _pending is null;
    }
}
