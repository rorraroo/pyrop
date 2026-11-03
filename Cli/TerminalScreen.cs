namespace Pyrop.Cli;

public interface ITerminalScreen
{
    (int Width, int Height) Size { get; }

    bool KeyAvailable { get; }

    ConsoleKeyInfo ReadKey();

    void Wait(int milliseconds);
}

public sealed class ConsoleScreen : ITerminalScreen
{
    public static ConsoleScreen Instance { get; } = new();

    public (int Width, int Height) Size
    {
        get
        {
            try
            {
                return (Console.WindowWidth, Console.WindowHeight);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or PlatformNotSupportedException)
            {
                return (80, 24);
            }
        }
    }

    public bool KeyAvailable
    {
        get
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);
}

public static class KeyNames
{
    public static string? Of(ConsoleKeyInfo key)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);
        if (alt && !control)
        {
            return null;
        }

        var named = key.Key switch
        {
            ConsoleKey.UpArrow => "up",
            ConsoleKey.DownArrow => "down",
            ConsoleKey.LeftArrow => "left",
            ConsoleKey.RightArrow => "right",
            ConsoleKey.Enter => "enter",
            ConsoleKey.Escape => "escape",
            ConsoleKey.Spacebar => "space",
            ConsoleKey.Tab => "tab",
            ConsoleKey.Backspace => "backspace",
            ConsoleKey.Delete => "delete",
            ConsoleKey.Insert => "insert",
            ConsoleKey.Home => "home",
            ConsoleKey.End => "end",
            ConsoleKey.PageUp => "page_up",
            ConsoleKey.PageDown => "page_down",
            >= ConsoleKey.F1 and <= ConsoleKey.F12 => $"f{key.Key - ConsoleKey.F1 + 1}",
            _ => null,
        };

        if (named is not null)
        {
            return control || alt ? null : named;
        }

        if (control && !alt && key.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            return $"control_{(char)('a' + (key.Key - ConsoleKey.A))}";
        }

        return key.KeyChar != '\0' && !char.IsControl(key.KeyChar) ? key.KeyChar.ToString() : null;
    }
}
