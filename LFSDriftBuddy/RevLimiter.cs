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

        // Krótka nazwa auta (np. "XFG", "FXO", "XRT") — to samo pole co IS_NPL.CName,
        // ale dostępne od razu z każdego pakietu OutGauge, bez dodatkowego kablowania InSim.
        // Używane do wykrywania zmiany pojazdu (patrz MainForm — ustawienia rev limitera per auto).
        public string Car { get; set; } = "";

        public uint DashLights { get; set; }   // jakie kontrolki auto W OGÓLE ma (stałe)
        public uint ShowLights { get; set; }   // ← NOWE: które kontrolki są TERAZ zapalone
        public bool Valid { get; set; }

        // Bity ShowLights wg specyfikacji OutGauge (DL_*):
        // DL_SHIFT=1, DL_FULLBEAM=2, DL_HANDBRAKE=4, DL_PITSPEED=8, DL_TC=16,
        // DL_SIGNAL_L=32, DL_SIGNAL_R=64, DL_SIGNAL_ANY=128, DL_OILWARN=256,
        // DL_BATTERY=512, DL_ABS=1024

        // DL_HANDBRAKE = bit 2 (wartość 4) — sprawdzamy ShowLights, nie DashLights!
        public bool HandbrakeOn => (ShowLights & 0x0004) != 0;

        // DL_SIGNAL_L / DL_SIGNAL_R — realny stan lewego/prawego kierunkowskazu na desce
        // rozdzielczej LFS (a nie nasza wewnętrzna intencja z IndicatorManager.CurrentState).
        public bool LeftSignalOn => (ShowLights & 0x0020) != 0;
        public bool RightSignalOn => (ShowLights & 0x0040) != 0;

        // DL_SIGNAL_ANY — LFS zapala ten bit gdy którykolwiek kierunkowskaz (lub awaryjne) miga.
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
                Car = d.Length >= 8 ? ParseCarName(d, 4) : "",   // ← NOWE
                DashLights = d.Length >= 44 ? BitConverter.ToUInt32(d, 40) : 0u,
                ShowLights = d.Length >= 48 ? BitConverter.ToUInt32(d, 44) : 0u, // ← NOWE
                Valid = true,
            };
        }

        // Car[4] w OutGaugePack ma DWIE różne interpretacje w zależności od typu auta:
        //
        //  • auta OFICJALNE — krótki tekstowy kod, litery A-Z / cyfry, zakończony zerem
        //    w polu 4-bajtowym (np. "XFG\0", "FZ5\0") albo wypełniający je całkowicie bez
        //    terminatora (np. "MRT5").
        //  • auta ZMODOWANE — LFS NIE wpisuje tu tekstu, tylko surowe bajty identyfikatora
        //    moda/skina (ten sam numer, który gra pokazuje jako hex w przeglądarce modów,
        //    np. "8C3894"). Poprzednia wersja tej metody filtrowała bajty do zakresu
        //    drukowalnych znaków ASCII, co dla takich danych PRZYPADKOWO "trafiało" pojedynczy
        //    bajt mieszczący się w tym zakresie (np. bajt 0x38 z "8C3894" to akurat ASCII '8'),
        //    gubiąc resztę i zapisując bezsensowny jednoznakowy klucz w JSON-ie.
        //
        // Rozwiązanie: sprawdzamy, czy WSZYSTKIE bajty przed terminatorem są literami/cyframi
        // (czyli wyglądają jak prawdziwy kod auta) — jeśli tak, dekodujemy jako tekst jak
        // dotychczas. Jeśli nie, to na 100% mod — kodujemy WSZYSTKIE 4 surowe bajty jako hex
        // (bez ucinania na zerze, bo 0x00 może być pełnoprawną częścią identyfikatora binarnego,
        // nie terminatorem). Daje to stabilny, unikalny i czytelny klucz zgodny z tym, jak sam
        // LFS identyfikuje mody.
        private static string ParseCarName(byte[] d, int offset)
        {
            if (d[offset] == 0) return "";   // pole jeszcze puste — brak danych o aucie

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