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

    // stan poprzedniej klatki, żeby wykrywać zbocze (naciśnięcie), a nie "trzymanie"
    private bool[] _previousButtons = Array.Empty<bool>();
    // mapowanie: indeks przycisku (0-based) -> akcja wywoływana przy naciśnięciu
    private readonly Dictionary<int, Action> _buttonBindings;

    // ustawiane na true po pierwszej próbie auto-connecta (żeby Poll() nie próbował w kółko
    // i żeby WheelNotFound wystrzelił dokładnie raz, gdy subskrybenci już są podpięci)
    private bool _autoConnectAttempted = false;

    public bool IsConnected => _wheel != null;
    public string DeviceName { get; private set; } = "";
    public Guid DeviceGuid { get; private set; } = Guid.Empty;
    public event Action<string> Log;

    // odpala się dla KAŻDEGO naciśniętego przycisku, niezależnie od tego,
    // czy ma przypisaną akcję — używane m.in. przez dialog bindowania
    public event Action<int> AnyButtonPressed;

    // -100 (do oporu w lewo) .. 0 (środek) .. 100 (do oporu w prawo)
    public double SteeringPercent { get; private set; } = 0;
    public event Action<double> SteeringChanged;

    // wystrzeliwuje się, gdy automatyczna detekcja nie znajdzie znanej kierownicy —
    // UI (MainForm) łapie to i pokazuje dialog ręcznego wyboru urządzenia
    public event Action<List<(Guid Guid, string Name)>> WheelNotFound;

    // surowe wartości wszystkich osi na każdej klatce — używane wyłącznie podczas
    // kalibracji osi w WheelSetupForm (kręcisz kierownicą, program patrzy która się rusza)
    public event Action<Dictionary<JoystickOffset, int>> RawAxesChanged;

    // zakres, do którego normalizujemy oś po Acquire() — stała wartość,
    // żeby nie zależeć od domyślnego zakresu sterownika
    private const int AxisRange = 10000;

    // która oś DirectInput reprezentuje skręt kierownicy — domyślnie X (większość kierownic),
    // ale da się to zmienić ręcznie (patrz SetSteeringAxis / WheelSetupForm)
    public JoystickOffset SteeringAxis { get; private set; } = JoystickOffset.X;

    // znane modele popularnych kierownic (fallback po nazwie, gdy sterownik nie zgłasza
    // DeviceType.Driving — zdarza się np. przy starszych driverach)
    private static readonly string[] KnownWheelNames = new[]
    {
        // Logitech
        "G25", "G27", "G29", "G920", "G923", "Driving Force",
        // Moza
        "Moza", "R3", "R5", "R9", "R12", "R16", "R21",
        // Thrustmaster
        "T150", "T300", "T500", "TS-XW", "TS-PC", "T-GT", "TX Racing", "TMX",
        // Fanatec
        "CSL", "Fanatec", "ClubSport"
    };

    // znane wyjątki, gdzie skręt NIE jest zgłaszany jako oś X — dopisuj tu modele,
    // dla których użytkownicy zgłoszą problem zamiast każdorazowo kalibrować ręcznie
    private static readonly Dictionary<string, JoystickOffset> KnownAxisOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "T500", JoystickOffset.RotationX },
            { "TX Racing", JoystickOffset.RotationX },
        };

    private static readonly JoystickOffset[] AllAxes = new[]
    {
        JoystickOffset.X, JoystickOffset.Y, JoystickOffset.Z,
        JoystickOffset.RotationX, JoystickOffset.RotationY, JoystickOffset.RotationZ
    };

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

        // UWAGA: świadomie NIE łączymy się tutaj automatycznie. Robimy to w pierwszym
        // Tick() timera, żeby wywołujący (MainForm) zdążył podpiąć się pod WheelNotFound
        // zanim ta próba w ogóle się odbędzie.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 }; // ~50Hz
        _pollTimer.Tick += (s, e) => Poll();
        _pollTimer.Start();
    }

    // ── Wykrywanie / wybór urządzenia ──────────────────────────

    // zwraca wszystkie podłączone kontrolery gier — do wyświetlenia w dialogu ręcznego wyboru
    public List<(Guid Guid, string Name)> GetAvailableDevices()
    {
        return _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .Select(d => (d.InstanceGuid, d.ProductName))
            .ToList();
    }

    private void TryAutoConnect()
    {
        Log?.Invoke("TryConnect");

        var devices = _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .ToList();

        var wheelInfo = devices.FirstOrDefault(d =>
            d.Type == DeviceType.Driving ||
            KnownWheelNames.Any(name => d.ProductName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));

        if (wheelInfo == null)
        {
           
           
            Log?.Invoke(Localization.T("wheelconfig.nodetectauto"));
            var list = devices.Select(d => (d.InstanceGuid, d.ProductName)).ToList();
            WheelNotFound?.Invoke(list);
            return;
        }

        var axisOverride = KnownAxisOverrides.FirstOrDefault(kv =>
            wheelInfo.ProductName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0);

        JoystickOffset axis = axisOverride.Key != null ? axisOverride.Value : JoystickOffset.X;

        Connect(wheelInfo.InstanceGuid, axis, wheelInfo.ProductName);
    }

    // wywoływane z dialogu / z ustawień zapisanych na dysku, gdy użytkownik (lub poprzednia
    // sesja) już wybrał konkretne urządzenie
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
            return;
        }

        var state = _xinputPad.GetState();

        // Lewy stick X jako "skręt" — działa niezależnie od fokusu okna
        double newSteering = Math.Round(state.Gamepad.LeftThumbX / 32767.0 * 100.0, 1);
        newSteering = Math.Max(-100, Math.Min(100, newSteering));

        if (Math.Abs(newSteering - SteeringPercent) >= 0.5)
        {
            SteeringPercent = newSteering;
            SteeringChanged?.Invoke(SteeringPercent);
        }

        // Mapowanie flag przycisków XInput na indeksy 0-15 (kolejność jak w SharpDX.XInput.GamepadButtonFlags)
        var flags = state.Gamepad.Buttons;
        bool[] buttons = new bool[16];
        // new_str
        var allFlags = Enum.GetValues(typeof(XInputApi.GamepadButtonFlags))
                            .Cast<XInputApi.GamepadButtonFlags>()
                            .Where(f => f != XInputApi.GamepadButtonFlags.None)
                            .ToArray();

        for (int i = 0; i < allFlags.Length && i < buttons.Length; i++)
            buttons[i] = flags.HasFlag(allFlags[i]);

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
    // pozwala zmienić oś skrętu bez ponownego łączenia (np. po ręcznej kalibracji)
    public void SetSteeringAxis(JoystickOffset axis)
    {
        SteeringAxis = axis;
        string text = Localization.T("wheelconfig.axis");
        Log?.Invoke($"{text} {axis}");
    }

    private static bool IsXInputPad(string productName) =>
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

            // Pierwszy podłączony/aktywny kontroler XInput (indeksy One..Four)
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
            SteeringAxis = axis; // nieużywane w trybie XInput, zostaje dla spójności API
            _previousButtons = new bool[16];
            _autoConnectAttempted = true;

            Log?.Invoke($"{Localization.T("status.connected")}  (XInput): {DeviceName}");
            return;
        }

        try
        {
            _wheel?.Unacquire();
            _wheel?.Dispose();

            _wheel = new Joystick(_directInput, instanceGuid);
            _wheel.Properties.BufferSize = 128;

            // Bez tego pady (XInput przez DirectInput) tracą input, gdy okno aplikacji
            // nie ma fokusu (np. gdy LFS jest na pierwszym planie). Kierownice zwykle
            // działały "przypadkiem" dzięki własnym sterownikom.
            _wheel.SetCooperativeLevel(_ownerHandle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            _wheel.Acquire();

            // wymuś stały, znany zakres na WSZYSTKICH osiach — niezależnie od tego,
            // którą finalnie wybierzemy jako skręt
            foreach (var deviceObject in _wheel.GetObjects(DeviceObjectTypeFlags.Axis))
            {
                _wheel.GetObjectPropertiesById(deviceObject.ObjectId).Range = new InputRange(-AxisRange, AxisRange);
            }

            SteeringAxis = axis;
            DeviceName = name;
            DeviceGuid = instanceGuid;
            _previousButtons = new bool[_wheel.Capabilities.ButtonCount];
            _autoConnectAttempted = true; // nie nadpisuj ręcznego/zapisanego wyboru auto-detekcją

            Log?.Invoke($"{Localization.T("status.connected")} {DeviceName} {Localization.T("wheelconfig.axis")} {SteeringAxis})");
        }
        catch (SharpDX.SharpDXException ex)
        {
            string text = Localization.T("wheelconfig.disconnecterror");
            Log?.Invoke($"{text} {ex.Message}");
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

            // tylko gdy ktoś faktycznie słucha (np. dialog kalibracji) — żeby nie alokować
            // słownika 50 razy na sekundę bez potrzeby
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

                // wykrywamy tylko moment naciśnięcia (zbocze narastające)
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
            // kierownica odłączona / utracona kontrola
            _wheel = null;
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