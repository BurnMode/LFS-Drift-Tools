using System;
using System.Threading;
using XInputApi = SharpDX.XInput;

namespace LFSDriftTools
{
    public sealed class InputMethodDetector : IDisposable
    {
        private const long RecentActivityMs = 20000;
        private const int PadPollMs = 200;
        private const long DisconnectedRetryMs = 2000;
        private const short StickThreshold = 9000;
        private const byte TriggerThreshold = 40;

        private readonly XInputApi.Controller[] _pads =
        {
            new(XInputApi.UserIndex.One),
            new(XInputApi.UserIndex.Two),
            new(XInputApi.UserIndex.Three),
            new(XInputApi.UserIndex.Four),
        };
        private readonly long[] _nextCheckMs = new long[4];
        private readonly Timer _timer;
        private long _lastWheelMs = long.MinValue / 2;
        private long _lastPadMs = long.MinValue / 2;
        private int _polling;

        public InputMethodDetector()
        {
            _timer = new Timer(_ => PollPads(), null, PadPollMs, PadPollMs);
        }

        public void NoteWheelActivity() => Interlocked.Exchange(ref _lastWheelMs, Environment.TickCount64);

        public void NotePadActivity() => Interlocked.Exchange(ref _lastPadMs, Environment.TickCount64);

        public InputMethod Current
        {
            get
            {
                long now = Environment.TickCount64;
                long wheel = Interlocked.Read(ref _lastWheelMs);
                long pad = Interlocked.Read(ref _lastPadMs);
                bool wheelRecent = now - wheel < RecentActivityMs;
                bool padRecent = now - pad < RecentActivityMs;
                if (wheelRecent && (!padRecent || wheel >= pad)) return InputMethod.Wheel;
                if (padRecent) return InputMethod.Pad;
                return InputMethod.KeyboardMouse;
            }
        }

        private void PollPads()
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try
            {
                long now = Environment.TickCount64;
                for (int i = 0; i < _pads.Length; i++)
                {
                    if (now < _nextCheckMs[i]) continue;
                    try
                    {
                        bool connected;
                        XInputApi.State state;
                        lock (DeviceSync.Lock)
                            connected = _pads[i].GetState(out state);
                        if (!connected)
                        {
                            _nextCheckMs[i] = now + DisconnectedRetryMs;
                            continue;
                        }
                        var g = state.Gamepad;
                        if (Math.Abs((int)g.LeftThumbX) > StickThreshold || Math.Abs((int)g.LeftThumbY) > StickThreshold
                            || Math.Abs((int)g.RightThumbX) > StickThreshold || Math.Abs((int)g.RightThumbY) > StickThreshold
                            || g.LeftTrigger > TriggerThreshold || g.RightTrigger > TriggerThreshold
                            || g.Buttons != XInputApi.GamepadButtonFlags.None)
                        {
                            NotePadActivity();
                        }
                    }
                    catch
                    {
                        _nextCheckMs[i] = now + DisconnectedRetryMs;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
