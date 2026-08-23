using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LFSDriftBuddy
{
    /// <summary>Manages turn signals (left/right/hazard): key handling, bindings,
    /// auto-cancel, and sending commands to LFS.</summary>
    public class IndicatorManager : IDisposable
    {
        public enum IndicatorState { Off, Left, Right, Hazard }
        public IndicatorState CurrentState { get; private set; } = IndicatorState.Off;

        public int LeftKeyCode { get; set; } = (int)Keys.D7;
        public int RightKeyCode { get; set; } = (int)Keys.D8;
        public int HazardKeyCode { get; set; } = (int)Keys.D9;

        // Bound wheel buttons (null = unbound).
        public int? LeftWheelButton { get; set; } = null;
        public int? RightWheelButton { get; set; } = null;
        public int? HazardWheelButton { get; set; } = null;

        public bool AutoReturnEnabled { get; set; } = true;
        private DateTime _lastIndicatorChangeTime = DateTime.UtcNow;
        private const double INDICATOR_TIMEOUT_SEC = 160.0;   // auto-off after this many seconds

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        // LFS's own default indicator key codes.
        private const int VK_7 = 0x37;  // Left
        private const int VK_8 = 0x38;  // Right
        private const int VK_9 = 0x39;  // Hazard
        private const int VK_0 = 0x30;  // Off

        public event Action<IndicatorState>? StateChanged;

        // Real lamp state from OutGauge (ShowLights) — the dashboard's own truth, which can
        // differ from CurrentState (our toggle intent), e.g. if LFS cancels it itself. Sounds
        // (click/cancel) should sync to THIS, not CurrentState.
        public bool LeftLampOn { get; private set; } = false;
        public bool RightLampOn { get; private set; } = false;

        // Some cars only expose the combined DL_SIGNAL_ANY bit, not separate L/R bits —
        // without this, those cars would never show a detected lamp even while blinking.
        public bool AnySignalLampOn { get; private set; } = false;

        public bool AnyLampOn => LeftLampOn || RightLampOn || AnySignalLampOn;

        /// <summary>Fires only on an actual lamp state change (on/off) from OutGauge — the only
        /// correct source for syncing indicator_click_on/off.wav.</summary>
        public event Action<bool>? LampStateChanged;

        /// <summary>Called on every OutGauge packet — updates real lamp state and fires
        /// LampStateChanged only on an actual change, not every UDP packet.</summary>
        public void UpdateFromOutGauge(bool leftOn, bool rightOn, bool anyOn)
        {
            bool wasOn = AnyLampOn;

            LeftLampOn = leftOn;
            RightLampOn = rightOn;
            AnySignalLampOn = anyOn;

            bool isOn = AnyLampOn;

            if (isOn != wasOn)
                LampStateChanged?.Invoke(isOn);
        }

        private static LowLevelKeyboardHook _keyboardHook;
        private static IndicatorManager _instance;

        public IndicatorManager()
        {
            _instance = this;
            if (_keyboardHook == null)
            {
                _keyboardHook = new LowLevelKeyboardHook();
                _keyboardHook.OnKeyDown += KeyboardHook_OnKeyDown;
            }
        }

        private static void KeyboardHook_OnKeyDown(Keys key)
        {
            if (_instance == null) return;

            int keyCode = (int)key;

            if (keyCode == _instance.LeftKeyCode)
                _instance.ToggleLeft();
            else if (keyCode == _instance.RightKeyCode)
                _instance.ToggleRight();
            else if (keyCode == _instance.HazardKeyCode)
                _instance.ToggleHazard();
        }

        public void ToggleLeft()
        {
            if (CurrentState == IndicatorState.Left)
                SetState(IndicatorState.Off);
            else if (CurrentState != IndicatorState.Hazard)
                SetState(IndicatorState.Left);
        }

        public void ToggleRight()
        {
            if (CurrentState == IndicatorState.Right)
                SetState(IndicatorState.Off);
            else if (CurrentState != IndicatorState.Hazard)
                SetState(IndicatorState.Right);
        }

        public void ToggleHazard()
        {
            if (CurrentState == IndicatorState.Hazard)
                SetState(IndicatorState.Off);
            else
                SetState(IndicatorState.Hazard);
        }

        public void SetState(IndicatorState newState)
        {
            if (CurrentState != newState)
            {
                CurrentState = newState;
                _lastIndicatorChangeTime = DateTime.UtcNow;
                StateChanged?.Invoke(newState);
            }
        }

        public void SendKeyToLFS()
        {
            IntPtr hwnd = FindLfsWindow();
            if (hwnd == IntPtr.Zero) return;

            int keyCode = GetKeyCodeForState();

            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)keyCode, (IntPtr)0x00170001);
            Thread.Sleep(50);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)keyCode, (IntPtr)0xC0170001);
        }

        private int GetKeyCodeForState()
        {
            return CurrentState switch
            {
                IndicatorState.Left => VK_7,
                IndicatorState.Right => VK_8,
                IndicatorState.Hazard => VK_9,
                _ => VK_0
            };
        }

        private static IntPtr FindLfsWindow()
        {
            string[] titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };
            foreach (var t in titles)
            {
                IntPtr h = FindWindow(null, t);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }

        /// <summary>Call every telemetry tick — auto-cancels after INDICATOR_TIMEOUT_SEC.</summary>
        public void Update()
        {
            if (!AutoReturnEnabled || CurrentState == IndicatorState.Off) return;

            var elapsed = DateTime.UtcNow - _lastIndicatorChangeTime;
            if (elapsed.TotalSeconds > INDICATOR_TIMEOUT_SEC && CurrentState != IndicatorState.Hazard)
                SetState(IndicatorState.Off);
        }

        public string GetIndicatorText()
        {
            return CurrentState switch
            {
                IndicatorState.Left => $"⬅ {Localization.T("indicators.left")}",
                IndicatorState.Right => $"{Localization.T("indicators.right")} ➡",
                IndicatorState.Hazard => $"⚠ {Localization.T("indicators.hazard")}",
                _ => "---"
            };
        }

        public void Dispose()
        {
            _keyboardHook?.Dispose();
            _keyboardHook = null;
        }
    }

    /// <summary>System-wide low-level keyboard hook.</summary>
    public class LowLevelKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;

        public event Action<Keys>? OnKeyDown;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public LowLevelKeyboardHook()
        {
            _hookProc = HookCallback;
            _hookHandle = SetupHook();
        }

        private IntPtr SetupHook()
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var kbdStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                OnKeyDown?.Invoke((Keys)kbdStruct.vkCode);
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}
