using System.Windows.Forms;

public enum InputKind { None, Keyboard, WheelButton }

public struct InputBinding
{
    public InputKind Kind { get; set; }
    public Keys Key { get; set; }
    public int WheelButton { get; set; }

    public static InputBinding None => new InputBinding { Kind = InputKind.None };
    public static InputBinding FromKey(Keys k) => new InputBinding { Kind = InputKind.Keyboard, Key = k };
    public static InputBinding FromWheelButton(int idx) => new InputBinding { Kind = InputKind.WheelButton, WheelButton = idx };

    public override string ToString() => Kind switch
    {
        InputKind.Keyboard => Key.ToString(),
        InputKind.WheelButton => $"Wheel Btn {WheelButton}",
        _ => "-"
    };
}