using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LFSDriftTools
{
        public class IndicatorManager : IDisposable
    {
        public enum IndicatorState { Off, Left, Right, Hazard }
        public IndicatorState CurrentState { get; private set; } = IndicatorState.Off;

        private int _leftKeyCode = (int)Keys.D7;
        private int _rightKeyCode = (int)Keys.D8;
        private int _hazardKeyCode = (int)Keys.D9;

        public int LeftKeyCode
        {
            get => _leftKeyCode;
            set { _leftKeyCode = value; RefreshKeyboardHook(); }
        }

        public int RightKeyCode
        {
            get => _rightKeyCode;
            set { _rightKeyCode = value; RefreshKeyboardHook(); }
        }

        public int HazardKeyCode
        {
            get => _hazardKeyCode;
            set { _hazardKeyCode = value; RefreshKeyboardHook(); }
        }

        public int? LeftWheelButton { get; set; } = null;
        public int? RightWheelButton { get; set; } = null;
        public int? HazardWheelButton { get; set; } = null;

        public bool AutoReturnEnabled { get; set; } = true;
        private DateTime _lastIndicatorChangeTime = DateTime.UtcNow;
        private const double INDICATOR_TIMEOUT_SEC = 160.0;

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!_enabled && CurrentState != IndicatorState.Off)
                    SetState(IndicatorState.Off);
                RefreshKeyboardHook();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        private const int VK_7 = 0x37;
        private const int VK_8 = 0x38;
        private const int VK_9 = 0x39;
        private const int VK_0 = 0x30;

        public event Action<IndicatorState>? StateChanged;

        public bool LeftLampOn { get; private set; } = false;
        public bool RightLampOn { get; private set; } = false;

        public bool AnySignalLampOn { get; private set; } = false;

        public bool AnyLampOn => LeftLampOn || RightLampOn || AnySignalLampOn;

                public event Action<bool>? LampStateChanged;

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

        private static LowLevelKeyboardHook? _keyboardHook;
        private static IndicatorManager? _instance;

        public IndicatorManager()
        {
            _instance = this;
            RefreshKeyboardHook();
        }

        private void RefreshKeyboardHook()
        {
            if (!ReferenceEquals(_instance, this)) return;

            bool wanted = _enabled && (_leftKeyCode > 0 || _rightKeyCode > 0 || _hazardKeyCode > 0);
            if (wanted && _keyboardHook == null)
            {
                _keyboardHook = new LowLevelKeyboardHook();
                _keyboardHook.OnKeyDown += KeyboardHook_OnKeyDown;
            }
            else if (!wanted && _keyboardHook != null)
            {
                _keyboardHook.Dispose();
                _keyboardHook = null;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static bool IsLfsForeground()
        {
            IntPtr lfs = LfsWindow.Find();
            return lfs != IntPtr.Zero && GetForegroundWindow() == lfs;
        }

        private static void KeyboardHook_OnKeyDown(Keys key)
        {
            if (_instance == null) return;

            int keyCode = (int)key;

            bool isIndicatorKey = keyCode == _instance.LeftKeyCode
                               || keyCode == _instance.RightKeyCode
                               || keyCode == _instance.HazardKeyCode;
            if (!isIndicatorKey || !IsLfsForeground()) return;

            if (keyCode == _instance.LeftKeyCode)
                _instance.ToggleLeft();
            else if (keyCode == _instance.RightKeyCode)
                _instance.ToggleRight();
            else if (keyCode == _instance.HazardKeyCode)
                _instance.ToggleHazard();
        }

        public void ToggleLeft()
        {
            if (!Enabled) return;
            if (CurrentState == IndicatorState.Left)
                SetState(IndicatorState.Off);
            else if (CurrentState != IndicatorState.Hazard)
                SetState(IndicatorState.Left);
        }

        public void ToggleRight()
        {
            if (!Enabled) return;
            if (CurrentState == IndicatorState.Right)
                SetState(IndicatorState.Off);
            else if (CurrentState != IndicatorState.Hazard)
                SetState(IndicatorState.Right);
        }

        public void ToggleHazard()
        {
            if (!Enabled) return;
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
            IntPtr hwnd = LfsWindow.Find();
            if (hwnd == IntPtr.Zero) return;

            int keyCode = GetKeyCodeForState();

            if (!LfsInputGuard.Check(PostMessage(hwnd, WM_KEYDOWN, (IntPtr)keyCode, (IntPtr)0x00170001))) return;
            Thread.Sleep(50);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)keyCode, unchecked((IntPtr)0xC0170001));
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
            if (ReferenceEquals(_instance, this))
                _instance = null;

            _keyboardHook?.Dispose();
            _keyboardHook = null;
        }
    }

        public class LowLevelKeyboardHook : IDisposable
    {
        public event Action<Keys>? OnKeyDown;

        private readonly HashSet<Keys> _heldKeys = new();

        public LowLevelKeyboardHook()
        {
            KeyboardHookHub.Installed += OnHookInstalled;
            KeyboardHookHub.Subscribe(OnKey);
        }

        private void OnHookInstalled() => _heldKeys.Clear();

        private void OnKey(Keys key, bool down, bool isSystem)
        {
            if (isSystem) return;

            if (!down)
            {
                _heldKeys.Remove(key);
                return;
            }

            if (!_heldKeys.Add(key)) return;

            OnKeyDown?.Invoke(key);
        }

        public void Dispose()
        {
            KeyboardHookHub.Installed -= OnHookInstalled;
            KeyboardHookHub.Unsubscribe(OnKey);
        }
    }
}
