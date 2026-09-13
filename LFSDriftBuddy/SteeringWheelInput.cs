using LFSDriftBuddy;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using XInputApi = SharpDX.XInput;

public class SteeringWheelInput : IDisposable
{
    private readonly DirectInput _directInput;
    private Joystick _wheel;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool[] _previousButtons = Array.Empty<bool>();
    private readonly Dictionary<int, Action> _buttonBindings;
    private bool _autoConnectAttempted = false;
    private DateTime _reconnectRetryAt = DateTime.MaxValue;
    private const int ReconnectDelayMs = 1500;
    public bool IsConnected => _wheel != null;
    public string DeviceName { get; private set; } = "";
    public Guid DeviceGuid { get; private set; } = Guid.Empty;
    public event Action<string> Log;

    public event Action<int> AnyButtonPressed;
    public double SteeringPercent { get; private set; } = 0;
    public event Action<double> SteeringChanged;

    public event Action<List<(Guid Guid, string Name)>> WheelNotFound;

    public event Action<Dictionary<JoystickOffset, int>> RawAxesChanged;

    private const int AxisRange = 10000;

    public JoystickOffset SteeringAxis { get; private set; } = JoystickOffset.X;

    private static readonly string[] KnownWheelNames = new[]
    {

        "G25", "G27", "G29", "G920", "G923", "Driving Force",

        "Moza", "R3", "R5", "R9", "R12", "R16", "R21",

        "T150", "T300", "T500", "TS-XW", "TS-PC", "T-GT", "TX Racing", "TMX",

        "CSL", "Fanatec", "ClubSport"
    };

    private static readonly Dictionary<string, JoystickOffset> KnownAxisOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "T500", JoystickOffset.RotationX },
            { "TX Racing", JoystickOffset.RotationX },
        };

    private static readonly XInputApi.GamepadButtonFlags[] XInputButtonFlags =
        Enum.GetValues(typeof(XInputApi.GamepadButtonFlags))
            .Cast<XInputApi.GamepadButtonFlags>()
            .Where(f => f != XInputApi.GamepadButtonFlags.None)
            .ToArray();

    public void SetBinding(int buttonIndex, Action onPressed)
    {
        _buttonBindings[buttonIndex] = onPressed;
    }

    public void RemoveBinding(int buttonIndex)
    {
        _buttonBindings.Remove(buttonIndex);
    }
    private XInputApi.Controller _xinputPad;
    private bool _useXInput = false;
    private readonly IntPtr _ownerHandle;

    public SteeringWheelInput(IntPtr ownerHandle, params (int buttonIndex, Action onPressed)[] bindings)
    {
        _ownerHandle = ownerHandle;
        _buttonBindings = bindings.ToDictionary(b => b.buttonIndex, b => b.onPressed);
        _directInput = new DirectInput();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pollTimer.Tick += (s, e) => Poll();
        _pollTimer.Start();
    }

    public List<(Guid Guid, string Name)> GetAvailableDevices()
    {
        return _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .Select(d => (d.InstanceGuid, d.ProductName))
            .ToList();
    }

    private void TryAutoConnect()
    {
        var devices = _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .ToList();

        var wheelInfo = devices.FirstOrDefault(d =>
            d.Type == DeviceType.Driving ||
            KnownWheelNames.Any(name => d.ProductName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));

        if (wheelInfo == null)
        {
            //Log?.Invoke(Localization.T("wheelconfig.nodetectauto"));
            var list = devices.Select(d => (d.InstanceGuid, d.ProductName)).ToList();
            WheelNotFound?.Invoke(list);
            return;
        }

        var axisOverride = KnownAxisOverrides.FirstOrDefault(kv =>
            wheelInfo.ProductName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0);

        JoystickOffset axis = axisOverride.Key != null ? axisOverride.Value : JoystickOffset.X;

        Connect(wheelInfo.InstanceGuid, axis, wheelInfo.ProductName);
    }

    public bool ConnectToDevice(Guid instanceGuid, JoystickOffset axis)
    {
        var info = _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .FirstOrDefault(d => d.InstanceGuid == instanceGuid);

        if (info == null)
        {
            Log?.Invoke(Localization.T("wheelconfig.disconnect"));
            return false;
        }

        Connect(instanceGuid, axis, info.ProductName);
        return true;
    }

    private void PollXInput()
    {
        if (_xinputPad == null || !_xinputPad.IsConnected)
        {
            Log?.Invoke(Localization.T("wheelconfig.disconnectpad"));

            _useXInput = false;
            _xinputPad = null;
            _reconnectRetryAt = DateTime.UtcNow.AddMilliseconds(ReconnectDelayMs);
            return;
        }

        var state = _xinputPad.GetState();

        double newSteering = Math.Round(state.Gamepad.LeftThumbX / 32767.0 * 100.0, 1);
        newSteering = Math.Max(-100, Math.Min(100, newSteering));

        if (Math.Abs(newSteering - SteeringPercent) >= 0.5)
        {
            SteeringPercent = newSteering;
            SteeringChanged?.Invoke(SteeringPercent);
        }

        RawAxesChanged?.Invoke(new Dictionary<JoystickOffset, int>
        {
            [JoystickOffset.X] = state.Gamepad.LeftThumbX,
            [JoystickOffset.Y] = state.Gamepad.LeftThumbY,
            [JoystickOffset.Z] = state.Gamepad.RightThumbX,
            [JoystickOffset.RotationX] = state.Gamepad.RightThumbY,
            [JoystickOffset.RotationY] = state.Gamepad.LeftTrigger,
            [JoystickOffset.RotationZ] = state.Gamepad.RightTrigger,
        });

        var flags = state.Gamepad.Buttons;
        bool[] buttons = new bool[16];

        for (int i = 0; i < XInputButtonFlags.Length && i < buttons.Length; i++)
            buttons[i] = flags.HasFlag(XInputButtonFlags[i]);

        const byte TriggerPressThreshold = 75;
        int triggerBaseIndex = XInputButtonFlags.Length;
        if (triggerBaseIndex < buttons.Length)
            buttons[triggerBaseIndex] = state.Gamepad.LeftTrigger >= TriggerPressThreshold;
        if (triggerBaseIndex + 1 < buttons.Length)
            buttons[triggerBaseIndex + 1] = state.Gamepad.RightTrigger >= TriggerPressThreshold;

        for (int i = 0; i < buttons.Length; i++)
        {
            bool now = buttons[i];
            bool before = i < _previousButtons.Length && _previousButtons[i];

            if (now && !before)
            {
                AnyButtonPressed?.Invoke(i);
                if (_buttonBindings.TryGetValue(i, out var action))
                    action?.Invoke();
            }
        }
        _previousButtons = buttons;
    }

    public void SetSteeringAxis(JoystickOffset axis)
    {
        SteeringAxis = axis;
        string text = Localization.T("wheelconfig.axis");
        Log?.Invoke($"{text}{axis}");
    }

    public static bool IsXInputPad(string productName) =>
       productName.IndexOf("XBOX", StringComparison.OrdinalIgnoreCase) >= 0 ||
       productName.IndexOf("XInput", StringComparison.OrdinalIgnoreCase) >= 0 ||
       productName.IndexOf("Controller (X", StringComparison.OrdinalIgnoreCase) >= 0;

    private void Connect(Guid instanceGuid, JoystickOffset axis, string name)
    {
        _useXInput = IsXInputPad(name);

        if (_useXInput)
        {
            _wheel?.Unacquire();
            _wheel?.Dispose();
            _wheel = null;

            _xinputPad = new[]
           {
                XInputApi.UserIndex.One, XInputApi.UserIndex.Two,
                XInputApi.UserIndex.Three, XInputApi.UserIndex.Four
            }
           .Select(i => new XInputApi.Controller(i))
           .FirstOrDefault(c => c.IsConnected);

            if (_xinputPad == null)
            {
                Log?.Invoke(Localization.T("wheelconfig.noxinput"));
                return;
            }

            DeviceName = name;
            DeviceGuid = instanceGuid;
            SteeringAxis = axis;
            _previousButtons = new bool[16];

            Log?.Invoke($"{Localization.T("status.connected")} (XInput): {DeviceName}");
            return;
        }

        try
        {
            _wheel?.Unacquire();
            _wheel?.Dispose();

            _wheel = new Joystick(_directInput, instanceGuid);
            _wheel.Properties.BufferSize = 128;

            _wheel.SetCooperativeLevel(_ownerHandle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            _wheel.Acquire();

            foreach (var deviceObject in _wheel.GetObjects(DeviceObjectTypeFlags.Axis))
            {
                _wheel.GetObjectPropertiesById(deviceObject.ObjectId).Range = new InputRange(-AxisRange, AxisRange);
            }

            SteeringAxis = axis;
            DeviceName = name;
            DeviceGuid = instanceGuid;
            _previousButtons = new bool[_wheel.Capabilities.ButtonCount];

            Log?.Invoke($"{Localization.T("status.connected")} {DeviceName} ({Localization.T("wheelconfig.axis")}{SteeringAxis})");
        }
        catch (SharpDX.SharpDXException ex)
        {
            string text = Localization.T("wheelconfig.disconnecterror");
            Log?.Invoke($"{text}{ex.Message}");
            _wheel = null;
        }
    }

    private void Poll()
    {
        if (_useXInput)
        {
            PollXInput();
            return;
        }

        if (_wheel == null)
        {
            if (!_autoConnectAttempted)
            {
                _autoConnectAttempted = true;
                TryAutoConnect();
            }
            else if (DateTime.UtcNow >= _reconnectRetryAt)
            {
                _reconnectRetryAt = DateTime.MaxValue;
                TryAutoConnect();
            }
            return;
        }

        try
        {
            _wheel.Poll();
            var state = _wheel.GetCurrentState();
            var buttons = state.Buttons;

            int raw = GetAxisValue(state, SteeringAxis);
            double newSteering = Math.Round((double)raw / AxisRange * 100.0, 1);
            newSteering = Math.Max(-100, Math.Min(100, newSteering));

            if (Math.Abs(newSteering - SteeringPercent) >= 0.5)
            {
                SteeringPercent = newSteering;
                SteeringChanged?.Invoke(SteeringPercent);
            }

            if (RawAxesChanged != null)
            {
                RawAxesChanged.Invoke(new Dictionary<JoystickOffset, int>
                {
                    [JoystickOffset.X] = state.X,
                    [JoystickOffset.Y] = state.Y,
                    [JoystickOffset.Z] = state.Z,
                    [JoystickOffset.RotationX] = state.RotationX,
                    [JoystickOffset.RotationY] = state.RotationY,
                    [JoystickOffset.RotationZ] = state.RotationZ,
                });
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                bool now = buttons[i];
                bool before = i < _previousButtons.Length && _previousButtons[i];

                if (now && !before)
                {
                    AnyButtonPressed?.Invoke(i);

                    if (_buttonBindings.TryGetValue(i, out var action))
                        action?.Invoke();
                }
            }
            _previousButtons = buttons.ToArray();
        }
        catch (SharpDX.SharpDXException)
        {

            _wheel = null;
            _reconnectRetryAt = DateTime.UtcNow.AddMilliseconds(ReconnectDelayMs);
            Log?.Invoke(Localization.T("status.disconnectwheel"));
        }
    }

    private static int GetAxisValue(JoystickState state, JoystickOffset axis) => axis switch
    {
        JoystickOffset.X => state.X,
        JoystickOffset.Y => state.Y,
        JoystickOffset.Z => state.Z,
        JoystickOffset.RotationX => state.RotationX,
        JoystickOffset.RotationY => state.RotationY,
        JoystickOffset.RotationZ => state.RotationZ,
        _ => state.X
    };

    public void Dispose()
    {
        _pollTimer?.Stop();
        _wheel?.Unacquire();
        _wheel?.Dispose();
        _directInput?.Dispose();
        _xinputPad = null;
    }
}