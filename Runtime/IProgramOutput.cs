namespace Pyrop.Runtime;

public interface IProgramOutput
{
    void Write(string text, OutputStyle style);

    void WriteError(string text, OutputStyle style);

    void WritePrompt(string prompt, string? hint, OutputStyle style);

    void LineRead();

    void EnsureLineStart();

    int? TryChoose(string prompt, IReadOnlyList<string> entries, OutputStyle style);

    bool TryWriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, OutputStyle style);

    void BeforeLineRead()
    {
    }

    bool TryShow(ViewNode view, OutputStyle style) => false;

    bool TryWriteView(ViewNode view, OutputStyle style) => false;

    bool TryReadKey(int? milliseconds, out string? key)
    {
        key = null;
        return false;
    }
}
