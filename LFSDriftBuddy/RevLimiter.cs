using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftBuddy
{

    public class RevLimiter : IDisposable
    {

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

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
        public bool IgnitionOn => _ignitionState;
        public bool IsRunning { get; private set; } = false;
        public int CutCount { get; private set; } = 0;

        public int WatchdogRecoveries { get; private set; } = 0;

        public event Action? CutStarted;
        public event Action? CutEnded;
        public event Action<string>? Error;

        private DateTime _lastCut = DateTime.MinValue;
        private readonly object _lock = new();
        private System.Threading.Timer? _watchdogTimer;

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
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
        }

        private void WatchdogCheck()
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

        private void ForceRestoreIgnitionIfNeeded(string reason)
        {
            bool needsRestore;
            lock (_lock)
            {
                needsRestore = !_ignitionState;
            }

            if (!needsRestore) return;

            Error?.Invoke(string.Format(Localization.T("status.revlimiter.forced_restore"), reason));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryPressIgnitionKey())
                    break;

                Thread.Sleep(50);
            }

            lock (_lock)
            {
                _ignitionState = true;
                IsCutting = false;
                _lastCut = DateTime.UtcNow;
                WatchdogRecoveries++;
            }

            CutEnded?.Invoke();
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

        private async Task DoCut()
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

                if (weTurnedItOff)
                    TryPressIgnitionKey();

                await Task.Delay(CutMs);
            }
            finally
            {
                bool needsRestore;
                lock (_lock)
                {
                    needsRestore = !_ignitionState;
                }

                if (needsRestore)
                    TryPressIgnitionKey();

                lock (_lock)
                {
                    _ignitionState = true;
                    IsCutting = false;
                    _lastCut = DateTime.UtcNow;
                }

                CutEnded?.Invoke();
            }
        }

        private bool TryPressIgnitionKey()
        {
            IntPtr hwnd = FindLfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                Error?.Invoke(Localization.T("status.revlimiter.no_window"));
                return false;
            }

            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_I, (IntPtr)0x00170001);
            Thread.Sleep(40);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_I, (IntPtr)0xC0170001);
            return true;
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

        public void Dispose()
        {
            Stop();
            _watchdogTimer?.Dispose();
        }
    }
}