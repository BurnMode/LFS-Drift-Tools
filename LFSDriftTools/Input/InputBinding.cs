using System.Windows.Forms;

public enum InputKind { None, Keyboard, WheelButton }

public struct InputBinding
{
    public InputKind Kind { get; set; }
    public Keys Key { get; set; }
    public Keys Modifiers { get; set; }
    public int WheelButton { get; set; }

    public static InputBinding None => new InputBinding { Kind = InputKind.None };
    public static InputBinding FromKey(Keys k) => new InputBinding { Kind = InputKind.Keyboard, Key = k };
    public static InputBinding FromKey(Keys k, Keys modifiers) =>
        new InputBinding { Kind = InputKind.Keyboard, Key = k, Modifiers = modifiers & (Keys.Control | Keys.Shift | Keys.Alt) };
    public static InputBinding FromWheelButton(int idx) => new InputBinding { Kind = InputKind.WheelButton, WheelButton = idx };

    public override string ToString() => Kind switch
    {
        InputKind.Keyboard => ModifierPrefix(Modifiers) + Key.ToString(),
        InputKind.WheelButton => $"Wheel Btn {WheelButton}",
        _ => "-"
    };

    public static string ModifierPrefix(Keys modifiers) =>
        ((modifiers & Keys.Control) != 0 ? "Ctrl+" : "")
        + ((modifiers & Keys.Shift) != 0 ? "Shift+" : "")
        + ((modifiers & Keys.Alt) != 0 ? "Alt+" : "");
}
