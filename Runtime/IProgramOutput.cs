namespace Pyrop.Runtime;

public interface IProgramOutput
{
    void Write(string text, OutputStyle style);

    void WritePrompt(string prompt, string? hint, OutputStyle style);

    void LineRead();
}
