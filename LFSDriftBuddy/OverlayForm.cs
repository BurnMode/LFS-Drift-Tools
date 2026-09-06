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

        private Font _tachoTickFont;
        private Font _tachoGearFont;
        private Font _tachoSpeedFont;
        private Font _tachoUnitFont;

        private readonly SolidBrush _scratchBrush = new(Color.Black);
        private readonly Pen _scratchPen = new(Color.Black, 1f);

        private void LoadFonts()
        {
            string fontPath = Path.Combine(Application.StartupPath, "Data", "Fonts");
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

        private static readonly Color LabelTextColor = Color.White;

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

        public double ComboTimeoutSec { get; set; } = 3.0;

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

        private readonly DataFreshnessGate _outGaugeFreshness = new(TimeSpan.FromMilliseconds(1500));

        public void NotifyOutGaugeData() => _outGaugeFreshness.Ping();

                public void ResetOutGaugeData() => _outGaugeFreshness.Reset();

        private bool HasFreshOutGaugeData => _outGaugeFreshness.IsFresh;

        private bool _activeRequested = false;
        private bool _isActiveNow = false;
        private double _comboRemainingSec = 0;
        private double _activeBlend = 0;
        private bool _angleVisTargetVisible = false;
        private bool _angleVisEntering = false;
        private DateTime _angleVisBlendStart = DateTime.MinValue;
        private const int AngleVisBlendMs = 220;
        private double _activeBlendFrom = 0, _activeBlendTo = 0;
        private DateTime _activeBlendStart;
        private const int ActiveBlendMs = 320;
        private DateTime _lastTickTime = DateTime.Now;

        private double _displayedRun = 0;
        private long _targetRun = 0;
        private double _combo = 1;
        private string _driftLabelText = "";

        private bool _revCutActive = false;
        private double _revCutBlend = 0;
        private double _revCutBlendFrom = 0, _revCutBlendTo = 0;
        private DateTime _revCutBlendStart = DateTime.MinValue;
        private const int RevCutBlendMs = 220;

        private int _currentRpm = 0;

        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public Color RedlineColor { get; set; } = Color.Red;
        public float RedlineThickness { get; set; } = 4f;

        public bool SpeedoTachoUseMph { get; set; } = false;
        private const double KmhToMph = 0.621371;

        public bool SpeedoTachoShowRpmDigits { get; set; } = true;
        public bool SpeedoTachoShowUnit { get; set; } = true;
        public bool SpeedoTachoShowMinorTicks { get; set; } = true;
        public bool SpeedoTachoShowMajorTicks { get; set; } = true;

        public Color SpeedoTachoTextColor { get; set; } = Color.White;
        public Color SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);

        public Image SpeedoBackgroundImage { get; set; } = null;
        public float SpeedoBackgroundPanX { get; set; } = 0f;
        public float SpeedoBackgroundPanY { get; set; } = 0f;
        public float SpeedoBackgroundZoom { get; set; } = 1.0f;
        public float SpeedoBackgroundOpacity { get; set; } = 1.0f;

        private double _targetSpeedKmh = 0;
        private double _displayedSpeedKmh = 0;
        private int _currentGear = 0;
        private int _calibratedMaxRpm = 0;

        public void UpdateSpeedGauge(double speedKmh) => _targetSpeedKmh = Math.Max(0, speedKmh);
        public void UpdateGear(int gear) => _currentGear = gear;
        public void UpdateMaxRpm(int maxRpm) => _calibratedMaxRpm = Math.Max(0, maxRpm);

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
        private const double BonusSlideMs = 220;
        private const float BonusBoxGap = 8f;

        private double _driftAngle = 0;
        private bool _isDrifting = false;
        private bool _sideRight = true;

        private bool _hudSizeInit = false;
        private float _dispLabelBoxW = 0, _dispRunBoxW = 0, _dispBarH = 0;
        private const float SizeSmoothFactor = 0.45f;

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

                public void UpdateRpm(int rpm) => _currentRpm = rpm;
        private long _lastLapScoreDisplay = 0;
        public void UpdateLastLapScore(long score) => _lastLapScoreDisplay = score;
        public void UpdateScore(long total) => _targetScore = total;
        public void UpdateRun(long run) => _targetRun = run;
        public void UpdateLapScore(long lapScore) => _targetLapScore = lapScore;
        public void UpdateBestLapScore(long bestLapScore) => _bestLapScore = bestLapScore;

        public void UpdateLapContextLabel(string label) => _lapContextLabel = label ?? "";

        private bool _inMenu = false;

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
            if (_inMenu && visible) return;
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

                public void SetActive(bool active) => _activeRequested = active;

        public void ShowBonus(string bonusText)
        {
            if (string.IsNullOrWhiteSpace(bonusText)) return;

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
                offsetX = (1.0 - eased) * 240;
            }
            else if (elapsed > slideOutStart)
            {
                double bt = (elapsed - slideOutStart) / slideInSec;
                double eased = Math.Pow(bt, 2);
                alpha = 1.0 - eased;
                offsetX = -eased * 240;
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
                string startText = Localization.T("hud.lapstart");
                valueSize = g.MeasureString(startText, valueFont);
                valueX = centerX - valueSize.Width / 2f;
                DrawShimmerText(g, startText, valueFont, valueX, valueY, alpha, elapsed, Color.Yellow, Color.Gray);
            }
        }

        private void DrawShimmerText(Graphics g, string text, Font font, float x, float y, double alpha, double elapsedSec, Color color, Color colorBack)
        {
            if (string.IsNullOrEmpty(text)) return;

            float emPx = font.Size * g.DpiY / 72f;

            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, emPx, new PointF(x, y), StringFormat.GenericTypographic);

            var bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var shadowMatrix = new Matrix())
            {
                shadowMatrix.Translate(1.5f, 1.5f);
                using var shadowPath = (GraphicsPath)path.Clone();
                shadowPath.Transform(shadowMatrix);
                _scratchBrush.Color = WithAlpha(colorBack, alpha);
                g.FillPath(_scratchBrush, shadowPath);
            }

            _scratchBrush.Color = WithAlpha(color, alpha * 0.7f);
            g.FillPath(_scratchBrush, path);

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

        private void Tick()
        {
            var now = DateTime.Now;
            double dt = (now - _lastTickTime).TotalSeconds;
            if (dt > 0.25) dt = 0.25;
            _lastTickTime = now;

            _isActiveNow = _activeRequested;

            _displayedScore += (_targetScore - _displayedScore) * 0.18;
            if (Math.Abs(_targetScore - _displayedScore) < 1) _displayedScore = _targetScore;

            _displayedRun += (_targetRun - _displayedRun) * 0.25;
            if (Math.Abs(_targetRun - _displayedRun) < 1) _displayedRun = _targetRun;

            _displayedLapScore += (_targetLapScore - _displayedLapScore) * 0.2;
            if (Math.Abs(_targetLapScore - _displayedLapScore) < 1) _displayedLapScore = _targetLapScore;

            _displayedAngle += (_driftAngle - _displayedAngle) * 0.25;
            if (Math.Abs(_driftAngle - _displayedAngle) < 0.1) _displayedAngle = _driftAngle;

            _displayedSpeedKmh += (_targetSpeedKmh - _displayedSpeedKmh) * 0.3;
            if (Math.Abs(_targetSpeedKmh - _displayedSpeedKmh) < 0.05) _displayedSpeedKmh = _targetSpeedKmh;

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
            DrawRevCutTextGlow(g, text, font, x, 5);

            DrawOutlinedText(g, text, font, x, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);
        }

        private void DrawActiveHud(Graphics g, float centerX, out float barX, out float barY, out float barW, out float barH)
        {
            barX = barY = barW = barH = 0;
            double alphacut = 0.25;
            double alpha = _activeBlend;
            if (alpha <= 0.01) return;

            float slideX = _isActiveNow ? (float)((1.0 - _activeBlend) * 220) : (float)(-(1.0 - _activeBlend) * 220);

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

            DrawRevCutTextGlow(g, scoreText, scoreFont, startX, 5);

            DrawOutlinedText(g, scoreText, scoreFont, startX, 5, WithAlpha(_accentColor, alpha), WithAlpha(_accentColorBack, alpha), outline: false);

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

            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            using (var oldClip = g.Clip)
            {
                g.SetClip(labelPath, CombineMode.Replace);

                _scratchBrush.Color = WithAlpha(_accentColor, alpha - alphacut);
                g.FillRectangle(_scratchBrush, barX, barY, labelBoxW, barH);

                float consumed = labelBoxW * (1f - (float)comboFraction);
                if (consumed > 0.5f)
                {
                    _scratchBrush.Color = WithAlpha(Color.Black, (alpha * 0.85) - alphacut);
                    g.FillRectangle(_scratchBrush, barX + labelBoxW - consumed, barY, consumed, barH);
                }

                g.Clip = oldClip;
            }

            using (var runPath = RoundedRightRect(new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6))
            {
                _scratchBrush.Color = WithAlpha(Darken(_accentColor, 0.15), alpha - alphacut);
                g.FillPath(_scratchBrush, runPath);
            }

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
                angleOffsetX = (1 - eased) * 60;
            }
            else
            {
                double eased = Math.Pow(angleTNorm, 2);
                angleAlpha = 1 - eased;
                angleOffsetX = -eased * 60;
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
                const float LetterSpacing = -12f;
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
                    {
                        _scratchBrush.Color = WithAlpha(_accentColor, alpha - alphacut);
                        g.FillPath(_scratchBrush, path);
                    }

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
            {
                _scratchBrush.Color = WithAlpha(Color.FromArgb(255, 18, 18, 22), alpha * 0.59);
                g.FillPath(_scratchBrush, path);
            }

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

            _scratchPen.Color = WithAlpha(_accentColor, alpha);
            _scratchPen.Width = 3.5f;
            _scratchPen.StartCap = LineCap.Round;
            _scratchPen.EndCap = LineCap.Round;
            _scratchPen.LineJoin = LineJoin.Round;

            if (_sideRight)
            {
                float startX = barX + barW + 18;
                for (int i = 0; i < count; i++)
                {
                    float cx = startX + i * (chevW + spacing);
                    g.DrawLines(_scratchPen, new[]
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
                    g.DrawLines(_scratchPen, new[]
                    {
                        new PointF(cx, midY - chevH / 2),
                        new PointF(cx - chevW / 2, midY),
                        new PointF(cx, midY + chevH / 2)
                    });
                }
            }
        }

        private const float SpeedoTachoDesignSize = 300f;

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

        private Color GetTachoStateColor()
        {
            if (_revCutActive || (_calibratedMaxRpm > 0 && _currentRpm >= _calibratedMaxRpm))
                return RedlineColor;

            if (_calibratedMaxRpm > 0)
            {
                float lower = _calibratedMaxRpm - 1000;
                float upper = _calibratedMaxRpm - 100;
                if (_currentRpm >= lower && _currentRpm < upper)
                    return Color.FromArgb(255, 150, 230, 170);
            }

            return SpeedoTachoIndicatorColor;
        }

        private void DrawSpeedoTacho(Graphics g)
        {
            if (!SpeedoTachoEnabled) return;

            if (!HasFreshOutGaugeData) return;

            float scale = Math.Max(0.3f, SpeedoTachoScale);
            float scaledSize = SpeedoTachoDesignSize * scale;

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

        private void DrawSpeedoBackgroundImage(Graphics g, RectangleF dialRect)
        {
            var img = SpeedoBackgroundImage;
            if (img == null || SpeedoBackgroundOpacity <= 0.001f) return;

            float diameter = dialRect.Width;
            float coverScale = diameter / Math.Min(img.Width, img.Height) * Math.Max(0.1f, SpeedoBackgroundZoom);
            float drawW = img.Width * coverScale;
            float drawH = img.Height * coverScale;
            float drawX = dialRect.X + diameter / 2f - drawW / 2f + SpeedoBackgroundPanX * diameter;
            float drawY = dialRect.Y + diameter / 2f - drawH / 2f + SpeedoBackgroundPanY * diameter;

            using var path = new GraphicsPath();
            path.AddEllipse(dialRect);
            var oldClip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);

            using var attrs = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = Math.Clamp(SpeedoBackgroundOpacity, 0f, 1f) };
            attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(img, new Rectangle((int)drawX, (int)drawY, (int)drawW, (int)drawH),
                0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);

            g.Clip = oldClip;
        }

        private void DrawSpeedoTachoContent(Graphics g)
        {
            const float cx = 140f, cy = 118f, radius = 102f;
            const float startAngle = 135f, sweepAngle = 270f;

            float gaugeMaxRpm = ComputeGaugeMaxRpm();
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = GetTachoStateColor();

            var dialRect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

            _scratchBrush.Color = SpeedoTachoBackgroundColor;
            g.FillEllipse(_scratchBrush, dialRect);

            DrawSpeedoBackgroundImage(g, dialRect);

            if (_calibratedMaxRpm > 0 && _calibratedMaxRpm < gaugeMaxRpm)
            {
                float redlineStartFrac = _calibratedMaxRpm / gaugeMaxRpm;
                float redlineStartAngle = startAngle + redlineStartFrac * sweepAngle;
                float redlineSweep = sweepAngle - redlineStartFrac * sweepAngle;

                // Keep the outer edge pinned to the dial's true edge and let extra thickness eat
                // inward only, instead of the stroke growing outward past the dial too.
                float redlineWidth = Math.Max(1f, RedlineThickness);
                float redlineRadius = radius - redlineWidth / 2f;
                var redlineRect = new RectangleF(cx - redlineRadius, cy - redlineRadius, redlineRadius * 2, redlineRadius * 2);

                _scratchPen.Color = WithAlpha(RedlineColor, 0.9);
                _scratchPen.Width = redlineWidth;
                _scratchPen.StartCap = LineCap.Round;
                _scratchPen.EndCap = LineCap.Round;
                g.DrawArc(_scratchPen, redlineRect, redlineStartAngle, redlineSweep);
            }

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

                if (SpeedoTachoShowMajorTicks)
                {
                    float x1 = cx + (float)Math.Cos(angleRad) * outerR;
                    float y1 = cy + (float)Math.Sin(angleRad) * outerR;
                    float x2 = cx + (float)Math.Cos(angleRad) * innerR;
                    float y2 = cy + (float)Math.Sin(angleRad) * innerR;

                    _scratchPen.Color = Color.FromArgb(150, 0, 0, 0);
                    _scratchPen.Width = 2.8f;
                    _scratchPen.StartCap = LineCap.Round;
                    _scratchPen.EndCap = LineCap.Round;
                    g.DrawLine(_scratchPen, x1 + 1.5f, y1 + 1.5f, x2 + 1.5f, y2 + 1.5f);

                    _scratchPen.Color = tickColor;
                    g.DrawLine(_scratchPen, x1, y1, x2, y2);
                }

                if (SpeedoTachoShowRpmDigits)
                {
                    string label = i.ToString();
                    var labelSize = g.MeasureString(label, tickFont);
                    float labelR = innerR - 15;
                    float lx = cx + (float)Math.Cos(angleRad) * labelR - labelSize.Width / 2f;
                    float ly = cy + (float)Math.Sin(angleRad) * labelR - labelSize.Height / 2f;
                    DrawOutlinedText(g, label, tickFont, lx, ly,
                        WithAlpha(SpeedoTachoTextColor, (double)SpeedoTachoTextColor.A / 255.0 * 0.85),
                        Color.FromArgb(180, 0, 0, 0), outline: false);
                }

                if (i < majorTicks && SpeedoTachoShowMinorTicks)
                {
                    float midFrac = (i + 0.5f) / majorTicks;
                    float midAngleDeg = startAngle + midFrac * sweepAngle;
                    double midAngleRad = midAngleDeg * Math.PI / 180.0;
                    float mInnerR = radius - 8;
                    float mx1 = cx + (float)Math.Cos(midAngleRad) * outerR;
                    float my1 = cy + (float)Math.Sin(midAngleRad) * outerR;
                    float mx2 = cx + (float)Math.Cos(midAngleRad) * mInnerR;
                    float my2 = cy + (float)Math.Sin(midAngleRad) * mInnerR;

                    _scratchPen.Color = Color.FromArgb(120, 0, 0, 0);
                    _scratchPen.Width = 1.4f;
                    _scratchPen.StartCap = LineCap.Flat;
                    _scratchPen.EndCap = LineCap.Flat;
                    g.DrawLine(_scratchPen, mx1 + 1.2f, my1 + 1.2f, mx2 + 1.2f, my2 + 1.2f);

                    _scratchPen.Color = WithAlpha(SpeedoTachoTickColor, 0.65);
                    g.DrawLine(_scratchPen, mx1, my1, mx2, my2);
                }
            }
            DrawRevCutRingGlow(g, dialRect);
            float rpmFrac = Math.Clamp(_currentRpm / gaugeMaxRpm, 0f, 1f);
            float needleAngleDeg = startAngle + rpmFrac * sweepAngle;
            double needleAngleRad = needleAngleDeg * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            _scratchPen.Color = Color.FromArgb(190, 0, 0, 0);
            _scratchPen.Width = 4f;
            _scratchPen.StartCap = LineCap.Round;
            _scratchPen.EndCap = LineCap.Round;
            g.DrawLine(_scratchPen, cx + 1.5f, cy + 1.5f, nx + 1.5f, ny + 1.5f);

            _scratchPen.Color = stateColor;
            g.DrawLine(_scratchPen, cx, cy, nx, ny);

            const float gearRadius = 32f;
            _scratchPen.Color = WithAlpha(stateColor, 0.0);
            _scratchPen.Width = 2f;
            _scratchPen.StartCap = LineCap.Flat;
            _scratchPen.EndCap = LineCap.Flat;
            g.DrawEllipse(_scratchPen, cx - gearRadius, cy - gearRadius, gearRadius * 2, gearRadius * 2);

            Color hubColor = _revCutActive
                ? WithAlpha(RedlineColor, SpeedoTachoBackgroundColor.A / 255.0)
                : SpeedoTachoBackgroundColor;
            _scratchBrush.Color = hubColor;
            g.FillEllipse(_scratchBrush, cx - gearRadius + 3, cy - gearRadius + 3,
                (gearRadius - 3) * 2, (gearRadius - 3) * 2);

            Font gearFont = _tachoGearFont;
            string gearText = GearToDisplayText(_currentGear);
            var gearSize = g.MeasureString(gearText, gearFont);
            DrawOutlinedText(g, gearText, gearFont, (cx - 2) - gearSize.Width / 2f, cy - gearSize.Height / 2f,
                SpeedoTachoTextColor, Color.FromArgb(200, 0, 0, 0), outline: false);

            Font speedFont = _tachoSpeedFont;
            Font unitFont = _tachoUnitFont;

            double displaySpeed = SpeedoTachoUseMph ? _displayedSpeedKmh * KmhToMph : _displayedSpeedKmh;

            string speedText = ((int)Math.Round(displaySpeed)).ToString("D3");
            var speedSize = g.MeasureString(speedText, speedFont);
            float speedX = cx - speedSize.Width / 2f;
            float speedY = cy + gearRadius + 10;

            DrawOutlinedText(g, speedText, speedFont, speedX, speedY,
                SpeedoTachoTextColor, Color.FromArgb(200, 0, 0, 0), outline: false);

            if (SpeedoTachoShowUnit)
            {
                string unitText = SpeedoTachoUseMph ? "MPH" : Localization.T("speedometer.unit").ToUpperInvariant();
                var unitSize = g.MeasureString(unitText, unitFont);
                DrawOutlinedText(g, unitText, unitFont,
                    cx + speedSize.Width / 2.5f - unitSize.Width, speedY,
                    WithAlpha(SpeedoTachoTextColor, (double)SpeedoTachoTextColor.A / 255.0 * 0.8), Color.FromArgb(180, 0, 0, 0), outline: false);
            }

            
        }

        private double GetRevCutGlowIntensity()
        {
            if (_revCutBlend <= 0.01) return 0;

            double pulse = 0.85 + 0.15 * Math.Sin((DateTime.Now - _revCutBlendStart).TotalSeconds * 6.0);
            return _revCutBlend * pulse;
        }

        // Soft red glow ring for the rev-limiter cut effect, hugging the gauge's own edge and
        // fading outward — layered concentric strokes instead of one crisp pen line.
        private void DrawRevCutRingGlow(Graphics g, RectangleF dialRect, float baseInflate = -1f, float spread = 20f, int layers = 10)
        {
            double glowStrength = GetRevCutGlowIntensity();
            if (glowStrength <= 0.01) return;

            for (int i = layers; i >= 1; i--)
            {
                spread--;
                float grow = baseInflate + (spread / layers) * i;
                double layerAlpha = glowStrength * (1.0 - (double)i / (layers + 1)) * 0.50;

                _scratchPen.Color = WithAlpha(RedlineColor, layerAlpha);
                _scratchPen.Width = 3f;
                g.DrawEllipse(_scratchPen, RectangleF.Inflate(dialRect, grow, grow));
            }
        }

        // Soft red glow that traces the shape of the number itself (many faint offset copies of
        // the same string), used for the rev-limiter cut effect on the total score — drawn
        // underneath the crisp score text.
        private void DrawRevCutTextGlow(Graphics g, string text, Font font, float x, float y)
        {
            double glowStrength = GetRevCutGlowIntensity();
            if (glowStrength <= 0.01 || string.IsNullOrEmpty(text)) return;

            const int radius = 5;
            _scratchBrush.Color = WithAlpha(Color.FromArgb(255, 40, 40), glowStrength * 0.05);
            for (int ox = -radius; ox <= radius; ox++)
            {
                for (int oy = -radius; oy <= radius; oy++)
                {
                    if (ox == 0 && oy == 0) continue;
                    if (Math.Sqrt(ox * ox + oy * oy) > radius) continue;
                    g.DrawString(text, font, _scratchBrush, x + ox, y + oy);
                }
            }
        }

        private void DrawOutlinedText(Graphics g, string text, Font font, float x, float y, Color fill, Color outlineColor, bool outline = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (outline)
            {
                _scratchBrush.Color = outlineColor;
                for (int ox = -2; ox <= 2; ox++)
                    for (int oy = -2; oy <= 2; oy++)
                        if (ox != 0 || oy != 0)
                            g.DrawString(text, font, _scratchBrush, x + ox, y + oy);
            }
            else
            {
                _scratchBrush.Color = outlineColor;
                g.DrawString(text, font, _scratchBrush, x + 1.5f, y + 1.5f);
            }

            _scratchBrush.Color = fill;
            g.DrawString(text, font, _scratchBrush, x, y);
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

            path.AddLine(rect.X, rect.Y, rect.Right - d, rect.Y);

            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);

            path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom - radius);

            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);

            path.AddLine(rect.Right - d, rect.Bottom, rect.X, rect.Bottom);

            path.CloseFigure();
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
                _scratchBrush?.Dispose();
                _scratchPen?.Dispose();
                _renderTimer?.Dispose();
                _trackTimer?.Dispose();
                _buffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}