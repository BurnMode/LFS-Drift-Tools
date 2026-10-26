using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LFSDriftTools
{

    public class RevLimiter : IDisposable
    {

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_I = 0x49;

        public int RpmLimit { get; set; } = 7500;
        public int CutMs { get; set; } = 40;
        public int CooldownMs { get; set; } = 30;
        public bool Enabled { get; set; } = true;

        public int WatchdogMarginMs { get; set; } = 200;

        public bool IsCutting { get; private set; } = false;

        private volatile bool _ignitionState = true;
        public bool IsRunning { get; private set; } = false;
        public int CutCount { get; private set; } = 0;

        public int WatchdogRecoveries { get; private set; } = 0;

        public event Action? CutStarted;
        public event Action? CutEnded;
        public event Action<string>? Error;

        private DateTime _lastCut = DateTime.MinValue;
        private readonly object _lock = new();
        private readonly object _restoreLock = new();
        private System.Threading.Timer? _watchdogTimer;
        private bool _timerResolutionRaised;

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            _timerResolutionRaised = TimerResolution.Acquire();
            _watchdogTimer = new System.Threading.Timer(
                _ => WatchdogCheck(),
                null,
                dueTime: 100,
                period: 100);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            _watchdogTimer?.Dispose();
            _watchdogTimer = null;

            ForceRestoreIgnitionIfNeeded("Stop()");

            IsCutting = false;

            if (_timerResolutionRaised)
            {
                TimerResolution.Release();
                _timerResolutionRaised = false;
            }
        }

        private void WatchdogCheck()
        {
            try
            {
                if (!_ignitionState)
                {
                    double sinceCut = (DateTime.UtcNow - _lastCut).TotalMilliseconds;
                    if (sinceCut > CutMs + WatchdogMarginMs)
                    {
                        ForceRestoreIgnitionIfNeeded("watchdog");
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Rev limiter watchdog", ex);
            }
        }

        private void ForceRestoreIgnitionIfNeeded(string reason)
        {
            if (!Monitor.TryEnter(_restoreLock)) return;

            try
            {
                bool needsRestore;
                lock (_lock)
                {
                    needsRestore = !_ignitionState;
                }

                if (!needsRestore) return;

                Error?.Invoke(string.Format(Localization.T("status.revlimiter.forced_restore"), reason));

                if (RestoreIgnitionLocked(attempts: 3))
                {
                    lock (_lock)
                    {
                        WatchdogRecoveries++;
                    }
                    CutEnded?.Invoke();
                }
            }
            finally
            {
                Monitor.Exit(_restoreLock);
            }
        }

        private bool RestoreIgnitionLocked(int attempts)
        {
            bool needsRestore;
            lock (_lock)
            {
                needsRestore = !_ignitionState;
            }

            if (!needsRestore) return true;

            bool restored = false;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (TryPressIgnitionKey())
                {
                    restored = true;
                    break;
                }

                Thread.Sleep(50);
            }

            lock (_lock)
            {
                if (restored)
                {
                    _ignitionState = true;
                    IsCutting = false;
                }
                _lastCut = DateTime.UtcNow;
            }

            return restored;
        }
        public void ProcessOutGaugeData(OutGaugeData data)
        {
            if (Enabled && data.Valid && !IsCutting)
                CheckRpm(data.RPM, data.Throttle);
        }

        private void CheckRpm(float rpm, float throttle)
        {
            CooldownMs = Math.Max(Math.Min((CutMs / 2), 55), 20);

            if (throttle < 0.20f) return;
            if (rpm <= RpmLimit - 100) return;
            if (rpm <= RpmLimit && _ignitionState) return;
            lock (_lock)
            {
                if (IsCutting) return;
                double gap = (DateTime.UtcNow - _lastCut).TotalMilliseconds;
                if (gap < CooldownMs) return;

                IsCutting = true;
                CutCount++;
            }

            Task.Run(() => DoCut());
        }

        private static void PreciseWait(int ms)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long ticks = ms * System.Diagnostics.Stopwatch.Frequency / 1000;
            long coarseMargin = 2 * System.Diagnostics.Stopwatch.Frequency / 1000;

            while (true)
            {
                long remaining = ticks - (System.Diagnostics.Stopwatch.GetTimestamp() - start);
                if (remaining <= 0) return;

                if (remaining > coarseMargin)
                    HighResolutionWaiter.Sleep(1);
                else
                    Thread.SpinWait(50);
            }
        }

        private void DoCut()
        {
            CutStarted?.Invoke();

            bool weTurnedItOff = false;

            try
            {
                lock (_lock)
                {
                    if (_ignitionState)
                    {
                        _ignitionState = false;
                        weTurnedItOff = true;
                        _lastCut = DateTime.UtcNow;
                    }
                }

                if (weTurnedItOff && !TryPressIgnitionKey())
                {
                    lock (_lock)
                    {
                        _ignitionState = true;
                    }
                    return;
                }

                PreciseWait(CutMs);
            }
            finally
            {
                bool restored;
                lock (_restoreLock)
                {
                    restored = RestoreIgnitionLocked(attempts: 1);
                }

                if (restored)
                    CutEnded?.Invoke();
            }
        }

        private bool TryPressIgnitionKey()
        {
            IntPtr hwnd = LfsWindow.Find();
            if (hwnd == IntPtr.Zero)
            {
                Error?.Invoke(Localization.T("status.revlimiter.no_window"));
                return false;
            }

            if (!LfsInputGuard.Check(PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_I, (IntPtr)0x00170001)))
            {
                Error?.Invoke(Localization.T("status.revlimiter.key_failed"));
                return false;
            }

            Thread.Sleep(40);

            if (!PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_I, unchecked((IntPtr)0xC0170001)))
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_I, unchecked((IntPtr)0xC0170001));

            return true;
        }

        public void Dispose()
        {
            Stop();
            _watchdogTimer?.Dispose();
        }
    }
}