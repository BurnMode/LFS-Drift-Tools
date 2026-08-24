using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

        private Font _idleFont32;
        private Font _activeFont22;
        private Font _activeFont24;
        private Font _activeFont32;
        private Font _activeFont42;
        private Font _activeFont20;
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
            _fontCollection.AddFontFile(Path.Combine(fontPath, "active.otf"));
            FontFamily active = _fontCollection.Families[0];

            _idleFont32 = new Font(active, 32f, FontStyle.Bold);

            _activeFont11 = new Font(active, 11f, FontStyle.Regular);
            _activeFont24 = new Font(active, 24f, FontStyle.Bold);
            _activeFont22 = new Font(active, 22f, FontStyle.Bold);
            _activeFont32 = new Font(active, 32f, FontStyle.Bold);
            _activeFont42 = new Font(active, 42f, FontStyle.Bold);
            _activeFont20 = new Font(active, 20f, FontStyle.Bold);
            _activeFont15 = new Font(active, 15f, FontStyle.Bold);

            _tachoTickFont = new Font(active, 12f, FontStyle.Bold);
            _tachoGearFont = new Font(active, 26f, FontStyle.Bold);
            _tachoSpeedFont = new Font(active, 40f, FontStyle.Bold);
            _tachoUnitFont = new Font(active, 11f, FontStyle.Bold);
        }

        private IntPtr _targetHwnd = IntPtr.Zero;

        // Fixed label/bonus text color — doesn't change with InSimColor.
        private static readonly Color LabelTextColor = Color.White;

        // Dynamic accent color (InSimColor1..5 by drift angle).
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

        // Combo duration, from DriftEngine.COMBO_TIMEOUT_SEC.
        public double ComboTimeoutSec { get; set; } = 3.0;

        // ── total score ──────────────────────────────────────
        private double _displayedScore = 0;
        private long _targetScore = 0;
        private double _displayedAngle = 0;
        private double _displayedLapScore = 0;
        private long _targetLapScore = 0;
        private long _bestLapScore = 0;
        private string _lapContextLabel = "";
        private bool _lapBoxVisible = false;
        private double _lapBoxBlend = 0;
        private double _lapBoxBlendFrom = 0, _lapBoxBlendTo = 0;
        private DateTime _lapBoxBlendStart = DateTime.MinValue;
        private const int LapBoxBlendMs = 260;

        // OutGauge is a separate UDP stream from InSim/MCI — LFS only sends it while the
        // player is actually in a car on track, not in menus/garage/replay without a car.
        // Freshness tracked from NotifyOutGaugeData (see MainForm.OnRevData). No fresh data
        // blocks: (1) the speedo+tacho gauge (nothing to show) and (2) switching the score HUD
        // to "active" (drift/speeding come from InSim MCI, which can run without OutGauge).
        private readonly DataFreshnessGate _outGaugeFreshness = new(TimeSpan.FromMilliseconds(1500));

        public void NotifyOutGaugeData() => _outGaugeFreshness.Ping();

        /// <summary>Call on disconnect so freshness doesn't linger into the next AttachTo().</summary>
        public void ResetOutGaugeData() => _outGaugeFreshness.Reset();

        private bool HasFreshOutGaugeData => _outGaugeFreshness.IsFresh;

        // _isActiveNow combines SetActive's request with OutGauge freshness — recomputed
        // every frame in Tick().
        private bool _activeRequested = false;
        private bool _isActiveNow = false;
        private double _comboRemainingSec = 0;
        private double _activeBlend = 0;
        private bool _angleVisTargetVisible = false;
        private bool _angleVisEntering = false;
        private DateTime _angleVisBlendStart = DateTime.MinValue;
        private const int AngleVisBlendMs = 220; // same scale as BonusSlideMs
        private double _activeBlendFrom = 0, _activeBlendTo = 0;
        private DateTime _activeBlendStart;
        private const int ActiveBlendMs = 320;
        private DateTime _lastTickTime = DateTime.Now;

        private double _displayedRun = 0;
        private long _targetRun = 0;
        private double _combo = 1;
        private string _driftLabelText = "";

        // ── rev limiter cut — red glow under the HUD ──────────
        private bool _revCutActive = false;
        private double _revCutBlend = 0;
        private double _revCutBlendFrom = 0, _revCutBlendTo = 0;
        private DateTime _revCutBlendStart = DateTime.MinValue;
        private const int RevCutBlendMs = 220;

        private int _currentRpm = 0;

        // ── Speedometer + Tachometer (Forza-style, bottom-right) ──
        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public Color RedlineColor { get; set; } = Color.Red;

        // km/h (default) vs mph — affects only the digital speed readout, not the tachometer.
        public bool SpeedoTachoUseMph { get; set; } = false;
        private const double KmhToMph = 0.621371;

        // Colors set from the UI ("HUD Colors" menu, see MainForm.ShowHudColorsMenu).
        public Color SpeedoTachoTextColor { get; set; } = Color.White;
        public Color SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);

        // Raw target from telemetry (see UpdateSpeedGauge) — arrives in steps, so we don't draw
        // it directly. _displayedSpeedKmh smoothly catches up to it each frame (see Tick()).
        private double _targetSpeedKmh = 0;
        private double _displayedSpeedKmh = 0;
        private int _currentGear = 0;          // OutGauge: 0=reverse, 1=neutral, 2=1st...
        private int _calibratedMaxRpm = 0;     // 0 = unknown yet -> default 0-10000 range

        public void UpdateSpeedGauge(double speedKmh) => _targetSpeedKmh = Math.Max(0, speedKmh);
        public void UpdateGear(int gear) => _currentGear = gear;
        public void UpdateMaxRpm(int maxRpm) => _calibratedMaxRpm = Math.Max(0, maxRpm);

        // ── bonus popup (gear ratio, etc.) ────────────────────
        private class BonusItem
        {
            public string Text;
            public DateTime Start;
            public float DispW;
            public bool SizeInit;
            public float DispX;
            public bool PosInit;
        }
        private readonly List<BonusItem> _bonusItems = new();
        private const double BonusDurationSec = 2.0;
        private const double BonusSlideMs = 220;   // slide in/out duration
        private const float BonusBoxGap = 8f;      // gap between adjacent boxes

        // ── drift angle / arrows ───────────────────────────────
        private double _driftAngle = 0;
        private bool _isDrifting = false;
        private bool _sideRight = true;

        // ── smooth box-size animation (label/run box, bonus box) ──
        private bool _hudSizeInit = false;
        private float _dispLabelBoxW = 0, _dispRunBoxW = 0, _dispBarH = 0;
        private const float SizeSmoothFactor = 0.45f; // higher = snappier

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

        /// <summary>RPM from OutGauge — currently just stored, reserved for future animations.</summary>
        public void UpdateRpm(int rpm) => _currentRpm = rpm;
        private long _lastLapScoreDisplay = 0;
        public void UpdateLastLapScore(long score) => _lastLapScoreDisplay = score;
        public void UpdateScore(long total) => _targetScore = total;
        public void UpdateRun(long run) => _targetRun = run;
        public void UpdateLapScore(long lapScore) => _targetLapScore = lapScore;
        public void UpdateBestLapScore(long bestLapScore) => _bestLapScore = bestLapScore;

        public void UpdateLapContextLabel(string label) => _lapContextLabel = label ?? "";

        // Blocks the lap-result box while the player is in the game's main menu (IS_STA ->
        // ISS_FRONT_END). Set from MainForm on RaceStateChanged(false/true).
        private bool _inMenu = false;

        /// <summary>Sets "in menu" mode. Entering it hides the lap box immediately (no fade)
        /// and blocks SetLapBoxVisible(true) until menu mode is turned off — otherwise an
        /// event racing the menu transition (e.g. TrackChanged) could show the box again
        /// before drift state catches up.</summary>
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
            if (_inMenu && visible) return;   // nothing shown in-menu until SetMenuMode(false)
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

        /// <summary>Called every telemetry frame: is drift/fast-driving active right now.
        /// The effective state (_isActiveNow) also requires fresh OutGauge data — see
        /// HasFreshOutGaugeData and Tick().</summary>
        public void SetActive(bool active) => _activeRequested = active;

        public void ShowBonus(string bonusText)
        {
            if (string.IsNullOrWhiteSpace(bonusText)) return;

            // New popups stack to the right of ongoing ones, they don't replace them.
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
                offsetX = (1.0 - eased) * 240; // slide in from the right
            }
            else if (elapsed > slideOutStart)
            {
                double bt = (elapsed - slideOutStart) / slideInSec;
                double eased = Math.Pow(bt, 2);
                alpha = 1.0 - eased;
                offsetX = -eased * 240; // slide out to the left
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

            // center both lines on a shared axis
            float titleX = centerX - titleSize.Width / 2f;
            float titleY = boxY + titleSize.Height / 2;

            float valueX = centerX - valueSize.Width / 2f;
            float valueY = boxY + titleSize.Height + padV;

            if (_lapResultScore >= 1000)
            {
                DrawOutlinedText(g, title, titleFont, titleX, titleY,
                    WithAlpha(Color.White, alpha * 0.85), WithAlpha(Color.Black, alpha), outline: false);

                DrawShimmerText(g, valueText, valueFont, valueX, valueY, alpha, elapsed, Color.White, Color.Gray);
            }
            else
            {
                valueSize = g.MeasureString("START", valueFont);
                valueX = centerX - valueSize.Width / 2f;
                DrawShimmerText(g, "START", valueFont, valueX, valueY, alpha, elapsed, Color.Yellow, Color.Gray);
            }
        }

        // Text filled with the accent color plus a sliding "shine" masked to the letter
        // shapes — same idea as the Windows 7 progress bar.
        private void DrawShimmerText(Graphics g, string text, Font font, float x, float y, double alpha, double elapsedSec, Color color, Color colorBack)
        {
            if (string.IsNullOrEmpty(text)) return;

            // AddString wants the font size in Graphics units (pixels); Font.Size is in
            // points, so convert via DPI to match DrawString's placement.
            float emPx = font.Size * g.DpiY / 72f;

            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, emPx, new PointF(x, y), StringFormat.GenericTypographic);

            var bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // drop shadow for legibility on light/dark backgrounds
            using (var shadowMatrix = new Matrix())
            {
                shadowMatrix.Translate(1.5f, 1.5f);
                using var shadowPath = (GraphicsPath)path.Clone();
                shadowPath.Transform(shadowMatrix);
                using var shadowBrush = new SolidBrush(WithAlpha(colorBack, alpha));
                g.FillPath(shadowBrush, shadowPath);
            }

            using (var baseBrush = new SolidBrush(WithAlpha(color, alpha * 0.7f)))
                g.FillPath(baseBrush, path);

            // clip to the letter shapes — the shine sweeps only inside them
            using var oldClip = g.Clip;
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

        // ── tracks the LFS window ─────────────────────────────
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

        // ── animation loop ─────────────────────────────────────
        private void Tick()
        {
            var now = DateTime.Now;
            double dt = (now - _lastTickTime).TotalSeconds;
            if (dt > 0.25) dt = 0.25;
            _lastTickTime = now;

            // Without fresh OutGauge data the score HUD stays idle (total only), regardless
            // of what SetActive reports — see the _activeRequested/HasFreshOutGaugeData comment above.
            _isActiveNow = _activeRequested && HasFreshOutGaugeData;

            _displayedScore += (_targetScore - _displayedScore) * 0.18;
            if (Math.Abs(_targetScore - _displayedScore) < 1) _displayedScore = _targetScore;

            _displayedRun += (_targetRun - _displayedRun) * 0.25;
            if (Math.Abs(_targetRun - _displayedRun) < 1) _displayedRun = _targetRun;

            _displayedLapScore += (_targetLapScore - _displayedLapScore) * 0.2;
            if (Math.Abs(_targetLapScore - _displayedLapScore) < 1) _displayedLapScore = _targetLapScore;

            _displayedAngle += (_driftAngle - _displayedAngle) * 0.25;
            if (Math.Abs(_driftAngle - _displayedAngle) < 0.1) _displayedAngle = _driftAngle;

            // Speedo: smoothly catch up to the latest telemetry value. Factor chosen so a
            // step takes ~150-200ms at 60 FPS — enough to hide uneven OutGauge packet timing
            // without a noticeable lag behind actual driving.
            _displayedSpeedKmh += (_targetSpeedKmh - _displayedSpeedKmh) * 0.3;
            if (Math.Abs(_targetSpeedKmh - _displayedSpeedKmh) < 0.05) _displayedSpeedKmh = _targetSpeedKmh;

            // Combo countdown: full while active, counts down otherwise.
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

            double lapBoxT = Math.Min(1.0, (now - _lapBoxBlendStart).TotalMilliseconds / LapBoxBlendMs);
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
                DrawLapScoreBox(g);
                DrawLapResultPopup(g);
                DrawSpeedoTacho(g);
            }

            SetBitmap(_buffer);
        }

        // ── góra: sam total score, znika w lewo z fade gdy aktywny HUD wjeżdża ──
        private void DrawIdleScore(Graphics g, float centerX, out float idlebarY)
        {
            double alpha = 0.75 - _activeBlend;
            float slideX = _isActiveNow ? (float)(-_activeBlend * 160) : (float)(_activeBlend * 160);

            string text = ((long)Math.Round(_displayedScore)).ToString("N0");
            var font = _idleFont32;
            var size = g.MeasureString(text, font);
            idlebarY = size.Height;
            if (alpha <= 0.01) return;
            float x = centerX - size.Width / 2f + slideX;
            DrawRevCutGlow(g, new RectangleF(x, 5, size.Width, size.Height), alpha * 0.55);

            DrawOutlinedText(g, text, font, x, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);
        }

        // ── active bar: total+combo (top), label|runscore box (bottom) ──
        private void DrawActiveHud(Graphics g, float centerX, out float barX, out float barY, out float barW, out float barH)
        {
            barX = barY = barW = barH = 0;
            double alphacut = 0.25;
            double alpha = _activeBlend;
            if (alpha <= 0.01) return;

            float slideX = _isActiveNow ? (float)((1.0 - _activeBlend) * 220) : (float)(-(1.0 - _activeBlend) * 220);

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

            DrawOutlinedText(g, scoreText, scoreFont, startX, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);

            DrawRevCutGlow(g, new RectangleF(startX, 5, scoreSize.Width, scoreSize.Height), alpha * 0.55);

            // box below: [ label (progress countdown) | run score (fixed accent) ]
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

            double comboFraction = ComboTimeoutSec > 0 ? _comboRemainingSec / ComboTimeoutSec : 1.0;
            comboFraction = Math.Max(0, Math.Min(1, comboFraction));

            // left box: label, background = progress bar (accent -> black from the right)
            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            using (var oldClip = g.Clip)
            {
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

            // right box: run score, fixed accent (no decay)
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
                angleOffsetX = (1 - eased) * 60; // slide in from the right
            }
            else
            {
                double eased = Math.Pow(angleTNorm, 2);
                angleAlpha = 1 - eased;
                angleOffsetX = -eased * 60; // slide out to the left
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

        private void DrawAnimatedCombo(Graphics g, Font font, string text, float x, float y, Color fill, Color shadow)
        {
            float posX = x;

            for (int i = 0; i < _comboChars.Count; i++)
            {
                var c = _comboChars[i];

                string current = c.Current.ToString();
                const float LetterSpacing = -12f; // negative = tighter, positive = wider
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

        // ── separate box below the label: bonuses (gear ratio, etc.), stacked side by side ──
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

            // target (base, no slide) X positions — centered group
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
            string lastLapLabel = Localization.T("hud.lastlapscore");
            string lastLapValue = _lastLapScoreDisplay.ToString("N0");
            string bestLabel = Localization.T("hud.bestlapscore");
            string bestValue = _bestLapScore.ToString("N0");
            string contextLabel = _lapContextLabel;

            var lapLabelSize = g.MeasureString(lapLabel, titleFont);
            var lapValueSize = g.MeasureString(lapValue, valueFont);
            var lastLapLabelSize = g.MeasureString(lastLapLabel, titleFont);
            var lastLapValueSize = g.MeasureString(lastLapValue, valueFont);
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
                       + lastLapLabelSize.Height + lastLapValueSize.Height
                       + bestLabelSize.Height + bestValueSize.Height
                       + contextExtra
                       + rowGap * 5 + 20;

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

            DrawOutlinedText(g, lastLapLabel, titleFont, textX, y,
                WithAlpha(Color.White, alpha * 0.82), WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapLabelSize.Height + rowGap;

            DrawOutlinedText(g, lastLapValue, valueFont, textX, y,
                WithAlpha(Color.FromArgb(120, 200, 255), alpha), WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapValueSize.Height + rowGap;

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

        // ── angle chevrons (like GetDriftValueINDR/INDL in DriftEngine) ──
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

        // ── Speedometer + Tachometer (Forza-style) ─────────────
        // Drawn in a local coordinate space (translate + scale transform) so SpeedoTachoScale
        // scales EVERYTHING (geometry, fonts, line widths) with one consistent factor instead
        // of rescaling each dimension by hand.
        private const float SpeedoTachoDesignSize = 300f;

        /// <summary>Tachometer scale upper bound — 10000 by default (before CalibratedMAXRPM is
        /// known), then MAXRPM + ~15% margin (rounded to the next 1000) once calibrated, so the
        /// redline starts before the very end of the scale, like a real car.</summary>
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

        // Needle color depends on engine state — doesn't affect the gear text color (always
        // SpeedoTachoTextColor, see DrawSpeedoTachoContent):
        //  • ignition cut OR RPM >= redline -> redline color
        //  • 1000-100 RPM before redline ("ready to shift") -> pastel green
        //  • otherwise -> SpeedoTachoIndicatorColor
        private Color GetTachoStateColor()
        {
            if (_revCutActive || (_calibratedMaxRpm > 0 && _currentRpm >= _calibratedMaxRpm))
                return RedlineColor;

            if (_calibratedMaxRpm > 0)
            {
                float lower = _calibratedMaxRpm - 1000;
                float upper = _calibratedMaxRpm - 100;
                if (_currentRpm >= lower && _currentRpm < upper)
                    return Color.FromArgb(255, 150, 230, 170);   // pastel green
            }

            return SpeedoTachoIndicatorColor;
        }

        private void DrawSpeedoTacho(Graphics g)
        {
            if (!SpeedoTachoEnabled) return;

            // No fresh OutGauge data (menu/garage/no car) = nothing to show — the HUD just
            // doesn't exist, rather than showing frozen/zero values.
            if (!HasFreshOutGaugeData) return;

            float scale = Math.Max(0.3f, SpeedoTachoScale);
            float scaledSize = SpeedoTachoDesignSize * scale;

            // default position: bottom-right, with margin + user offset
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

        // Drawn in local "design" units — scaling is already applied by the transform in
        // DrawSpeedoTacho.
        private void DrawSpeedoTachoContent(Graphics g)
        {
            const float cx = 140f, cy = 118f, radius = 102f;
            const float startAngle = 135f, sweepAngle = 270f;   // classic 270° arc, open at the bottom

            float gaugeMaxRpm = ComputeGaugeMaxRpm();
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = GetTachoStateColor();

            var dialRect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

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

            // Ticks: major every 1000 RPM with a number, minor at the midpoint.
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

            // tachometer needle
            float rpmFrac = Math.Clamp(_currentRpm / gaugeMaxRpm, 0f, 1f);
            float needleAngleDeg = startAngle + rpmFrac * sweepAngle;
            double needleAngleRad = needleAngleDeg * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            using (var needlePen = new Pen(stateColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(needlePen, cx, cy, nx, ny);

            // Central hub with the current gear — its background swaps to RedlineColor during
            // ignition cut, keeping the same alpha as SpeedoTachoBackgroundColor.
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

            // Digital speed — real vehicle speed from InSim MCI only. MPH conversion (if
            // enabled) affects only this number, the tachometer (RPM) is unaffected.
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

            // subtle pulse so it looks like it's glowing, not static
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

            // top edge: from the square left corner to the start of the top-right arc
            path.AddLine(rect.X, rect.Y, rect.Right - d, rect.Y);

            // top-right arc
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);

            path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom - radius);

            // bottom-right arc
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);

            // bottom edge: from the end of the arc to the square left corner
            path.AddLine(rect.Right - d, rect.Bottom, rect.X, rect.Bottom);

            path.CloseFigure(); // closes the left edge back to (rect.X, rect.Y)
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _idleFont32?.Dispose();
                _activeFont11?.Dispose();
                _activeFont24?.Dispose();
                _activeFont22?.Dispose();
                _activeFont32?.Dispose();
                _activeFont42?.Dispose();
                _activeFont20?.Dispose();
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