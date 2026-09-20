using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class GlobalHotkey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private class KeyState
    {
        public bool KeyDown;
        public volatile bool Pending;
        public Action Callback;
    }

    private readonly Dictionary<Keys, KeyState> _bindings = new();

    private readonly System.Windows.Forms.Timer checkTimer = new System.Windows.Forms.Timer();

    private LowLevelKeyboardProc proc;
    private IntPtr hook;

    public GlobalHotkey(params (Keys key, Action callback)[] bindings)
    {
        foreach (var b in bindings)
            _bindings[b.key] = new KeyState { Callback = b.callback };

        proc = HookCallback;
        hook = SetHook(proc);

        checkTimer.Interval = 20;
        checkTimer.Tick += CheckTimer_Tick;
        checkTimer.Start();
    }

    public void SetBinding(Keys key, Action callback)
    {
        _bindings[key] = new KeyState { Callback = callback };
    }

    public void RemoveBinding(Keys key)
    {
        _bindings.Remove(key);
    }

    private void CheckTimer_Tick(object sender, EventArgs e)
    {
        foreach (var state in _bindings.Values.ToArray())
        {
            if (!state.Pending) continue;

            state.Pending = false;
            state.Callback?.Invoke();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            Keys key = (Keys)Marshal.ReadInt32(lParam);

            if (_bindings.TryGetValue(key, out var state))
            {
                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    if (!state.KeyDown)
                    {
                        state.KeyDown = true;
                        state.Pending = true;
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                    state.KeyDown = false;
            }
        }

        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        checkTimer.Stop();
        UnhookWindowsHookEx(hook);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;

        return SetWindowsHookEx(
            WH_KEYBOARD_LL,
            proc,
            GetModuleHandle(curModule?.ModuleName),
            0);
    }
}