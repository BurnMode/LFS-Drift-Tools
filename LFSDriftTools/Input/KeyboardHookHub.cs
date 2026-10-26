using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public static class KeyboardHookHub
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static readonly LowLevelKeyboardProc Proc = HookCallback;
        private static IntPtr _hook = IntPtr.Zero;
        private static Action<Keys, bool, bool>? _handlers;
        private static int _subscribers;
        private static bool _targetRunning;

        public static bool IsActive => _hook != IntPtr.Zero;
        public static bool IsRequested => _subscribers > 0 && _targetRunning;
        public static int InstallError { get; private set; }

        public static event Action? Installed;

        public static void Subscribe(Action<Keys, bool, bool> handler)
        {
            _handlers += handler;
            _subscribers++;
            Apply();
        }

        public static void Unsubscribe(Action<Keys, bool, bool> handler)
        {
            _handlers -= handler;
            if (_subscribers > 0) _subscribers--;
            Apply();
        }

        public static void SetTargetRunning(bool running)
        {
            if (_targetRunning == running) return;
            _targetRunning = running;
            Apply();
        }

        private static void Apply()
        {
            bool wanted = IsRequested;
            if (wanted && _hook == IntPtr.Zero) Install();
            else if (!wanted && _hook != IntPtr.Zero) Uninstall();
        }

        private static void Install()
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule? curModule = curProcess.MainModule;

            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(curModule?.ModuleName), 0);
            InstallError = _hook == IntPtr.Zero ? Marshal.GetLastPInvokeError() : 0;
            if (_hook != IntPtr.Zero) Installed?.Invoke();
        }

        private static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = (int)wParam;
                bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool up = message == WM_KEYUP || message == WM_SYSKEYUP;

                if (down || up)
                {
                    Keys key = (Keys)Marshal.ReadInt32(lParam);
                    bool isSystem = message == WM_SYSKEYDOWN || message == WM_SYSKEYUP;

                    try { _handlers?.Invoke(key, down, isSystem); }
                    catch (Exception ex) { ErrorLog.Write("Keyboard hook handler", ex); }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
