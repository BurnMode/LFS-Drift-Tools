using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftBuddy
{
    // RevLimiter — OutGauge UDP reader + ignition cut via WinAPI.
    // Requires in LFS cfg.txt: OutGauge Mode 2, Delay 1, IP 127.0.0.1, Port 35555, ID 1.

    public class OutGaugeData
    {
        public float RPM { get; set; }
        public float Speed { get; set; }   // m/s
        public float Throttle { get; set; }   // 0..1
        public float Gear { get; set; }
        public float EngTemp { get; set; }
        public float Fuel { get; set; }

        // Short car code ("XFG", "FXO"...) — same field as IS_NPL.CName, but available from
        // every OutGauge packet without wiring up InSim. Used to detect a car change.
        public string Car { get; set; } = "";

        public uint DashLights { get; set; }   // which lights the car HAS at all (fixed)
        public uint ShowLights { get; set; }   // which lights are lit RIGHT NOW
        public bool Valid { get; set; }

        // ShowLights bits (OutGauge DL_*): SHIFT=1, FULLBEAM=2, HANDBRAKE=4, PITSPEED=8, TC=16,
        // SIGNAL_L=32, SIGNAL_R=64, SIGNAL_ANY=128, OILWARN=256, BATTERY=512, ABS=1024.
        public bool HandbrakeOn => (ShowLights & 0x0004) != 0;

        // Real dashboard turn-signal state, not our own toggle intent.
        public bool LeftSignalOn => (ShowLights & 0x0020) != 0;
        public bool RightSignalOn => (ShowLights & 0x0040) != 0;

        // Set when any signal (or hazards) is blinking, for cars without separate L/R bits.
        public bool AnySignalOn => (ShowLights & 0x0080) != 0;
    }

    public class RevLimiter : IDisposable
    {
        // ── WinAPI ────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_I = 0x49;  // "I" — ignition key in LFS

        // ── Settings (public, UI-adjustable) ──────────────────
        public int RpmLimit { get; set; } = 7500;
        public int CutMs { get; set; } = 40;      // ignition-off duration [ms]
        public int CooldownMs { get; set; } = 30;      // min. time between cuts
        public bool Enabled { get; set; } = true;

        public int UdpPort { get; set; } = 35555;

        // Safety margin for the watchdog — if more than CutMs + this has passed and the
        // engine is still off, the watchdog forces ignition back on regardless of what
        // happened to the main cut cycle (lag, dropped packet, exception, ...).
        public int WatchdogMarginMs { get; set; } = 200;

        // ── State ─────────────────────────────────────────────
        public OutGaugeData LastData { get; private set; } = new OutGaugeData();
        public bool IsCutting { get; private set; } = false;

        private volatile bool _ignitionState = true;  // assume engine on at start
        public bool IgnitionOn => _ignitionState;
        public bool IsRunning { get; private set; } = false;
        public int CutCount { get; private set; } = 0;

        // How many times the watchdog had to recover — useful for debugging reports of
        // the engine getting stuck off.
        public int WatchdogRecoveries { get; private set; } = 0;

        // ── Events ────────────────────────────────────────────
        public event Action<OutGaugeData>? DataReceived;
        public event Action? CutStarted;
        public event Action? CutEnded;
        public event Action<string>? Error;

        // ── Internals ─────────────────────────────────────────
        private UdpClient? _udp;
        private CancellationTokenSource _cts = new();
        private DateTime _lastCut = DateTime.MinValue;
        private readonly object _lock = new();
        private System.Threading.Timer? _watchdogTimer;

        // ─────────────────────────────────────────────────────
        //  Start / Stop
        // ─────────────────────────────────────────────────────
        public void Start()
        {
            if (IsRunning) return;
            try
            {
                _cts = new CancellationTokenSource();
                _udp = new UdpClient(UdpPort);
                _udp.Client.ReceiveTimeout = 2000;
                IsRunning = true;
                Task.Run(() => ReceiveLoop(_cts.Token));

                // Watchdog runs independently of the UDP loop — even if LFS stops sending
                // packets (lag, freeze), it still checks ignition state.
                _watchdogTimer = new System.Threading.Timer(
                    _ => WatchdogCheck(),
                    null,
                    dueTime: 100,
                    period: 100);
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Nie można otworzyć UDP {UdpPort}: {ex.Message}");
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _cts.Cancel();
            _udp?.Close();
            _udp = null;
            IsRunning = false;

            _watchdogTimer?.Dispose();
            _watchdogTimer = null;

            // Don't leave the engine cut if stopping mid-cut.
            ForceRestoreIgnitionIfNeeded("Stop()");

            IsCutting = false;
            LastData = new OutGaugeData();
        }

        // ── Watchdog: makes sure the engine never stays cut longer than it should ──
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

        // Shared ignition-restore path used by both the watchdog and Stop().
        private void ForceRestoreIgnitionIfNeeded(string reason)
        {
            bool needsRestore;
            lock (_lock)
            {
                needsRestore = !_ignitionState;
            }

            if (!needsRestore) return;

            Error?.Invoke($"Rev limiter: wymuszam przywrócenie zapłonu ({reason}) — silnik utknął zgaszony.");

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

        // ── UDP receive loop ──────────────────────────────────
        private void ReceiveLoop(CancellationToken ct)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] data = _udp!.Receive(ref ep);
                    if (data.Length < 64) continue;

                    var og = ParseOutGauge(data);
                    LastData = og;
                    DataReceived?.Invoke(og);

                    if (Enabled && og.Valid && !IsCutting)
                        CheckRpm(og.RPM, og.Throttle);
                }
                catch (SocketException)
                {
                    if (!ct.IsCancellationRequested)
                        continue;   // timeout — normal when LFS isn't sending
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Error?.Invoke("OutGauge error: " + ex.Message);
                }
            }
        }

        // ── RPM check & cut trigger ───────────────────────────
        private void CheckRpm(float rpm, float throttle)
        {
            CooldownMs = Math.Max(Math.Min((CutMs / 2), 55), 20);

            if (throttle < 0.20f) return;   // only while on throttle
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

            // Run async so the UDP loop isn't blocked.
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

        // Sends the "I" ignition key to the LFS window. Returns false if the window wasn't
        // found, so callers know a retry may be worth it.
        private bool TryPressIgnitionKey()
        {
            IntPtr hwnd = FindLfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                Error?.Invoke("Nie znaleziono okna LFS (rev limiter)");
                return false;
            }

            // PostMessage is non-blocking and works even while LFS is in the background.
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

        // OutGauge packet, 96 bytes little-endian: Time(4) Car(4) Flags(2) Gear(1) PLID(1)
        // Speed(4) RPM(4) Turbo(4) EngTemp(4) Fuel(4) OilPress(4) OilTemp(4) DashLights(4)
        // ShowLights(4) Throttle(4) Brake(4) Clutch(4) Display1(16) Display2(16) ID(4, optional).
        private static OutGaugeData ParseOutGauge(byte[] d)
        {
            return new OutGaugeData
            {
                RPM = BitConverter.ToSingle(d, 16),
                Speed = BitConverter.ToSingle(d, 12),
                Throttle = d.Length >= 52 ? BitConverter.ToSingle(d, 48) : 0f,
                Gear = d[10],
                EngTemp = d.Length >= 28 ? BitConverter.ToSingle(d, 24) : 0f,
                Fuel = d.Length >= 32 ? BitConverter.ToSingle(d, 28) : 0f,
                Car = d.Length >= 8 ? ParseCarName(d, 4) : "",
                DashLights = d.Length >= 44 ? BitConverter.ToUInt32(d, 40) : 0u,
                ShowLights = d.Length >= 48 ? BitConverter.ToUInt32(d, 44) : 0u,
                Valid = true,
            };
        }

        // Car[4] means two different things depending on car type: official cars are a short
        // A-Z/0-9 text code ("XFG\0"); modded cars carry raw mod-id bytes instead (e.g. "8C3894"
        // as LFS shows it in the mod browser), which aren't text at all. So: if every byte before
        // the terminator looks like a letter/digit, decode as text; otherwise it's a mod — encode
        // all 4 raw bytes as hex (no truncation at 0x00, which can be a real part of a mod id).
        private static string ParseCarName(byte[] d, int offset)
        {
            if (d[offset] == 0) return "";

            int len = 0;
            while (len < 4 && d[offset + len] != 0) len++;

            bool looksLikeOfficialCode = len > 0;
            for (int i = 0; i < len && looksLikeOfficialCode; i++)
            {
                byte b = d[offset + i];
                looksLikeOfficialCode = (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'0' && b <= (byte)'9');
            }

            if (looksLikeOfficialCode)
                return System.Text.Encoding.ASCII.GetString(d, offset, len);

            return BitConverter.ToString(d, offset, 4).Replace("-", "");
        }

        public void Dispose()
        {
            Stop();
            _watchdogTimer?.Dispose();
        }
    }
}