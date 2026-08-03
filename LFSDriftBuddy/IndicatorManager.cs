using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LFSDriftBuddy
{/// <summary>/// Zarządza kierunkowskazami (lewy, prawy, awaryjny) z obsługą klawiszy,
    /// bindowaniem, automatycznym wyłączaniem i wysyłaniem komend do LFS.
    /// 
    /// </summary>
    public class IndicatorManager : IDisposable{
        // ── Stany kierunkowskazów ─────────────────────────────
        public enum IndicatorState { Off, Left, Right, Hazard }
        public IndicatorState CurrentState { get; private set; } = IndicatorState.Off;

        // ── Konfiguracja ──────────────────────────────────────
        public int LeftKeyCode { get; set; } = (int)Keys.D7;      // 7
        public int RightKeyCode { get; set; } = (int)Keys.D8;    // 8
        public int HazardKeyCode { get; set; } = (int)Keys.D9;   // 9

        // przypisane przyciski kierownicy (null = brak przypisania)
        public int? LeftWheelButton { get; set; } = null;
        public int? RightWheelButton { get; set; } = null;
        public int? HazardWheelButton { get; set; } = null;
        public double SteeringReturnThreshold { get; set; } = 10.0;  // stopni

    // ── Funkcje ───────────────────────────────────────────
    public bool AutoReturnEnabled { get; set; } = true;
    public double LastHeadingDeg { get; private set; } = 0;
    public double CurrentHeadingDeg { get; private set; } = 0;
    private DateTime _lastIndicatorChangeTime = DateTime.UtcNow;
    private const double INDICATOR_TIMEOUT_SEC = 160.0;  // auto-wyłącz po 8 sekundach

    // ── Śledzenie stabilizacji kierunku (dla auto-return) ──
    private int _stableHeadingFrames = 0;
    private const int STABLE_FRAMES_THRESHOLD = 100;  // 5 kolejnych frame'ów ze zmianą < progu

    // ── WinAPI do wysyłania klawiszy do LFS ───────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    // Kody klawiszy dla kierunkowskazów
    private const int VK_7 = 0x37;  // Lewy
    private const int VK_8 = 0x38;  // Prawy
    private const int VK_9 = 0x39;  // Awaryjny
    private const int VK_0 = 0x30;  // Wyłączenie

    // ── Events ────────────────────────────────────────────
    public event Action<IndicatorState>? StateChanged;

    // ── Global keyboard hook ──────────────────────────────
    private static LowLevelKeyboardHook _keyboardHook;
    private static IndicatorManager _instance;

    public IndicatorManager()
    {
        _instance = this;
        if (_keyboardHook == null)
        {
            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.OnKeyDown += KeyboardHook_OnKeyDown;
            _keyboardHook.OnKeyUp += KeyboardHook_OnKeyUp;
        }
    }

    // ── Obsługa wciśnięcia klawisza ───────────────────────
    private static void KeyboardHook_OnKeyDown(Keys key)
    {
        if (_instance == null) return;

        int keyCode = (int)key;

        if (keyCode == _instance.LeftKeyCode)
        {
            _instance.ToggleLeft();
        }
        else if (keyCode == _instance.RightKeyCode)
        {
            _instance.ToggleRight();
        }
        else if (keyCode == _instance.HazardKeyCode)
        {
            _instance.ToggleHazard();
        }
    }

    private static void KeyboardHook_OnKeyUp(Keys key)
    {
        // Opcjonalnie: można tutaj obsługiwać zwolnienie klawisza
    }

    // ── Metody główne ────────────────────────────────────
    public void ToggleLeft()
    {
        if (CurrentState == IndicatorState.Left)
        {
            SetState(IndicatorState.Off);
        }
        else if (CurrentState != IndicatorState.Hazard)
        {
            SetState(IndicatorState.Left);
        }
    }

    public void ToggleRight()
    {
        if (CurrentState == IndicatorState.Right)
        {
            SetState(IndicatorState.Off);
        }
        else if (CurrentState != IndicatorState.Hazard)
        {
            SetState(IndicatorState.Right);
        }
    }

    public void ToggleHazard()
    {
        if (CurrentState == IndicatorState.Hazard)
        {
            SetState(IndicatorState.Off);
        }
        else
        {
            SetState(IndicatorState.Hazard);
        }
    }

    public void SetState(IndicatorState newState)
    {
        if (CurrentState != newState)
        {
            CurrentState = newState;
            _lastIndicatorChangeTime = DateTime.UtcNow;
            _stableHeadingFrames = 0;  // ← DODAJ TĘ LINIĘ
            StateChanged?.Invoke(newState);
        }
    }

    // ── Wysyłanie klawisza do LFS ───────────────────────
    public void SendKeyToLFS()
    {
        // Szukamy okna LFS (analogicznie do RevLimiter)
        IntPtr hwnd = FindLfsWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;  // LFS nie znalezione - nie wysyłamy
        }

        // Wyznacz kod klawisza na podstawie aktualnego stanu
        int keyCode = GetKeyCodeForState();

        // Wyślij klawisz do LFS (non-blocking via PostMessage)
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)keyCode, (IntPtr)0x00170001);
        Thread.Sleep(50);  // 50ms na naciśnięcie
        PostMessage(hwnd, WM_KEYUP, (IntPtr)keyCode, (IntPtr)0xC0170001);
    }

    private int GetKeyCodeForState()
    {
        return CurrentState switch
        {
            IndicatorState.Left => VK_7,
            IndicatorState.Right => VK_8,
            IndicatorState.Hazard => VK_9,
            _ => VK_0  // Off
        };
    }

    private static IntPtr FindLfsWindow()
    {
        // LFS używa różnych tytułów zależnie od wersji
        string[] titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };
        foreach (var t in titles)
        {
            IntPtr h = FindWindow(null, t);
            if (h != IntPtr.Zero) return h;
        }
        return IntPtr.Zero;
    }

    // ── Auto-wyłączanie na podstawie stabilizacji kierunku ──
    public void Update(double headingDeg)
    {
        LastHeadingDeg = CurrentHeadingDeg;
        CurrentHeadingDeg = headingDeg;

        if (!AutoReturnEnabled || CurrentState == IndicatorState.Off)
        {
            _stableHeadingFrames = 0;
            return;
        }

        // Sprawdź timeout
        var elapsed = DateTime.UtcNow - _lastIndicatorChangeTime;
        if (elapsed.TotalSeconds > INDICATOR_TIMEOUT_SEC && CurrentState != IndicatorState.Hazard)
        {
            SetState(IndicatorState.Off);
            _stableHeadingFrames = 0;
            return;
        }

        // ── Logika: kierownica wraca do centru gdy heading stabilizuje się ──
        // Auto-wyłączanie oparte tylko na timeout, nie na heading
        // (heading w LFS zmienia się zbyt powoli - nie nadaje się do tego)
    }

    // ── Konwersja stanu na ciąg tekstowy ──────────────────
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

// ────────────────────────────────────────────────────────
// Low-Level Keyboard Hook dla globalnego chwytania klawiszy
// ────────────────────────────────────────────────────────
public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc _hookProc;

    public event Action<Keys>? OnKeyDown;
    public event Action<Keys>? OnKeyUp;

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
        if (nCode >= 0)
        {
            var kbdStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            Keys key = (Keys)kbdStruct.vkCode;

            if (wParam == (IntPtr)WM_KEYDOWN)
            {
                OnKeyDown?.Invoke(key);
            }
            else if (wParam == (IntPtr)WM_KEYUP)
            {
                OnKeyUp?.Invoke(key);
            }
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