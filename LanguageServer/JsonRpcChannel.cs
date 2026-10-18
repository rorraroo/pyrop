using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Pyrop.LanguageServer;

public sealed class JsonRpcChannel(Stream input, Stream output)
{
    private const int MaximumHeaderLine = 8192;
    private const int MaximumMessage = 64 * 1024 * 1024;

    private readonly Stream _input = new BufferedStream(input);
    private readonly object _writing = new();

    public byte[]? Read()
    {
        int? length = null;
        while (true)
        {
            if (ReadHeaderLine() is not { } line)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (length is not null)
                {
                    break;
                }

                continue;
            }

            var colon = line.IndexOf(':');
            if (colon > 0
                && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out var value)
                && value >= 0)
            {
                length = value;
            }
        }

        if (length > MaximumMessage)
        {
            return null;
        }

        var body = new byte[length.Value];
        return _input.ReadAtLeast(body, body.Length, throwOnEndOfStream: false) == body.Length ? body : null;
    }

    public void Write(Action<Utf8JsonWriter> writeMessage)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writeMessage(writer);
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {buffer.WrittenCount}\r\n\r\n");
        lock (_writing)
        {
            try
            {
                output.Write(header);
                output.Write(buffer.WrittenSpan);
                output.Flush();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private string? ReadHeaderLine()
    {
        var line = new StringBuilder();
        while (true)
        {
            var next = _input.ReadByte();
            if (next < 0)
            {
                return null;
            }

            if (next == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (line.Length < MaximumHeaderLine)
            {
                line.Append((char)next);
            }
        }
    }
}
