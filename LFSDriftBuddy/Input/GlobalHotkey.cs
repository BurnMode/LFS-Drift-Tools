using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LFSDriftTools;

public class GlobalHotkey : IDisposable
{
    private class KeyState
    {
        public bool KeyDown;
        public volatile bool Pending;
        public Action? Callback;
    }

    private readonly Dictionary<Keys, KeyState> _bindings = new();

    private readonly System.Windows.Forms.Timer checkTimer = new System.Windows.Forms.Timer();

    public GlobalHotkey(params (Keys key, Action callback)[] bindings)
    {
        foreach (var b in bindings)
            _bindings[b.key] = new KeyState { Callback = b.callback };

        KeyboardHookHub.Subscribe(OnKey);

        checkTimer.Interval = 10;
        checkTimer.Tick += CheckTimer_Tick;
    }

    public void SetBinding(Keys key, Action callback)
    {
        _bindings[key] = new KeyState { Callback = callback };
    }

    public void RemoveBinding(Keys key)
    {
        _bindings.Remove(key);
    }

    private void CheckTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var state in _bindings.Values.ToArray())
        {
            if (!state.Pending) continue;

            state.Pending = false;
            state.Callback?.Invoke();
        }

        if (!_bindings.Values.Any(s => s.Pending))
            checkTimer.Stop();
    }

    private void OnKey(Keys key, bool down, bool isSystem)
    {
        if (!_bindings.TryGetValue(key, out var state)) return;

        if (down)
        {
            if (!state.KeyDown)
            {
                state.KeyDown = true;
                state.Pending = true;

                if (!checkTimer.Enabled)
                    checkTimer.Start();
            }
        }
        else
        {
            state.KeyDown = false;
        }
    }

    public void Dispose()
    {
        checkTimer.Stop();
        checkTimer.Dispose();
        KeyboardHookHub.Unsubscribe(OnKey);
    }
}
