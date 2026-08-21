using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static System.Formats.Asn1.AsnWriter;

namespace LFSDriftBuddy
{
    public class OverlayForm : LayeredForm
    {
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly System.Windows.Forms.Timer _trackTimer;
        private Bitmap _buffer;

        private readonly PrivateFontCollection _fontCollection = new();

        private Font _idleFont24;
        private Font _idleFont26;
        private Font _idleFont32;
        private Font _activeFont22;
        private Font _activeFont24;
        private Font _activeFont32;
        private Font _activeFont42;
        private Font _activeFont20;
        private Font _activeFont17;
        private Font _activeFont15;
        private Font _activeFont11;
        // Cached fonts for DrawSpeedoTachoContent (60fps loop) — avoid per-frame Font allocation.
        private Font _tachoTickFont;
        private Font _tachoGearFont;
        private Font _tachoSpeedFont;
        private Font _tachoUnitFont;


        private void LoadFonts()
        {
            string fontPath = Path.Combine(Application.StartupPath, "Fonts");

            //_fontCollection.AddFontFile(Path.Combine(fontPath, "idle.ttf"));
            _fontCollection.AddFontFile(Path.Combine(fontPath, "active.otf"));

            //FontFamily idle = _fontCollection.Families[1];
            FontFamily active = _fontCollection.Families[0];


            _idleFont24 = new Font(active, 24f, FontStyle.Bold);
            _idleFont26 = new Font(active, 26f, FontStyle.Bold);
            _idleFont32 = new Font(active, 32f, FontStyle.Bold);

            _activeFont11 = new Font(active, 11f, FontStyle.Regular);
            _activeFont24 = new Font(active, 24f, FontStyle.Bold);
            _activeFont22 = new Font(active, 22f, FontStyle.Bold);
            _activeFont32 = new Font(active, 32f, FontStyle.Bold);
            _activeFont42 = new Font(active, 42f, FontStyle.Bold);
            _activeFont20 = new Font(active, 20f, FontStyle.Bold);
            _activeFont17 = new Font(active, 17f, FontStyle.Bold);
            _activeFont15 = new Font(active, 15f, FontStyle.Bold);

            _tachoTickFont = new Font(active, 12f, FontStyle.Bold);
            _tachoGearFont = new Font(active, 26f, FontStyle.Bold);
            _tachoSpeedFont = new Font(active, 40f, FontStyle.Bold);
            _tachoUnitFont = new Font(active, 11f, FontStyle.Bold);
        }

        private IntPtr _targetHwnd = IntPtr.Zero;

        // ── stały kolor tekstu labelki/bonusu (nie zmienia się z InSimColor) ──
        private static readonly Color LabelTextColor = Color.White;

        // ── dynamiczny kolor (InSimColor1..5 wg kąta driftu) ────
        private Color _accentColor = Color.FromArgb(255, 235, 30);
        private Color _accentColorBack = Color.FromArgb(225, 205, 0);
        public void UpdateAccentColor(Color c)
        {
            _accentColor = c;
            _accentColorBack = Color.FromArgb(
                c.A,
                (int)(c.R * 0.35),
                (int)(c.G * 0.35),
                (int)(c.B * 0.35)
            );
        }

        private class ComboCharAnim
        {
            public char Current;
            public char Previous;
            public DateTime Start;
            public bool Animating;
        }




        private readonly List<ComboCharAnim> _comboChars = new();
        private string _comboDisplayed = "";
        private const double ComboAnimMs = 140;


        // ── czas trwania combo (z DriftEngine.COMBO_TIMEOUT_SEC) ──
        public double ComboTimeoutSec { get; set; } = 3.0;

        // ── total score ──────────────────────────────────────
        private double _displayedScore = 0;
        private long _targetScore = 0;
        private double _displayedAngle = 0;
        private double _displayedLapScore = 0;
        private long _targetLapScore = 0;
        private long _bestLapScore = 0;
        private string _lapContextLabel = "";   // ← NOWE: skrót mapy_trasy_layoutu
        private bool _lapBoxVisible = false;                 // ← NOWE
        private double _lapBoxBlend = 0;
        private double _lapBoxBlendFrom = 0, _lapBoxBlendTo = 0;
        private DateTime _lapBoxBlendStart = DateTime.MinValue;
        private const int LapBoxBlendMs = 260;// ← NOWE

        // ── obecność danych OutGauge (RPM/bieg/itd.) ────────────────────────────
        // OutGauge to OSOBNY strumień UDP od InSim/MCI — LFS wysyła go WYŁĄCZNIE gdy
        // gracz faktycznie siedzi w aucie na torze, nie w menu/garażu/powtórce bez
        // auta. Świeżość liczona z ostatniego wywołania NotifyOutGaugeData (patrz
        // MainForm.OnRevData — jeden telefon na każdy odebrany pakiet) względem
        // progu poniżej; brak świeżych danych blokuje: (1) prędkościomierz+obrotomierz
        // (patrz DrawSpeedoTacho — po prostu nie ma czego pokazać) i (2) przełączanie
        // HUD-u wyniku na "active" (patrz Tick — _isActiveNow liczone tu, nie ustawiane
        // wprost przez SetActive), bo drift/speeding liczą się z InSim MCI, który MOŻE
        // działać nawet bez OutGauge — bez tej blokady HUD punktacji przełączałby się
        // na "active" przy zerowych danych z silnika.
        private readonly DataFreshnessGate _outGaugeFreshness = new(TimeSpan.FromMilliseconds(1500));

        public void NotifyOutGaugeData() => _outGaugeFreshness.Ping();

        /// <summary>Wywołaj przy rozłączeniu z LFS, żeby stan nie "dogrywał" się jeszcze
        /// przez próg świeżości po AttachTo() przy kolejnym połączeniu.</summary>
        public void ResetOutGaugeData() => _outGaugeFreshness.Reset();

        private bool HasFreshOutGaugeData => _outGaugeFreshness.IsFresh;

        // ── aktywność (drift/speeding) + countdown combo ────
        // _isActiveNow to WYPADKOWA tego, co zgłasza SetActive (_activeRequested) ORAZ
        // świeżości danych OutGauge (patrz wyżej) — przeliczana co klatkę w Tick().
        private bool _activeRequested = false;
        private bool _isActiveNow = false;
        private double _comboRemainingSec = 0;
        private double _activeBlend = 0;
        private bool _angleVisTargetVisible = false;
        private bool _angleVisEntering = false;
        private DateTime _angleVisBlendStart = DateTime.MinValue;
        private const int AngleVisBlendMs = 220; // ta sama skala co BonusSlideMs
        private double _activeBlendFrom = 0, _activeBlendTo = 0;
        private DateTime _activeBlendStart;
        private const int ActiveBlendMs = 320;
        private DateTime _lastTickTime = DateTime.Now;

        private double _displayedRun = 0;
        private long _targetRun = 0;
        private double _combo = 1;
        private string _driftLabelText = "";

        // ── REV LIMITER CUT — czerwona poświata pod HUD-em ──────
        private bool _revCutActive = false;
        private double _revCutBlend = 0;
        private double _revCutBlendFrom = 0, _revCutBlendTo = 0;
        private DateTime _revCutBlendStart = DateTime.MinValue;
        private const int RevCutBlendMs = 220;

        // RPM z OutGauge — na razie tylko przechowywane, docelowo może zasilać kolejne animacje
        private int _currentRpm = 0;

        // ── Speedometer + Tachometer (Forza-style, prawy dolny róg) ─────────────────────
        // Ustawienia sterowane z UI (speedometerPanel w MainForm) — patrz odpowiednie property.
        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public Color RedlineColor { get; set; } = Color.Red;

        // Przełącznik jednostek prędkości (KM/H domyślnie / MPH) — sterowany z UI (mały
        // MacToggleSwitch w speedometerPanel). Wpływa TYLKO na cyfrową prędkość i etykietę
        // jednostki w tym HUD-zie — obrotomierz (RPM) jest niezależny od tego ustawienia.
        public bool SpeedoTachoUseMph { get; set; } = false;
        private const double KmhToMph = 0.621371;

        // ── Kolory elementów HUD-u prędkościomierz+obrotomierz — każdy z własnym RGB+A,
        // ustawiane z UI przez podmenu "HUD Colors" (patrz MainForm.ShowHudColorsMenu).
        public Color SpeedoTachoTextColor { get; set; } = Color.White;
        public Color SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);

        // Wartość "surowa", zasilana bezpośrednio z pakietów InSim (patrz UpdateSpeedGauge)
        // — przychodzi skokowo, w tempie telemetrii, więc NIE rysujemy jej wprost. Zamiast
        // tego to tylko cel ("target"), do którego w każdej klatce (patrz Tick()) płynnie
        // dogania się osobna wartość "wyświetlana" (_displayedSpeedKmh) — ten sam wzorzec
        // wygładzania co _displayedAngle/_displayedScore powyżej, żeby cyfry na HUD-zie nie
        // "skakały" między pakietami.
        private double _targetSpeedKmh = 0;
        private double _displayedSpeedKmh = 0;
        private int _currentGear = 0;          // OutGauge: 0=wsteczny, 1=jałowy, 2=1.bieg...
        private int _calibratedMaxRpm = 0;     // 0 = jeszcze nieznane -> domyślny zakres 0-10000

        public void UpdateSpeedGauge(double speedKmh) => _targetSpeedKmh = Math.Max(0, speedKmh);
        public void UpdateGear(int gear) => _currentGear = gear;
        public void UpdateMaxRpm(int maxRpm) => _calibratedMaxRpm = Math.Max(0, maxRpm);

        // ── bonus popup (przełożenie itp.) ───────────────────
        private class BonusItem
        {
            public string Text;
            public DateTime Start;
            public float DispW;
            public bool SizeInit;
            public float DispX;     // ← NOWE
            public bool PosInit;
        }
        private readonly List<BonusItem> _bonusItems = new();
        private const double BonusDurationSec = 2.0;
        private const double BonusSlideMs = 220;   // czas wjazdu/wyjazdu
        private const float BonusBoxGap = 8f;      // odstęp między sąsiednimi boxami

        // ── kąt driftu / strzałki ─────────────────────────────
        private double _driftAngle = 0;
        private bool _isDrifting = false;
        private bool _sideRight = true;

        // ── płynna animacja rozmiaru ramek (label/run box, bonus box) ──
        private bool _hudSizeInit = false;
        private float _dispLabelBoxW = 0, _dispRunBoxW = 0, _dispBarH = 0;
        private const float SizeSmoothFactor = 0.45f; // wyższe = szybsza reakcja

        private bool _lapResultVisible = false;
        private long _lapResultScore = 0;
        private DateTime _lapResultStart = DateTime.MinValue;
        private const double LapResultDurationSec = 5.0;
        private const double LapResultSlideMs = 260;

        public void ShowLapResult(long score)
        {
            _lapResultScore = score;
            _lapResultStart = DateTime.Now;
            _lapResultVisible = true;
        }

        public OverlayForm()
        {
            Width = 900;
            Height = 300;
            Opacity = 1.0;

            LoadFonts();

            _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _renderTimer.Tick += (s, e) => Tick();

            _trackTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _trackTimer.Tick += (s, e) => TrackTarget();
        }

        // ── API ───────────────────────────────────────────────
        public void AttachTo(IntPtr lfsHwnd)
        {
            _targetHwnd = lfsHwnd;
            _lastTickTime = DateTime.Now;
            _trackTimer.Start();
            _renderTimer.Start();
            Show();
            SetClickThrough(true);
        }

        public void Detach()
        {
            _targetHwnd = IntPtr.Zero;
            _trackTimer.Stop();
            _renderTimer.Stop();
            Hide();
        }
        public void SetRevCut(bool active)
        {
            if (_revCutActive == active) return;
            _revCutActive = active;
            _revCutBlendFrom = _revCutBlend;
            _revCutBlendTo = active ? 1.0 : 0.0;
            _revCutBlendStart = DateTime.Now;
        }

        /// <summary>RPM z OutGauge — na razie używane tylko do ew. przyszłych animacji reaktywnych na obroty.</summary>
        public void UpdateRpm(int rpm) => _currentRpm = rpm;
        private long _lastLapScoreDisplay = 0;    // ← NOWE
        public void UpdateLastLapScore(long score) => _lastLapScoreDisplay = score;   // ← NOWE
        public void UpdateScore(long total) => _targetScore = total;
        public void UpdateRun(long run) => _targetRun = run;
        public void UpdateLapScore(long lapScore) => _targetLapScore = lapScore;
        public void UpdateBestLapScore(long bestLapScore) => _bestLapScore = bestLapScore;

        public void UpdateLapContextLabel(string label) => _lapContextLabel = label ?? "";

        // Blokuje pokazywanie ramki wyniku okrążenia, gdy gracz jest w menu głównym gry
        // (przy IS_STA -> ISS_FRONT_END). Wywoływane z MainForm przy RaceStateChanged(false/true).
        private bool _inMenu = false;

        /// <summary>
        /// Ustawia tryb "w menu gry". Wejście w tryb menu natychmiast (bez 260ms animacji
        /// wygaszania) chowa ramkę wyniku okrążenia i blokuje jej ponowne pokazanie przez
        /// SetLapBoxVisible(true), dopóki tryb menu nie zostanie wyłączony — chroni to przed
        /// sytuacją, w której inne zdarzenie (np. TrackChanged) odpalone tuż przy przejściu
        /// do menu z powrotem pokazałoby ramkę zanim stan drifta zdąży się zaktualizować.
        /// </summary>
        public void SetMenuMode(bool inMenu)
        {
            _inMenu = inMenu;
            if (inMenu)
            {
                _lapBoxVisible = false;
                _lapBoxBlend = 0;
                _lapBoxBlendFrom = 0;
                _lapBoxBlendTo = 0;
            }
        }

        public void SetLapBoxVisible(bool visible)
        {
            if (_inMenu && visible) return;   // w menu nic nie pokazujemy, dopóki SetMenuMode(false)
            if (_lapBoxVisible == visible) return;
            _lapBoxVisible = visible;
            _lapBoxBlendFrom = _lapBoxBlend;
            _lapBoxBlendTo = visible ? 1.0 : 0.0;
            _lapBoxBlendStart = DateTime.Now;
        }
        public void UpdateCombo(double combo)
        {
            _combo = combo;

            string newText = $"x{combo:0.0}";

            if (_comboDisplayed == "")
            {
                _comboDisplayed = newText;

                _comboChars.Clear();

                foreach (char c in newText)
                {
                    _comboChars.Add(new ComboCharAnim
                    {
                        Current = c,
                        Previous = c
                    });
                }

                return;
            }

            while (_comboChars.Count < newText.Length)
                _comboChars.Insert(0, new ComboCharAnim());

            while (_comboChars.Count > newText.Length)
                _comboChars.RemoveAt(0);

            while (_comboDisplayed.Length < newText.Length)
                _comboDisplayed = " " + _comboDisplayed;

            while (_comboDisplayed.Length > newText.Length)
                _comboDisplayed = _comboDisplayed.Substring(1);

            for (int i = 0; i < newText.Length; i++)
            {
                if (_comboDisplayed[i] != newText[i])
                {
                    _comboChars[i].Previous = _comboDisplayed[i];
                    _comboChars[i].Current = newText[i];
                    _comboChars[i].Animating = true;
                    _comboChars[i].Start = DateTime.Now;
                }
            }

            _comboDisplayed = newText;
        }
        public void UpdateLabel(string label) => _driftLabelText = label ?? "";

        /// <summary>Wywoływane co klatkę telemetrii: czy TERAZ trwa drift/szybka jazda.
        /// Efektywny stan (_isActiveNow) dodatkowo wymaga świeżych danych OutGauge —
        /// patrz HasFreshOutGaugeData i Tick().</summary>
        public void SetActive(bool active) => _activeRequested = active;

        public void ShowBonus(string bonusText)
        {
            if (string.IsNullOrWhiteSpace(bonusText)) return;

            // nowy komunikat dokłada się z prawej strony, obok trwających — nie zastępuje ich
            _bonusItems.Add(new BonusItem
            {
                Text = bonusText,
                Start = DateTime.Now
            });
        }

        public void SetDriftAngle(double angle, bool drifting, bool sideRight)
        {
            _driftAngle = angle;
            _isDrifting = drifting;
            _sideRight = sideRight;
        }

        private void DrawLapResultPopup(Graphics g)
        {
            if (!_lapResultVisible) return;

            var now = DateTime.Now;
            double elapsed = (now - _lapResultStart).TotalSeconds;
            if (elapsed > LapResultDurationSec)
            {
                _lapResultVisible = false;
                return;
            }

            double slideInSec = LapResultSlideMs / 1000.0;
            double slideOutStart = LapResultDurationSec - slideInSec;

            double alpha, offsetX;

            if (elapsed < slideInSec)
            {
                double bt = elapsed / slideInSec;
                double eased = 1.0 - Math.Pow(1 - bt, 3);
                alpha = eased;
                offsetX = (1.0 - eased) * 240; // wjazd z prawej
            }
            else if (elapsed > slideOutStart)
            {
                double bt = (elapsed - slideOutStart) / slideInSec;
                double eased = Math.Pow(bt, 2);
                alpha = 1.0 - eased;
                offsetX = -eased * 240; // wyjazd w lewo
            }
            else
            {
                alpha = 1.0;
                offsetX = 0;
            }

            if (alpha <= 0.01) return;

            var titleFont = _activeFont20;
            var valueFont = _activeFont42;

            string title = Localization.T("hud.lastlapscore");
            string valueText = _lapResultScore.ToString("N0");

            var titleSize = g.MeasureString(title, titleFont);
            var valueSize = g.MeasureString(valueText, valueFont);

            float padV = 0;
            float centerX = Width / 2f + (float)offsetX;
            float boxY = 350;

            // ── wyśrodkowanie obu linii wokół wspólnej osi ──
            float titleX = centerX - titleSize.Width / 2f;
            float titleY = boxY + titleSize.Height / 2;

            float valueX = centerX - valueSize.Width / 2f;
            float valueY = boxY + titleSize.Height + padV;


            if (_lapResultScore >= 250)
            {
                // ── tytuł: statyczny, wyśrodkowany ──
                DrawOutlinedText(g, title, titleFont, titleX, titleY,
                    WithAlpha(Color.White, alpha * 0.85), WithAlpha(Color.Black, alpha), outline: false);

                // ── wartość: wyśrodkowana, z przesuwającym się blaskiem w kształcie cyfr ──
                DrawShimmerText(g, valueText, valueFont, valueX, valueY, alpha, elapsed, Color.White, Color.Gray);
            }
            else
            {
                valueSize = g.MeasureString("START", valueFont);
                valueX = centerX - valueSize.Width / 2f;
                DrawShimmerText(g, "START", valueFont, valueX, valueY, alpha, elapsed, Color.Yellow, Color.Gray);
            }
        }

        // ── tekst wypełniony akcentem + suwający się "shine" zamaskowany kształtem liter,
        //    dokładnie jak pasek postępu w Windows 7 ──
        private void DrawShimmerText(Graphics g, string text, Font font, float x, float y, double alpha, double elapsedSec, Color color, Color colorBack)
        {
            if (string.IsNullOrEmpty(text)) return;

            // AddString wymaga rozmiaru czcionki w tych samych jednostkach co Graphics (domyślnie piksele),
            // a Font.Size jest w punktach — konwertujemy przez DPI, żeby wyszło identycznie jak DrawString.
            float emPx = font.Size * g.DpiY / 72f;

            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, emPx, new PointF(x, y), StringFormat.GenericTypographic);

            var bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // cień pod spodem — dla czytelności na jasnym/ciemnym tle
            using (var shadowMatrix = new Matrix())
            {
                shadowMatrix.Translate(1.5f, 1.5f);
                using var shadowPath = (GraphicsPath)path.Clone();
                shadowPath.Transform(shadowMatrix);
                using var shadowBrush = new SolidBrush(WithAlpha(colorBack, alpha));
                g.FillPath(shadowBrush, shadowPath);
            }

            // bazowe, stałe wypełnienie liter kolorem akcentu
            using (var baseBrush = new SolidBrush(WithAlpha(color, alpha * 0.7f)))
                g.FillPath(baseBrush, path);

            // maska = dokładny kształt cyfr — w niej suwa się jasne pasmo
            var oldClip = g.Clip;
            g.SetClip(path, CombineMode.Replace);

            const double sweepCycleSec = 0.7;
            double sweepT = (elapsedSec % sweepCycleSec) / sweepCycleSec;
            float sweepWidth = bounds.Width * 0.65f;
            float sweepCenter = bounds.X - sweepWidth + (float)(sweepT * (bounds.Width + sweepWidth * 2));

            using (var sweepBrush = new LinearGradientBrush(
                new RectangleF(sweepCenter - sweepWidth, bounds.Y - 4, sweepWidth * 2, bounds.Height + 8),
                Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                var blend = new ColorBlend(3)
                {
                    Colors = new[]
                    {
                WithAlpha(Color.White, 0),
                WithAlpha(Color.White, alpha * 0.7),
                WithAlpha(Color.White, 0)
            },
                    Positions = new float[] { 0f, 0.5f, 1f }
                };
                sweepBrush.InterpolationColors = blend;
                g.FillRectangle(sweepBrush, sweepCenter - sweepWidth, bounds.Y - 4, sweepWidth * 2, bounds.Height + 8);
            }

            g.Clip = oldClip;
        }

        // ── śledzenie okna LFS ──────────────────────────────
        private void TrackTarget()
        {
            if (_targetHwnd == IntPtr.Zero || !GetWindowRect(_targetHwnd, out var r) || IsIconic(_targetHwnd))
            {
                if (Visible) Hide();
                return;
            }

            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;

            if (Left != r.Left || Top != r.Top || Width != w || Height != h)
                Bounds = new Rectangle(r.Left, r.Top, Math.Max(w, 1), Math.Max(h, 1));

            if (!Visible) Show();
            ForceTopmost();
        }

        // ── pętla animacji ───────────────────────────────────
        private void Tick()
        {
            var now = DateTime.Now;
            double dt = (now - _lastTickTime).TotalSeconds;
            if (dt > 0.25) dt = 0.25;
            _lastTickTime = now;

            // Bez świeżych danych OutGauge HUD wyniku jest zablokowany w trybie idle
            // (tylko łączny wynik) niezależnie od tego, co zgłasza SetActive — patrz
            // komentarz przy _activeRequested/HasFreshOutGaugeData powyżej.
            _isActiveNow = _activeRequested && HasFreshOutGaugeData;

            _displayedScore += (_targetScore - _displayedScore) * 0.18;
            if (Math.Abs(_targetScore - _displayedScore) < 1) _displayedScore = _targetScore;

            _displayedRun += (_targetRun - _displayedRun) * 0.25;
            if (Math.Abs(_targetRun - _displayedRun) < 1) _displayedRun = _targetRun;

            _displayedLapScore += (_targetLapScore - _displayedLapScore) * 0.2;   // ← NOWE
            if (Math.Abs(_targetLapScore - _displayedLapScore) < 1) _displayedLapScore = _targetLapScore;

            _displayedAngle += (_driftAngle - _displayedAngle) * 0.25;
            if (Math.Abs(_driftAngle - _displayedAngle) < 0.1) _displayedAngle = _driftAngle;

            // ── prędkościomierz: płynne dogonienie ostatniej wartości z telemetrii ──
            // Współczynnik dobrany tak, by przy 60 FPS (Interval=16ms) doganianie skoku
            // trwało ok. 150–200ms — wystarczająco płynnie, żeby ukryć nierówne tempo
            // pakietów OutGauge, ale bez zauważalnego opóźnienia względem rzeczywistej jazdy.
            _displayedSpeedKmh += (_targetSpeedKmh - _displayedSpeedKmh) * 0.3;
            if (Math.Abs(_targetSpeedKmh - _displayedSpeedKmh) < 0.05) _displayedSpeedKmh = _targetSpeedKmh;

            // ── countdown combo: pełny podczas aktywności, zlicza w dół gdy nie ──
            if (_isActiveNow)
                _comboRemainingSec = ComboTimeoutSec;
            else
                _comboRemainingSec = Math.Max(0, _comboRemainingSec - dt);


            bool angleVisible = _displayedAngle > 15.0 && _displayedAngle < 160.0 && _isDrifting;

            if (angleVisible != _angleVisTargetVisible)
            {
                _angleVisTargetVisible = angleVisible;
                _angleVisEntering = angleVisible;
                _angleVisBlendStart = now;
            }


            bool shouldShowActiveHud = _isActiveNow || _comboRemainingSec > 0;
            double targetBlend = shouldShowActiveHud ? 1.0 : 0.0;

            if (targetBlend != _activeBlendTo)
            {
                _activeBlendFrom = _activeBlend;
                _activeBlendTo = targetBlend;
                _activeBlendStart = now;
            }

            double t = Math.Min(1.0, (now - _activeBlendStart).TotalMilliseconds / ActiveBlendMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            _activeBlend = _activeBlendFrom + (_activeBlendTo - _activeBlendFrom) * eased;

            double revCutT = Math.Min(1.0, (now - _revCutBlendStart).TotalMilliseconds / RevCutBlendMs);
            double revCutEased = 1 - Math.Pow(1 - revCutT, 3);
            _revCutBlend = _revCutBlendFrom + (_revCutBlendTo - _revCutBlendFrom) * revCutEased;

            double lapBoxT = Math.Min(1.0, (now - _lapBoxBlendStart).TotalMilliseconds / LapBoxBlendMs);   // ← NOWE
            double lapBoxEased = 1 - Math.Pow(1 - lapBoxT, 3);
            _lapBoxBlend = _lapBoxBlendFrom + (_lapBoxBlendTo - _lapBoxBlendFrom) * lapBoxEased;

            Render();
        }

        private void Render()
        {
            if (Width <= 0 || Height <= 0) return;

            if (_buffer == null || _buffer.Width != Width || _buffer.Height != Height)
            {
                _buffer?.Dispose();
                _buffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            }

            using (var g = Graphics.FromImage(_buffer))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float centerX = Width / 2f;

                DrawIdleScore(g, centerX, out float idlebarY);
                DrawActiveHud(g, centerX, out float barX, out float barY, out float barW, out float barH);
                DrawBonusBox(g, centerX, barY + idlebarY, barH);
                DrawLapScoreBox(g);   // ← NOWE
                DrawLapResultPopup(g);
                DrawSpeedoTacho(g);   // ← NOWE: prędkościomierz + obrotomierz w stylu Forza
            }

            SetBitmap(_buffer);
        }

        // ── góra: sam total score, znika w lewo z fade gdy aktywny HUD wjeżdża ──
        private void DrawIdleScore(Graphics g, float centerX, out float idlebarY)
        {

            double alpha = 0.75 - _activeBlend;


            float slideX = (float)(-_activeBlend * 160);
            if (_isActiveNow) { slideX = (float)(-_activeBlend * 160); } else { slideX = (float)-(-_activeBlend * 160); }



            string text = ((long)Math.Round(_displayedScore)).ToString("N0");
            var font = _idleFont32;
            var size = g.MeasureString(text, font);
            idlebarY = size.Height;
            if (alpha <= 0.01) return;
            float x = centerX - size.Width / 2f + slideX;
            DrawRevCutGlow(g, new RectangleF(x, 5, size.Width, size.Height), alpha * 0.55);

            DrawOutlinedText(g, text, font, x, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);
        }

        // ── pasek aktywny: total+combo (góra) i ramka label|runscore (dół) ──
        private void DrawActiveHud(Graphics g, float centerX, out float barX, out float barY, out float barW, out float barH)
        {
            barX = barY = barW = barH = 0;
            double alphacut = 0.25;
            double alpha = _activeBlend;
            if (alpha <= 0.01) return;






            float slideX = (float)((1.0 - _activeBlend) * 220);
            if (_isActiveNow) { slideX = (float)((1.0 - _activeBlend) * 220); } else { slideX = (float)(-(1.0 - _activeBlend) * 220); }


            // ── total score + combo (ta sama pozycja co idle score) ──
            string scoreText = ((long)Math.Round(_displayedScore)).ToString("N0");
            var scoreFont = _activeFont32;
            var scoreSize = g.MeasureString(scoreText, scoreFont);

            var comboFont = _activeFont24;
            string comboText = $"x{_combo:0.0}";
            var comboSize = g.MeasureString(comboText, comboFont);


            float comboH = comboSize.Height;

            float totalW = scoreSize.Width;
            float totalH = scoreSize.Height;

            float startX = centerX - totalW / 2f + slideX;


            //DrawShimmerText(g, scoreText, scoreFont, startX, 5, alpha, 0, _accentColor, _accentColorBack);


            DrawOutlinedText(g, scoreText, scoreFont, startX, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);

            DrawRevCutGlow(g, new RectangleF(startX, 5, scoreSize.Width, scoreSize.Height), alpha * 0.55);

            // ── ramka: [ label (progress countdown) | run score (stały akcent) ] ──
            var labelFont = _activeFont22;
            var runFont = _activeFont22;

            string labelText = _driftLabelText ?? "";
            string runText = ((long)Math.Round(_displayedRun)).ToString("N0");

            var labelSize = g.MeasureString(labelText, labelFont);
            var runSize = g.MeasureString(runText, runFont);

            float padH = 7, padV = 4;
            float targetLabelBoxW = labelSize.Width + padH * 2;
            float targetRunBoxW = runSize.Width + padH * 2;
            float targetBarH = Math.Max(labelSize.Height, runSize.Height) + (padV * 2) - 1;






            if (!_hudSizeInit)
            {
                _dispLabelBoxW = targetLabelBoxW;
                _dispRunBoxW = targetRunBoxW;
                _dispBarH = targetBarH;
                _hudSizeInit = true;
            }
            else
            {
                _dispLabelBoxW += (targetLabelBoxW - _dispLabelBoxW) * SizeSmoothFactor;
                _dispRunBoxW += (targetRunBoxW - _dispRunBoxW) * SizeSmoothFactor;
                _dispBarH += (targetBarH - _dispBarH) * SizeSmoothFactor;
            }

            float labelBoxW = _dispLabelBoxW;
            float runBoxW = _dispRunBoxW;
            barH = _dispBarH;
            barW = labelBoxW + runBoxW;
            barX = centerX - barW / 2f + slideX;
            barY = 68;

            float glowLeft = Math.Min(startX, barX);
            float glowRight = Math.Max(startX + totalW, barX + barW + comboSize.Width + 10);



            double comboFraction = ComboTimeoutSec > 0 ? _comboRemainingSec / ComboTimeoutSec : 1.0;
            comboFraction = Math.Max(0, Math.Min(1, comboFraction));

            // ── lewy box: label, tło = progress bar (accent → czarny od prawej) ──
            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            {
                var oldClip = g.Clip;
                g.SetClip(labelPath, CombineMode.Replace);

                using (var fillBrush = new SolidBrush(WithAlpha(_accentColor, alpha - alphacut)))
                    g.FillRectangle(fillBrush, barX, barY, labelBoxW, barH);

                float consumed = labelBoxW * (1f - (float)comboFraction);
                if (consumed > 0.5f)
                {
                    using var darkBrush = new SolidBrush(WithAlpha(Color.Black, (alpha * 0.85) - alphacut));
                    g.FillRectangle(darkBrush, barX + labelBoxW - consumed, barY, consumed, barH);
                }

                g.Clip = oldClip;
            }

            // ── prawy box: run score, stały akcent (bez decayu) ──
            using (var runPath = RoundedRightRect(new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6))
            using (var runBrush = new SolidBrush(WithAlpha(Darken(_accentColor, 0.15), alpha - alphacut)))
                g.FillPath(runBrush, runPath);

            DrawAnimatedCombo(
                g,
                comboFont,
                comboText,
                barX + labelBoxW + runBoxW + 5,
                barY + (barH / 2) - (comboH / 2),
                WithAlpha(_accentColor, alpha),
                WithAlpha(_accentColorBack, alpha));


            double angleElapsedMs = (DateTime.Now - _angleVisBlendStart).TotalMilliseconds;
            double angleTNorm = Math.Min(1.0, Math.Max(0.0, angleElapsedMs / AngleVisBlendMs));

            double angleAlpha;
            double angleOffsetX;

            if (_angleVisEntering)
            {
                double eased = 1 - Math.Pow(1 - angleTNorm, 3);
                angleAlpha = eased;
                angleOffsetX = (1 - eased) * 60; // wjazd z prawej
            }
            else
            {
                double eased = Math.Pow(angleTNorm, 2);
                angleAlpha = 1 - eased;
                angleOffsetX = -eased * 60; // wyjazd w lewo
            }

            if (angleAlpha > 0.01)
            {
                string angleText = ((long)Math.Round(_displayedAngle)).ToString("N0") + "°";
                var angleFont = _activeFont24;
                var angleSize = g.MeasureString(angleText, angleFont);
                float angleW = angleSize.Width;
                float angleH = angleSize.Height;

                DrawOutlinedText(
                   g,
                   angleText,
                   angleFont,
                   barX - angleW - 2 + ((float)angleOffsetX / 2),
                   barY + (barH / 2) - (angleH / 2),
                   WithAlpha(_accentColor, alpha * angleAlpha),
                   WithAlpha(_accentColorBack, alpha * angleAlpha),
                   outline: false);
            }
            DrawOutlinedText(g, labelText, labelFont, barX + padH, barY + padV,
                WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);





            DrawOutlinedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV, WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);

            if (_isDrifting)
                DrawAngleArrows(g, startX, 5, scoreSize.Width, scoreSize.Height, alpha);
            barY = (barY - labelSize.Height - 8);

        }

        private void DrawAnimatedCombo(
    Graphics g,
    Font font,
    string text,
    float x,
    float y,
    Color fill,
    Color shadow)
        {
            float posX = x;

            for (int i = 0; i < _comboChars.Count; i++)
            {
                var c = _comboChars[i];

                string current = c.Current.ToString();
                const float LetterSpacing = -12f; // ujemna = ciaśniej, dodatnia = szerzej
                float w = g.MeasureString(current, font).Width + LetterSpacing;

                if (!c.Animating)
                {
                    DrawOutlinedText(
                        g,
                        current,
                        font,
                        posX,
                        y,
                        fill,
                        shadow,
                        outline: false);

                    posX += w;
                    continue;
                }

                double t = (DateTime.Now - c.Start).TotalMilliseconds / ComboAnimMs;

                if (t >= 1)
                {
                    c.Animating = false;

                    DrawOutlinedText(
                        g,
                        current,
                        font,
                        posX,
                        y,
                        fill,
                        shadow,
                        outline: false);

                    posX += w;
                    continue;
                }

                float h = font.Height;

                float oldY = y + (float)(t * h);
                float newY = y - h + (float)(t * h);

                Color oldColor = Color.FromArgb(
                    (int)((1.0 - t) * fill.A),
                    fill);

                Color newColor = Color.FromArgb(
                    (int)(t * fill.A),
                    fill);

                Color oldShadow = Color.FromArgb(
                    (int)((1.0 - t) * shadow.A),
                    shadow);

                Color newShadow = Color.FromArgb(
                    (int)(t * shadow.A),
                    shadow);

                DrawOutlinedText(
                    g,
                    c.Previous.ToString(),
                    font,
                    posX,
                    oldY,
                    oldColor,
                    oldShadow,
                    outline: false);

                DrawOutlinedText(
                    g,
                    current,
                    font,
                    posX,
                    newY,
                    newColor,
                    newShadow,
                    outline: false);

                posX += w;
            }

        }

        // ── osobna ramka pod labelem: bonus (przełożenie itp.), wjazd z prawej / wyjazd w lewo ──
        // ── osobna ramka pod labelem: bonusy (przełożenie itp.), stackowane obok siebie ──
        private void DrawBonusBox(Graphics g, float centerX, float labelBarY, float labelBarH)
        {
            double alphacut = 0.5;
            var now = DateTime.Now;

            _bonusItems.RemoveAll(it => (now - it.Start).TotalSeconds > BonusDurationSec);
            if (_bonusItems.Count == 0) return;

            var font = _activeFont15;
            const float padH = 7, padV = 4;
            double slideInSec = BonusSlideMs / 1000.0;
            double slideOutStart = BonusDurationSec - slideInSec;

            var widths = new float[_bonusItems.Count];
            var heights = new float[_bonusItems.Count];
            var alphas = new double[_bonusItems.Count];
            var slideOffsets = new double[_bonusItems.Count];

            float totalW = 0;
            for (int i = 0; i < _bonusItems.Count; i++)
            {
                var item = _bonusItems[i];
                var sz = g.MeasureString(item.Text, font);
                float targetW = sz.Width + padH * 2;
                float targetH = sz.Height + padV * 2 - 1;

                if (!item.SizeInit)
                {
                    item.DispW = targetW;
                    item.SizeInit = true;
                }
                else
                {
                    item.DispW += (targetW - item.DispW) * SizeSmoothFactor;
                }

                double elapsed = (now - item.Start).TotalSeconds;
                double alpha, slideOffset;

                if (elapsed < slideInSec)
                {
                    double bt = elapsed / slideInSec;
                    double eased = 1.0 - Math.Pow(1 - bt, 3);
                    alpha = eased;
                    slideOffset = (1.0 - eased) * 140;
                }
                else if (elapsed > slideOutStart)
                {
                    double bt = (elapsed - slideOutStart) / slideInSec;
                    double eased = Math.Pow(bt, 2);
                    alpha = 1.0 - eased;
                    slideOffset = -eased * 140;
                }
                else
                {
                    alpha = 1.0;
                    slideOffset = 0;
                }

                widths[i] = item.DispW;
                heights[i] = targetH;
                alphas[i] = alpha;
                slideOffsets[i] = slideOffset;

                totalW += item.DispW;
                if (i > 0) totalW += BonusBoxGap;
            }

            float boxY = labelBarY + labelBarH + 8;

            // ── docelowe (bazowe, bez slide) pozycje X — wyśrodkowana grupa ──
            float baseX = centerX - totalW / 2f;
            float accum = baseX;
            for (int i = 0; i < _bonusItems.Count; i++)
            {
                var item = _bonusItems[i];
                float targetX = accum;

                if (!item.PosInit)
                {
                    item.DispX = targetX;
                    item.PosInit = true;
                }
                else
                {
                    item.DispX += (targetX - item.DispX) * SizeSmoothFactor;
                }

                accum += widths[i] + BonusBoxGap;
            }

            for (int i = 0; i < _bonusItems.Count; i++)
            {
                float w = widths[i];
                float h = heights[i];
                double alpha = alphas[i];

                if (alpha > 0.01)
                {
                    float boxX = _bonusItems[i].DispX + (float)slideOffsets[i];

                    using (var path = RoundedRect(new RectangleF(boxX, boxY, w, h), 6))
                    using (var bgBrush = new SolidBrush(WithAlpha(_accentColor, alpha - alphacut)))
                        g.FillPath(bgBrush, path);

                    DrawOutlinedText(g, _bonusItems[i].Text, font, boxX + padH, boxY + padV,
                        WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);
                }
            }
        }


        private void DrawLapScoreBox(Graphics g)
        {
            if (_inMenu || _lapBoxBlend <= 0.01) return;

            double alpha = _lapBoxBlend;
            float slideX = (float)((1.0 - _lapBoxBlend) * -30);

            var titleFont = _activeFont15;
            var valueFont = _activeFont24;
            var contextFont = _activeFont11;

            string lapLabel = Localization.T("hud.lapscore");
            string lapValue = ((long)Math.Round(_displayedLapScore)).ToString("N0");
            string lastLapLabel = Localization.T("hud.lastlapscore");       // ← NOWE (dodaj klucz w Localization.cs)
            string lastLapValue = _lastLapScoreDisplay.ToString("N0");      // ← NOWE
            string bestLabel = Localization.T("hud.bestlapscore");
            string bestValue = _bestLapScore.ToString("N0");
            string contextLabel = _lapContextLabel;

            var lapLabelSize = g.MeasureString(lapLabel, titleFont);
            var lapValueSize = g.MeasureString(lapValue, valueFont);
            var lastLapLabelSize = g.MeasureString(lastLapLabel, titleFont);     // ← NOWE
            var lastLapValueSize = g.MeasureString(lastLapValue, valueFont);     // ← NOWE
            var bestLabelSize = g.MeasureString(bestLabel, titleFont);
            var bestValueSize = g.MeasureString(bestValue, valueFont);
            var contextSize = string.IsNullOrEmpty(contextLabel)
                ? SizeF.Empty : g.MeasureString(contextLabel, contextFont);

            float boxW = Math.Max(
                Math.Max(Math.Max(lapLabelSize.Width, lapValueSize.Width),
                         Math.Max(lastLapLabelSize.Width, lastLapValueSize.Width)),
                Math.Max(Math.Max(bestLabelSize.Width, bestValueSize.Width), contextSize.Width)) + 28;

            float rowGap = 1;
            float contextExtra = contextSize.Height > 0 ? contextSize.Height + 8 : 0;

            float boxH = lapLabelSize.Height + lapValueSize.Height
                       + lastLapLabelSize.Height + lastLapValueSize.Height     // ← NOWE
                       + bestLabelSize.Height + bestValueSize.Height
                       + contextExtra
                       + rowGap * 5 + 20;   // ← było rowGap * 3

            float boxX = 24 + slideX;
            float boxY = (Height - boxH) / 2f;

            using (var path = RoundedRect(new RectangleF(boxX, boxY, boxW, boxH), 10))
            using (var bgBrush = new SolidBrush(WithAlpha(Color.FromArgb(255, 18, 18, 22), alpha * 0.59)))
                g.FillPath(bgBrush, path);

            float textX = boxX + 14;
            float y = boxY + 10;

            DrawOutlinedText(g, lapLabel, titleFont, textX, y,
                WithAlpha(Color.White, alpha * 0.82), WithAlpha(Color.Black, alpha), outline: false);
            y += lapLabelSize.Height + rowGap;

            DrawOutlinedText(g, lapValue, valueFont, textX, y,
                WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);
            y += lapValueSize.Height + rowGap;

            // ← NOWY BLOK: ostatnie okrążenie
            DrawOutlinedText(g, lastLapLabel, titleFont, textX, y,
                WithAlpha(Color.White, alpha * 0.82), WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapLabelSize.Height + rowGap;

            DrawOutlinedText(g, lastLapValue, valueFont, textX, y,
                WithAlpha(Color.FromArgb(120, 200, 255), alpha), WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapValueSize.Height + rowGap;
            // ← KONIEC NOWEGO BLOKU

            DrawOutlinedText(g, bestLabel, titleFont, textX, y,
                WithAlpha(Color.White, alpha * 0.82), WithAlpha(Color.Black, alpha), outline: false);
            y += bestLabelSize.Height + rowGap;

            DrawOutlinedText(g, bestValue, valueFont, textX, y,
                WithAlpha(Color.FromArgb(255, 215, 0), alpha), WithAlpha(Color.Black, alpha), outline: false);
            y += bestValueSize.Height;

            if (!string.IsNullOrEmpty(contextLabel))
            {
                y += 8;
                DrawOutlinedText(g, contextLabel, contextFont, textX, y,
                    WithAlpha(Color.FromArgb(255, 170, 170, 180), alpha * 0.85), WithAlpha(Color.Black, alpha), outline: false);
            }
        }

        // ── strzałki kąta (jak GetDriftValueINDR/INDL z DriftEngine) ──
        private void DrawAngleArrows(Graphics g, float barX, float barY, float barW, float barH, double alpha)
        {
            int count = _driftAngle > 90 ? 6
                      : _driftAngle > 65 ? 5
                      : _driftAngle > 55 ? 4
                      : _driftAngle > 45 ? 3
                      : _driftAngle > 25 ? 2
                      : _driftAngle > 15 ? 1
                      : 0;
            if (count == 0) return;

            float chevW = 14, chevH = 16, spacing = 4;
            float midY = barY + barH / 2f;

            using var pen = new Pen(WithAlpha(_accentColor, alpha), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            if (_sideRight)
            {
                float startX = barX + barW + 18;
                for (int i = 0; i < count; i++)
                {
                    float cx = startX + i * (chevW + spacing);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx, midY - chevH / 2),
                        new PointF(cx + chevW / 2, midY),
                        new PointF(cx, midY + chevH / 2)
                    });
                }
            }
            else
            {
                float startX = barX - 18;
                for (int i = 0; i < count; i++)
                {
                    float cx = startX - i * (chevW + spacing);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx, midY - chevH / 2),
                        new PointF(cx - chevW / 2, midY),
                        new PointF(cx, midY + chevH / 2)
                    });
                }
            }
        }

        // ── Speedometer + Tachometer (Forza-style) ────────────────────────────
        //
        // Rysowane w lokalnym układzie współrzędnych (transform: translate + scale), żeby
        // SpeedoTachoScale skalował WSZYSTKO (geometrię, czcionki, grubości linii) jednym
        // spójnym mnożnikiem, zamiast ręcznie przeliczać każdy wymiar osobno.
        private const float SpeedoTachoDesignSize = 300f;

        /// <summary>
        /// Górna granica skali obrotomierza — domyślnie 10000 (dopóki nie znamy jeszcze
        /// CalibratedMAXRPM), a po kalibracji: MAXRPM + margines ~15% (zaokrąglony do pełnego
        /// tysiąca), tak jak w prawdziwym aucie redline zaczyna się przed samym końcem skali,
        /// a nie dokładnie na jej granicy.
        /// </summary>
        private float ComputeGaugeMaxRpm()
        {
            if (_calibratedMaxRpm <= 0) return 10000f;

            float withMargin = _calibratedMaxRpm * 1.15f;
            float rounded = (float)(Math.Ceiling(withMargin / 1000.0) * 1000.0);
            return Math.Max(rounded, _calibratedMaxRpm + 1000f);
        }

        private static string GearToDisplayText(int gear)
        {
            if (gear <= 0) return "R";
            if (gear == 1) return "N";
            return (gear - 1).ToString();
        }

        // Kolor igły obrotomierza zależny od stanu silnika — NIE dotyczy koloru tekstu biegu
        // na środku (ten zawsze zostaje SpeedoTachoTextColor, patrz DrawSpeedoTachoContent):
        //  • cięcie zapłonu (rev limiter) LUB RPM >= redline → kolor redline (ustawiany w UI)
        //  • RPM w strefie 1000–100 przed redline ("gotowy do zmiany biegu") → pastelowa zieleń
        //  • w pozostałych przypadkach → SpeedoTachoIndicatorColor (ustawiany w UI)
        private Color GetTachoStateColor()
        {
            if (_revCutActive || (_calibratedMaxRpm > 0 && _currentRpm >= _calibratedMaxRpm))
                return RedlineColor;

            if (_calibratedMaxRpm > 0)
            {
                float lower = _calibratedMaxRpm - 1000;
                float upper = _calibratedMaxRpm - 100;
                if (_currentRpm >= lower && _currentRpm < upper)
                    return Color.FromArgb(255, 150, 230, 170);   // pastelowa zieleń
            }

            return SpeedoTachoIndicatorColor;
        }

        private void DrawSpeedoTacho(Graphics g)
        {
            if (!SpeedoTachoEnabled) return;

            // Brak świeżych danych OutGauge (menu/garaż/poza autem) = nie ma RPM/biegu/
            // prędkości do pokazania — HUD po prostu nie istnieje, zamiast pokazywać
            // zamrożone/zerowe wartości.
            if (!HasFreshOutGaugeData) return;

            float scale = Math.Max(0.3f, SpeedoTachoScale);
            float scaledSize = SpeedoTachoDesignSize * scale;

            // domyślna lokalizacja: prawy dolny róg ekranu, z marginesem + przesunięcie z ustawień
            const float marginX = 24f, marginY = 24f;
            float anchorRight = Width - marginX + SpeedoTachoOffsetX;
            float anchorBottom = Height - marginY + SpeedoTachoOffsetY;

            float originX = anchorRight - scaledSize;
            float originY = anchorBottom - scaledSize;

            var savedState = g.Save();
            g.TranslateTransform(originX, originY);
            g.ScaleTransform(scale, scale);

            try
            {
                DrawSpeedoTachoContent(g);
            }
            finally
            {
                g.Restore(savedState);
            }
        }

        // Rysuje w lokalnym układzie "designu" (jednostki niezależne od SpeedoTachoScale —
        // przeskalowanie już zostało zaaplikowane przez transform w DrawSpeedoTacho).
        private void DrawSpeedoTachoContent(Graphics g)
        {
            const float cx = 140f, cy = 118f, radius = 102f;
            const float startAngle = 135f, sweepAngle = 270f;   // klasyczny łuk 270°, otwarty u dołu

            float gaugeMaxRpm = ComputeGaugeMaxRpm();
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = GetTachoStateColor();

            var dialRect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

            // ── tło tarczy — kolor konfigurowalny z UI ──
            using (var bgBrush = new SolidBrush(SpeedoTachoBackgroundColor))
                g.FillEllipse(bgBrush, dialRect);

            // ── łuk redline: od poziomu MAXRPM do górnej granicy skali ──
            if (_calibratedMaxRpm > 0 && _calibratedMaxRpm < gaugeMaxRpm)
            {
                float redlineStartFrac = _calibratedMaxRpm / gaugeMaxRpm;
                float redlineStartAngle = startAngle + redlineStartFrac * sweepAngle;
                float redlineSweep = sweepAngle - redlineStartFrac * sweepAngle;

                using var redlinePen = new Pen(WithAlpha(RedlineColor, 0.9), 6f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(redlinePen, dialRect, redlineStartAngle, redlineSweep);
            }

            // ── ticki (główne co 1000 RPM + numer, pomocnicze w połowie odcinka) — kolor konfigurowalny ──
            Font tickFont = _tachoTickFont;
            for (int i = 0; i <= majorTicks; i++)
            {
                float frac = (float)i / majorTicks;
                float angleDeg = startAngle + frac * sweepAngle;
                double angleRad = angleDeg * Math.PI / 180.0;

                bool inRedline = _calibratedMaxRpm > 0 && (i * 1000f) >= _calibratedMaxRpm;
                Color tickColor = inRedline ? RedlineColor : SpeedoTachoTickColor;

                float outerR = radius;
                float innerR = radius - 14;
                float x1 = cx + (float)Math.Cos(angleRad) * outerR;
                float y1 = cy + (float)Math.Sin(angleRad) * outerR;
                float x2 = cx + (float)Math.Cos(angleRad) * innerR;
                float y2 = cy + (float)Math.Sin(angleRad) * innerR;

                using (var tickPen = new Pen(tickColor, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(tickPen, x1, y1, x2, y2);

                string label = i.ToString();
                var labelSize = g.MeasureString(label, tickFont);
                float labelR = innerR - 15;
                float lx = cx + (float)Math.Cos(angleRad) * labelR - labelSize.Width / 2f;
                float ly = cy + (float)Math.Sin(angleRad) * labelR - labelSize.Height / 2f;
                using (var labelBrush = new SolidBrush(WithAlpha(SpeedoTachoTextColor, (double)SpeedoTachoTextColor.A / 255.0 * 0.85)))
                    g.DrawString(label, tickFont, labelBrush, lx, ly);

                if (i < majorTicks)
                {
                    float midFrac = (i + 0.5f) / majorTicks;
                    float midAngleDeg = startAngle + midFrac * sweepAngle;
                    double midAngleRad = midAngleDeg * Math.PI / 180.0;
                    float mInnerR = radius - 8;
                    float mx1 = cx + (float)Math.Cos(midAngleRad) * outerR;
                    float my1 = cy + (float)Math.Sin(midAngleRad) * outerR;
                    float mx2 = cx + (float)Math.Cos(midAngleRad) * mInnerR;
                    float my2 = cy + (float)Math.Sin(midAngleRad) * mInnerR;
                    using var minorPen = new Pen(WithAlpha(SpeedoTachoTickColor, 0.65), 1.4f);
                    g.DrawLine(minorPen, mx1, my1, mx2, my2);
                }
            }

            // ── igła obrotomierza ──
            float rpmFrac = Math.Clamp(_currentRpm / gaugeMaxRpm, 0f, 1f);
            float needleAngleDeg = startAngle + rpmFrac * sweepAngle;
            double needleAngleRad = needleAngleDeg * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            using (var needlePen = new Pen(stateColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(needlePen, cx, cy, nx, ny);

            // ── centralny krążek z aktualnym biegiem ──
            // Tło krążka (hub) zmienia się na kolor redline podczas cięcia zapłonu — zachowując
            // ten sam poziom przezroczystości co skonfigurowany SpeedoTachoBackgroundColor,
            // tylko z podmienionym RGB na RedlineColor.
            const float gearRadius = 32f;
            using (var ringPen = new Pen(WithAlpha(stateColor, 0.0), 2f))
                g.DrawEllipse(ringPen, cx - gearRadius, cy - gearRadius, gearRadius * 2, gearRadius * 2);

            Color hubColor = _revCutActive
                ? WithAlpha(RedlineColor, SpeedoTachoBackgroundColor.A / 255.0)
                : SpeedoTachoBackgroundColor;
            using (var hubBrush = new SolidBrush(hubColor))
                g.FillEllipse(hubBrush, cx - gearRadius + 3, cy - gearRadius + 3,
                    (gearRadius - 3) * 2, (gearRadius - 3) * 2);

            Font gearFont = _tachoGearFont;
            string gearText = GearToDisplayText(_currentGear);
            var gearSize = g.MeasureString(gearText, gearFont);
            DrawOutlinedText(g, gearText, gearFont, (cx - 2) - gearSize.Width / 2f, cy - gearSize.Height / 2f,
                SpeedoTachoTextColor, Color.FromArgb(200, 0, 0, 0), outline: false);

            // ── cyfrowa prędkość: WYŁĄCZNIE rzeczywista prędkość auta (z InSim MCI).
            //    (Wcześniej była tu też druga, estymowana "prędkość z wału" z RPM/OutSim —
            //    usunięta, bo obie próby jej wyliczenia (z RPM+przełożenia, potem z OutSim
            //    AngVel) okazały się niewiarygodne w praktyce. Zostaje jedna, pewna liczba.)
            //    Konwersja na MPH (jeśli włączona) dotyczy WYŁĄCZNIE tej liczby — obrotomierz
            //    (RPM) jest od tego niezależny.
            Font speedFont = _tachoSpeedFont;
            Font unitFont = _tachoUnitFont;

            double displaySpeed = SpeedoTachoUseMph ? _displayedSpeedKmh * KmhToMph : _displayedSpeedKmh;

            string speedText = ((int)Math.Round(displaySpeed)).ToString("D3");
            var speedSize = g.MeasureString(speedText, speedFont);
            float speedX = cx - speedSize.Width / 2f;
            float speedY = cy + gearRadius + 10;

            DrawOutlinedText(g, speedText, speedFont, speedX, speedY,
                SpeedoTachoTextColor, Color.FromArgb(200, 0, 0, 0), outline: false);

            string unitText = SpeedoTachoUseMph ? "MPH" : "KM/H";
            var unitSize = g.MeasureString(unitText, unitFont);
            DrawOutlinedText(g, unitText, unitFont,
                cx + speedSize.Width / 2.5f - unitSize.Width, speedY,
                WithAlpha(SpeedoTachoTextColor, (double)SpeedoTachoTextColor.A / 255.0 * 0.8), Color.FromArgb(180, 0, 0, 0), outline: false);
        }

        // ── helpers ──────────────────────────────────────────

        private double GetRevCutGlowIntensity()
        {
            if (_revCutBlend <= 0.01) return 0;

            // delikatny puls, żeby wyglądało jakby "żarzyło się", a nie stało w miejscu
            double pulse = 0.85 + 0.15 * Math.Sin((DateTime.Now - _revCutBlendStart).TotalSeconds * 6.0);
            return _revCutBlend * pulse;
        }

        private void DrawRevCutGlow(Graphics g, RectangleF bounds, double baseAlpha)
        {
            double glowStrength = GetRevCutGlowIntensity();
            if (glowStrength <= 0.01 || baseAlpha <= 0.01) return;

            double alpha = baseAlpha * glowStrength;

            const int layers = 16;
            const float spread = 50f;

            RectangleF inflated = RectangleF.Inflate(bounds, -30, -30);

            for (int i = layers; i >= 1; i--)
            {
                float grow = (spread / layers) * i;
                double layerAlpha = alpha * (0.7 - (double)i / (layers + 1)) * 0.35;

                using var path = RoundedRect(RectangleF.Inflate(inflated, grow, grow), 10 + grow / 2f);
                using var brush = new SolidBrush(WithAlpha(Color.FromArgb(255, 40, 40), layerAlpha));
                g.FillPath(brush, path);
            }
        }


        private void DrawOutlinedText(Graphics g, string text, Font font, float x, float y, Color fill, Color outlineColor, bool outline = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (outline)
            {
                using var outlineBrush = new SolidBrush(outlineColor);
                for (int ox = -2; ox <= 2; ox++)
                    for (int oy = -2; oy <= 2; oy++)
                        if (ox != 0 || oy != 0)
                            g.DrawString(text, font, outlineBrush, x + ox, y + oy);
            }
            else
            {
                using var shadowBrush = new SolidBrush(outlineColor);
                g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
            }

            using var fillBrush = new SolidBrush(fill);
            g.DrawString(text, font, fillBrush, x, y);
        }

        private static Color WithAlpha(Color c, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            return Color.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);
        }

        private static Color Darken(Color c, double amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedLeftRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddLine(rect.Right, rect.Y, rect.Right, rect.Bottom);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRightRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;

            // górna krawędź: od kwadratowego lewego rogu do startu łuku prawego-górnego
            path.AddLine(rect.X, rect.Y, rect.Right - d, rect.Y);

            // łuk prawy-górny (start u góry okręgu, 90° w prawo do boku)
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);

            // prawa krawędź w dół (opcjonalnie, dla pewności ciągłości)
            path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom - radius);

            // łuk prawy-dolny (start z boku, 90° w dół do dołu)
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);

            // dolna krawędź: od końca łuku do kwadratowego lewego rogu
            path.AddLine(rect.Right - d, rect.Bottom, rect.X, rect.Bottom);

            path.CloseFigure(); // zamyka lewą krawędzią z powrotem do (rect.X, rect.Y)
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _idleFont24?.Dispose();

                _idleFont26?.Dispose();
                _idleFont32?.Dispose();
                _activeFont11?.Dispose();
                _activeFont24?.Dispose();
                _activeFont22?.Dispose();
                _activeFont32?.Dispose();
                _activeFont42?.Dispose();
                _activeFont20?.Dispose();
                _activeFont17?.Dispose();
                _activeFont15?.Dispose();
                _tachoTickFont?.Dispose();
                _tachoGearFont?.Dispose();
                _tachoSpeedFont?.Dispose();
                _tachoUnitFont?.Dispose();

                _fontCollection?.Dispose();
                _renderTimer?.Dispose();
                _trackTimer?.Dispose();
                _buffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}