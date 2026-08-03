using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftBuddy
{
    // =========================================================
    //  RevLimiter — OutGauge UDP reader + ignition cut via WinAPI
    //
    //  Wymaga w LFS cfg.txt:
    //    OutGauge Mode 2
    //    OutGauge Delay 1
    //    OutGauge IP 127.0.0.1
    //    OutGauge Port 35555
    //    OutGauge ID 1
    // =========================================================

    public class OutGaugeData
    {
        public float RPM { get; set; }
        public float Speed { get; set; }   // m/s
        public float Throttle { get; set; }   // 0..1
        public float Gear { get; set; }
        public float EngTemp { get; set; }
        public float Fuel { get; set; }

        public uint DashLights { get; set; }   // jakie kontrolki auto W OGÓLE ma (stałe)
        public uint ShowLights { get; set; }   // ← NOWE: które kontrolki są TERAZ zapalone
        public bool Valid { get; set; }

        // DL_HANDBRAKE = bit 2 (wartość 4) — sprawdzamy ShowLights, nie DashLights!
        public bool HandbrakeOn => (ShowLights & 0x0004) != 0;
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
        private const int VK_I = 0x49;  // klawisz "I" – zapłon w LFS

        // ── Settings (publiczne, można zmieniać z UI) ─────────
        public int RpmLimit { get; set; } = 7500;   // RPM przy którym tnie
        public int CutMs { get; set; } = 40;     // czas wyłączenia zapłonu [ms]
        public int CooldownMs { get; set; } = 30;     // minimalny czas między cięciami
        public bool Enabled { get; set; } = true;
        public int UdpPort { get; set; } = 35555;

        // margines bezpieczeństwa dla watchdoga — jeśli minęło więcej niż CutMs + to,
        // a silnik wciąż jest zgaszony, watchdog wymusza zapłon niezależnie od tego,
        // co się stało z głównym cyklem cięcia (lag, zgubiony pakiet, wyjątek itp.)
        public int WatchdogMarginMs { get; set; } = 200;

        // ── State ─────────────────────────────────────────────
        public OutGaugeData LastData { get; private set; } = new OutGaugeData();
        public bool IsCutting { get; private set; } = false;

        public float lastRPM { get; private set; } = 0;

        private volatile bool _ignitionState = true;  // zakładamy że silnik jest włączony na start
        public bool IgnitionOn => _ignitionState;
        public bool IsRunning { get; private set; } = false;
        public int CutCount { get; private set; } = 0;   // ile razy uciął

        // ile razy watchdog musiał ratować sytuację — przydatne do debugowania,
        // jeśli ktoś zgłosi że silnik regularnie "się gubi"
        public int WatchdogRecoveries { get; private set; } = 0;

        // ── Events ────────────────────────────────────────────
        public event Action<OutGaugeData>? DataReceived;   // każdy pakiet UDP
        public event Action? CutStarted;     // zapłon wyłączony
        public event Action? CutEnded;       // zapłon przywrócony
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

                // watchdog działa NIEZALEŻNIE od pętli UDP — nawet jeśli LFS przestanie
                // wysyłać pakiety (lag, freeze), watchdog i tak sprawdzi stan zapłonu
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

            // jeśli program się zatrzymuje w trakcie cięcia, nie zostawiaj silnika zgaszonego
            ForceRestoreIgnitionIfNeeded("Stop()");

            IsCutting = false;
            LastData = new OutGaugeData();
        }

        // ─────────────────────────────────────────────────────
        //  Watchdog — pilnuje, żeby silnik nigdy nie został
        //  zgaszony na dłużej niż powinien
        // ─────────────────────────────────────────────────────
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

        // wspólna, bezpieczna ścieżka przywracania zapłonu — używana zarówno przez
        // watchdog, jak i przez Stop(), żeby nie duplikować logiki
        private void ForceRestoreIgnitionIfNeeded(string reason)
        {
            bool needsRestore;
            lock (_lock)
            {
                needsRestore = !_ignitionState;
            }

            if (!needsRestore) return;

            Error?.Invoke($"Rev limiter: wymuszam przywrócenie zapłonu ({reason}) — silnik utknął zgaszony.");

            // próbuj kilka razy — jeśli okno LFS chwilowo nie odpowiada (np. przez
            // sam lag który spowodował problem), pierwsza próba może nie wystarczyć
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

        // ─────────────────────────────────────────────────────
        //  UDP receive loop
        // ─────────────────────────────────────────────────────
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
                        continue;   // timeout – normalny stan gdy LFS nie wysyła
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Error?.Invoke("OutGauge error: " + ex.Message);
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  RPM check & cut trigger
        // ─────────────────────────────────────────────────────
        private void CheckRpm(float rpm, float throttle)
        {
            CooldownMs = Math.Max(Math.Min((CutMs / 2), 55), 20);

            if (throttle < 0.20f) return;   // tylko przy wciśniętym gazie
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

            // Uruchom asynchronicznie żeby nie blokować pętli UDP
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
                // finally gwarantuje, że nawet gdy powyżej wyleci wyjątek,
                // spróbujemy przywrócić zapłon i zwolnić stan IsCutting —
                // watchdog i tak by to naprawił, ale nie ma sensu czekać
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

        // ─────────────────────────────────────────────────────
        //  Wysyłanie klawisza "I" do okna LFS
        // ─────────────────────────────────────────────────────
        // zwraca false, jeśli nie udało się znaleźć okna LFS — pozwala wywołującemu
        // wiedzieć, że warto spróbować ponownie (patrz ForceRestoreIgnitionIfNeeded)
        private bool TryPressIgnitionKey()
        {
            IntPtr hwnd = FindLfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                Error?.Invoke("Nie znaleziono okna LFS (rev limiter)");
                return false;
            }

            // PostMessage jest nieblokujące i działa nawet gdy LFS jest w tle
            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_I, (IntPtr)0x00170001);
            Thread.Sleep(40);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_I, (IntPtr)0xC0170001);
            return true;
        }

        private static IntPtr FindLfsWindow()
        {
            // LFS używa różnych tytułów zależnie od wersji i trybu
            string[] titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };
            foreach (var t in titles)
            {
                IntPtr h = FindWindow(null, t);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }

        // ─────────────────────────────────────────────────────
        //  OutGauge packet parser
        //
        //  Struktura (96 bajtów, little-endian):
        //  Offset  0 : uint   Time
        //  Offset  4 : char[4] Car
        //  Offset  8 : ushort Flags
        //  Offset 10 : byte   Gear
        //  Offset 11 : byte   PLID
        //  Offset 12 : float  Speed  (m/s)
        //  Offset 16 : float  RPM
        //  Offset 20 : float  Turbo
        //  Offset 24 : float  EngTemp
        //  Offset 28 : float  Fuel
        //  Offset 32 : float  OilPress
        //  Offset 36 : float  OilTemp
        //  Offset 40 : uint   DashLights
        //  Offset 44 : uint   ShowLights
        //  Offset 48 : float  Throttle
        //  Offset 52 : float  Brake
        //  Offset 56 : float  Clutch
        //  Offset 60 : char[16] Display1
        //  Offset 76 : char[16] Display2
        //  Offset 92 : int    ID  (opcjonalne)
        // ─────────────────────────────────────────────────────
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
                DashLights = d.Length >= 44 ? BitConverter.ToUInt32(d, 40) : 0u,
                ShowLights = d.Length >= 48 ? BitConverter.ToUInt32(d, 44) : 0u, // ← NOWE
                Valid = true,
            };
        }

        public void Dispose()
        {
            Stop();
            _watchdogTimer?.Dispose();
        }
    }
}