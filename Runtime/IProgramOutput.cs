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
}
