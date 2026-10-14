namespace Pyrop.Runtime;

public sealed class LineInput(TextReader reader)
{
    private const char ByteOrderMark = '﻿';

    private string? _pending;
    private bool _hasPending;
    private bool _started;

    public string? ReadLine()
    {
        if (!_hasPending)
        {
            return ReadFromReader();
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
            _pending = ReadFromReader();
            _hasPending = true;
        }

        return _pending is null;
    }

    private string? ReadFromReader()
    {
        var line = reader.ReadLine();
        if (!_started)
        {
            _started = true;
            if (line is [ByteOrderMark, ..])
            {
                return line[1..];
            }
        }

        return line;
    }
}
