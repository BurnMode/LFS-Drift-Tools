using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LFSDriftTools
{
    public class OverlayForm : LayeredForm
    {
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly System.Windows.Forms.Timer _trackTimer;

        private Font _idleFont32 = null!;
        private Font _activeFont22 = null!;
        private Font _activeFont24 = null!;
        private Font _activeFont32 = null!;
        private Font _activeFont42 = null!;
        private Font _activeFont20 = null!;
        private Font _activeFont15 = null!;
        private Font _activeFont11 = null!;

        private readonly SolidBrush _scratchBrush = new(Color.Black);
        private readonly Pen _scratchPen = new(Color.Black, 1f);

        private void LoadFonts()
        {
            FontFamily active = FontLibrary.ActiveOrFallback;

            _idleFont32 = new Font(active, 32f, FontStyle.Bold);

            _activeFont11 = new Font(active, 11f, FontStyle.Regular);
            _activeFont24 = new Font(active, 24f, FontStyle.Bold);
            _activeFont22 = new Font(active, 22f, FontStyle.Bold);
            _activeFont32 = new Font(active, 32f, FontStyle.Bold);
            _activeFont42 = new Font(active, 42f, FontStyle.Bold);
            _activeFont20 = new Font(active, 20f, FontStyle.Bold);
            _activeFont15 = new Font(active, 15f, FontStyle.Bold);
        }

        private IntPtr _targetHwnd = IntPtr.Zero;

        private static readonly Color LabelTextColor = Color.White;
        private static readonly StringFormat AchievementWrapFormat = new();

        private Color _accentColor = Color.FromArgb(255, 235, 30);
        private Color _accentColorBack = Color.FromArgb(225, 205, 0);

        private void SetAccentColorInternal(Color c)
        {
            _accentColor = c;
            _accentColorBack = Color.FromArgb(
                c.A,
                (int)(c.R * 0.35),
                (int)(c.G * 0.35),
                (int)(c.B * 0.35)
            );
        }

        public void UpdateAccentColor(Color c)
        {
            if (DateTime.Now < _notificationUntil) return;
            SetAccentColorInternal(c);
        }

        private string? _notificationText = null;
        private DateTime _notificationUntil = DateTime.MinValue;

        public void ShowConnectionNotification(string scoreText, string bonusText, Color accentColor, double durationSec = 5.0)
        {
            _notificationText = scoreText;
            _notificationUntil = DateTime.Now.AddSeconds(durationSec);
            SetAccentColorInternal(accentColor);
            ShowBonus(bonusText, durationSec, boostOpacity: true);
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

        public void ResetOutGaugeData() { _outGaugeFreshness.Reset(); MarkInput(); }

        private bool HasFreshOutGaugeData => _outGaugeFreshness.IsFresh;

        private readonly DataFreshnessGate _carDataFreshness = new(TimeSpan.FromMilliseconds(1500));

        public void NotifyCarData() => _carDataFreshness.Ping();

        public void ResetCarData() { _carDataFreshness.Reset(); MarkInput(); }

        private bool HasFreshCarData => _carDataFreshness.IsFresh;

        private bool _activeRequested = false;
        private bool _isActiveNow = false;
        private double _comboRemainingSec = 0;
        private double _activeBlend = 0;
        private double _scoreVisibleBlend = 1.0;
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

        public bool ScoreOverlayEnabled { get; set; } = true;

        public bool SpeedoTachoEnabled { get; set; } = true;
        public bool SpeedoReplacedByInSimHud { get; set; }
        public const float SpeedoBaseOffsetX = 25f;
        public const float SpeedoBaseOffsetY = -60f;
        public const float SpeedoBaseScale = 1.25f;

        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public Color RedlineColor { get; set; } = Color.Red;
        public float RedlineThickness { get; set; } = 4f;

        public bool SpeedoTachoUseMph { get; set; } = false;
        private const double KmhToMph = 0.621371;

        public int SpeedoTachoType { get; set; } = SpeedoPresetData.TachoType1;
        public bool SpeedoTachoShowRpmDigits { get; set; } = true;
        public bool SpeedoTachoShowUnit { get; set; } = true;
        public bool SpeedoTachoShowMinorTicks { get; set; } = true;
        public bool SpeedoTachoShowMajorTicks { get; set; } = true;
        public bool SpeedoTachoPlainDigits { get; set; } = false;
        public bool SpeedoTachoInstantSpeed { get; set; } = false;
        public bool SpeedoTachoAirSpeed { get; set; } = false;
        public bool SpeedoTachoShowTelltales { get; set; } = true;
        public bool SpeedoTachoCenterUnit { get; set; } = true;

        public Color SpeedoTachoTextColor { get; set; } = Color.White;
        public Color SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color SpeedoTachoMidColor { get; set; } = Color.FromArgb(255, 255, 200, 0);
        public Color SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);
        public Color SpeedoTachoFuelColor { get; set; } = Color.FromArgb(128, 0, 187, 250);
        public Color SpeedoTachoTurboColor { get; set; } = Color.FromArgb(128, 255, 35, 34);

        public Image? SpeedoBackgroundImage { get; set; } = null;
        public float SpeedoBackgroundPanX { get; set; } = 0f;
        public float SpeedoBackgroundPanY { get; set; } = 0f;
        public float SpeedoBackgroundZoom { get; set; } = 1.0f;
        public float SpeedoBackgroundOpacity { get; set; } = 1.0f;

        private double _targetSpeedKmhTrue = 0;
        private double _targetSpeedKmhDash = 0;
        private double _displayedSpeedKmh = 0;
        private int _currentGear = 0;
        private int _calibratedMaxRpm = 0;

        public void UpdateSpeedGauge(double speedKmh) { double v = Math.Max(0, speedKmh); if (v != _targetSpeedKmhTrue) { _targetSpeedKmhTrue = v; MarkInput(); } }
        public void UpdateSpeedGaugeDash(double speedKmh) { double v = Math.Max(0, speedKmh); if (v != _targetSpeedKmhDash) { _targetSpeedKmhDash = v; MarkInput(); } }
        public void UpdateGear(int gear) { if (gear != _currentGear) { _currentGear = gear; MarkInput(); } }

        private float _dashFuel = -1f;
        private uint _dashShowLights;
        private float _turboFraction = -1f;

        public void UpdateDashInfo(float fuel, uint showLights)
        {
            float f = float.IsFinite(fuel) ? fuel : -1f;
            if (f == _dashFuel && showLights == _dashShowLights) return;
            _dashFuel = f;
            _dashShowLights = showLights;
            MarkInput();
        }

        public void UpdateTurbo(float boostBar, float maxBar, bool hasTurbo)
        {
            float fraction = hasTurbo && maxBar > 0f && float.IsFinite(boostBar)
                ? Math.Clamp(boostBar / maxBar, 0f, 1f)
                : -1f;
            if (fraction == _turboFraction) return;
            _turboFraction = fraction;
            MarkInput();
        }
        public void UpdateMaxRpm(int maxRpm) { int v = Math.Max(0, maxRpm); if (v != _calibratedMaxRpm) { _calibratedMaxRpm = v; MarkInput(); } }

        public bool ControllerAssistBarVisible { get; set; } = false;
        private bool _controllerAssistActive = false;
        private double _controllerAssistRawPercent = 0;
        private double _controllerAssistOutputPercent = 0;

        public void UpdateControllerAssist(bool active, double rawPercent, double outputPercent)
        {
            if (active == _controllerAssistActive && rawPercent == _controllerAssistRawPercent && outputPercent == _controllerAssistOutputPercent)
                return;
            _controllerAssistActive = active;
            _controllerAssistRawPercent = rawPercent;
            _controllerAssistOutputPercent = outputPercent;
            MarkInput();
        }

        private string? _caNotificationText = null;
        private DateTime _caNotificationUntil = DateTime.MinValue;

        public void ShowControllerAssistNotification(string text, double durationSec = 3.0)
        {
            _caNotificationText = text;
            _caNotificationUntil = DateTime.Now.AddSeconds(durationSec);
            MarkInput();
        }

        private class BonusItem
        {
            public string Text = "";
            public DateTime Start;
            public double DurationSec;
            public bool BoostOpacity;
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
            MarkInput();
        }

        public OverlayForm()
        {
            Width = 900;
            Height = 300;
            Opacity = 1.0;

            LoadFonts();

            _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _renderTimer.Tick += (s, e) => { long p = PerfProbe.Begin(); Tick(); PerfProbe.End(PerfSection.OverlayTick, p); };

            _trackTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _trackTimer.Tick += (s, e) => { long p = PerfProbe.Begin(); TrackTarget(); PerfProbe.End(PerfSection.OverlayTrack, p); };
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
            MarkInput();
        }

        public void UpdateRpm(int rpm) { if (rpm != _currentRpm) { _currentRpm = rpm; MarkInput(); } }
        private long _lastLapScoreDisplay = 0;
        public void UpdateLastLapScore(long score) { if (score != _lastLapScoreDisplay) { _lastLapScoreDisplay = score; MarkInput(); } }
        public void UpdateScore(long total) { if (total != _targetScore) { _targetScore = total; MarkInput(); } }
        public void UpdateRun(long run) { if (run != _targetRun) { _targetRun = run; MarkInput(); } }
        public void UpdateLapScore(long lapScore) { if (lapScore != _targetLapScore) { _targetLapScore = lapScore; MarkInput(); } }
        public void UpdateBestLapScore(long bestLapScore) { if (bestLapScore != _bestLapScore) { _bestLapScore = bestLapScore; MarkInput(); } }

        public void UpdateLapContextLabel(string label) { string v = label ?? ""; if (v != _lapContextLabel) { _lapContextLabel = v; MarkInput(); } }
        public bool LapBoxShown => _lapBoxVisible && HasFreshCarData;
        public long LastLapScoreShown => _lastLapScoreDisplay;
        public long BestLapScoreShown => _bestLapScore;
        public string LapContextLabel => _lapContextLabel;

        private bool _inMenu = true;

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
            MarkInput();
        }

        public void UpdateCombo(double combo)
        {
            _combo = combo;

            string newText = _comboText.Get(combo);

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
        public void UpdateLabel(string label) { string v = label ?? ""; if (v != _driftLabelText) { _driftLabelText = v; MarkInput(); } }

        public void SetActive(bool active) { if (active != _activeRequested) { _activeRequested = active; MarkInput(); } }

        public event Action<int>? BonusShown;

        public void ShowBonus(string bonusText, double durationSec = BonusDurationSec, bool boostOpacity = false)
        {
            if (string.IsNullOrWhiteSpace(bonusText)) return;

            var now = DateTime.Now;
            _bonusItems.Add(new BonusItem
            {
                Text = bonusText,
                Start = now,
                DurationSec = durationSec,
                BoostOpacity = boostOpacity
            });

            int active = 0;
            foreach (var item in _bonusItems)
                if ((now - item.Start).TotalSeconds < item.DurationSec) active++;
            BonusShown?.Invoke(active);
        }

        public void SetDriftAngle(double angle, bool drifting, bool sideRight)
        {
            _driftAngle = angle;
            _isDrifting = drifting;
            _sideRight = sideRight;
            MarkInput();
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
            string valueText = _lapResultText.Get(_lapResultScore);

            var titleSize = MeasureText(g, title, titleFont);
            var valueSize = MeasureText(g, valueText, valueFont);

            float padV = 0;
            float centerX = Width / 2f + (float)offsetX;
            float boxY = 350;

            float titleX = centerX - titleSize.Width / 2f;
            float titleY = boxY + titleSize.Height / 2;

            float valueX = centerX - valueSize.Width / 2f;
            float valueY = boxY + titleSize.Height + padV;

            EnterRegion(g, new RectangleF(0, boxY - 10, Width, titleSize.Height * 1.5f + valueSize.Height + 50));
            if (_lapResultScore >= 1000)
            {
                DrawOutlinedText(g, title, titleFont, titleX, titleY,
                    DrawingHelpers.WithAlpha(Color.White, alpha * 0.85), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);

                DrawShimmerText(g, valueText, valueFont, valueX, valueY, alpha, elapsed, Color.White, Color.Gray);
            }
            else
            {
                string startText = Localization.T("hud.lapstart");
                valueSize = MeasureText(g, startText, valueFont);
                valueX = centerX - valueSize.Width / 2f;
                DrawShimmerText(g, startText, valueFont, valueX, valueY, alpha, elapsed, Color.Yellow, Color.Gray);
            }
            g.ResetClip();
        }

        private void DrawShimmerText(Graphics g, string text, Font font, float x, float y, double alpha, double elapsedSec, Color color, Color colorBack)
        {
            if (string.IsNullOrEmpty(text)) return;

            float emPx = font.Size * g.DpiY / 72f;

            if (_shimmerPath == null || _shimmerText != text || !ReferenceEquals(_shimmerFont, font) || _shimmerX != x || _shimmerY != y)
            {
                _shimmerPath?.Dispose();
                _shimmerShadowPath?.Dispose();

                _shimmerPath = new GraphicsPath();
                _shimmerPath.AddString(text, font.FontFamily, (int)font.Style, emPx, new PointF(x, y), StringFormat.GenericTypographic);
                _shimmerBounds = _shimmerPath.GetBounds();

                _shimmerShadowPath = (GraphicsPath)_shimmerPath.Clone();
                using (var shadowMatrix = new Matrix())
                {
                    shadowMatrix.Translate(1.5f, 1.5f);
                    _shimmerShadowPath.Transform(shadowMatrix);
                }

                _shimmerText = text;
                _shimmerFont = font;
                _shimmerX = x;
                _shimmerY = y;
            }

            var path = _shimmerPath;
            var bounds = _shimmerBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            _scratchBrush.Color = DrawingHelpers.WithAlpha(colorBack, alpha);
            g.FillPath(_scratchBrush, _shimmerShadowPath!);

            _scratchBrush.Color = DrawingHelpers.WithAlpha(color, alpha * 0.7f);
            g.FillPath(_scratchBrush, path);

            var shimmerClipState = g.Save();
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
                DrawingHelpers.WithAlpha(Color.White, 0),
                DrawingHelpers.WithAlpha(Color.White, alpha * 0.7),
                DrawingHelpers.WithAlpha(Color.White, 0)
            },
                    Positions = new float[] { 0f, 0.5f, 1f }
                };
                sweepBrush.InterpolationColors = blend;
                g.FillRectangle(sweepBrush, sweepCenter - sweepWidth, bounds.Y - 4, sweepWidth * 2, bounds.Height + 8);
            }

            g.Restore(shimmerClipState);
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

            bool wantsMenuMode = !HasFreshCarData && now >= _notificationUntil;
            if (wantsMenuMode != _inMenu)
                SetMenuMode(wantsMenuMode);

            double scoreVisibleTarget = (_inMenu || !ScoreOverlayEnabled) ? 0.0 : 1.0;
            double scoreVisibleRate = scoreVisibleTarget > _scoreVisibleBlend ? 0.45 : 0.3;
            _scoreVisibleBlend += (scoreVisibleTarget - _scoreVisibleBlend) * scoreVisibleRate;
            if (Math.Abs(scoreVisibleTarget - _scoreVisibleBlend) < 0.01) _scoreVisibleBlend = scoreVisibleTarget;

            _displayedScore += (_targetScore - _displayedScore) * 0.18;
            if (Math.Abs(_targetScore - _displayedScore) < 1) _displayedScore = _targetScore;

            _displayedRun += (_targetRun - _displayedRun) * 0.25;
            if (Math.Abs(_targetRun - _displayedRun) < 1) _displayedRun = _targetRun;

            _displayedLapScore += (_targetLapScore - _displayedLapScore) * 0.2;
            if (Math.Abs(_targetLapScore - _displayedLapScore) < 1) _displayedLapScore = _targetLapScore;

            _displayedAngle += (_driftAngle - _displayedAngle) * 0.25;
            if (Math.Abs(_driftAngle - _displayedAngle) < 0.1) _displayedAngle = _driftAngle;

            double targetSpeedKmh = SpeedoTachoAirSpeed ? _targetSpeedKmhTrue : _targetSpeedKmhDash;
            if (SpeedoTachoInstantSpeed)
            {
                _displayedSpeedKmh = targetSpeedKmh;
            }
            else
            {
                _displayedSpeedKmh += (targetSpeedKmh - _displayedSpeedKmh) * 0.3;
                if (Math.Abs(targetSpeedKmh - _displayedSpeedKmh) < 0.05) _displayedSpeedKmh = targetSpeedKmh;
            }

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

            if (!Visible)
            {
                SetRenderInterval(HiddenRenderIntervalMs);
                return;
            }

            long nowMs = Environment.TickCount64;
            int stateSignature = StateSignature(now);
            if (_unchangedFrames >= IdleFrameThreshold && _inputVersion == _renderedInputVersion
                && stateSignature == _renderedStateSignature && _bonusItems.Count == 0
                && !_lapResultVisible && _achievementCurrent == null && _achievementQueue.Count == 0
                && nowMs - _lastRenderTickMs < IdleSafetyRenderMs)
            {
                PerfProbe.Count(PerfCounter.OverlayRenderSkipped);
                return;
            }
            _renderedInputVersion = _inputVersion;
            _renderedStateSignature = stateSignature;
            _lastRenderTickMs = nowMs;
            bool changed = Render();
            UpdateRenderCadence(changed);
        }

        private const int ActiveRenderIntervalMs = 16;
        private const int IdleRenderIntervalMs = 50;
        private const int HiddenRenderIntervalMs = 100;
        private const int IdleFrameThreshold = 30;
        private const int IdleSafetyRenderMs = 250;
        private int _inputVersion;
        private int _renderedInputVersion = -1;
        private long _lastRenderTickMs;

        private int _renderedStateSignature;

        private void MarkInput() => _inputVersion++;

        private int StateSignature(DateTime now)
        {
            var h = new HashCode();
            h.Add(_targetHwnd);
            h.Add(Width);
            h.Add(Height);
            h.Add(_inMenu);
            h.Add(_activeBlendTo);
            h.Add(_angleVisTargetVisible);
            h.Add(_lapBoxBlendTo);
            h.Add(_revCutBlendTo);
            h.Add(_scoreVisibleBlend);
            h.Add(HasFreshOutGaugeData);
            h.Add(HasFreshCarData);
            h.Add(now < _notificationUntil);
            h.Add(now < _caNotificationUntil);
            h.Add(_notificationText);
            h.Add(_caNotificationText);
            h.Add(_accentColor.ToArgb());
            h.Add(_comboDisplayed);
            h.Add(_combo);
            h.Add(ComboTimeoutSec);
            h.Add(ScoreOverlayEnabled);
            h.Add(SpeedoTachoEnabled);
            h.Add(SpeedoReplacedByInSimHud);
            h.Add(SpeedoTachoOffsetX);
            h.Add(SpeedoTachoOffsetY);
            h.Add(SpeedoTachoScale);
            h.Add(RedlineColor.ToArgb());
            h.Add(RedlineThickness);
            h.Add(SpeedoTachoUseMph);
            h.Add(SpeedoTachoType);
            h.Add(SpeedoTachoShowRpmDigits);
            h.Add(SpeedoTachoShowUnit);
            h.Add(SpeedoTachoShowMinorTicks);
            h.Add(SpeedoTachoShowMajorTicks);
            h.Add(SpeedoTachoPlainDigits);
            h.Add(SpeedoTachoInstantSpeed);
            h.Add(SpeedoTachoAirSpeed);
            h.Add(SpeedoTachoShowTelltales);
            h.Add(SpeedoTachoCenterUnit);
            h.Add(SpeedoTachoTextColor.ToArgb());
            h.Add(SpeedoTachoIndicatorColor.ToArgb());
            h.Add(SpeedoTachoMidColor.ToArgb());
            h.Add(SpeedoTachoTickColor.ToArgb());
            h.Add(SpeedoTachoBackgroundColor.ToArgb());
            h.Add(SpeedoTachoFuelColor.ToArgb());
            h.Add(SpeedoTachoTurboColor.ToArgb());
            h.Add(SpeedoBackgroundImage);
            h.Add(SpeedoBackgroundPanX);
            h.Add(SpeedoBackgroundPanY);
            h.Add(SpeedoBackgroundZoom);
            h.Add(SpeedoBackgroundOpacity);
            h.Add(ControllerAssistBarVisible);
            return h.ToHashCode();
        }
        private int _unchangedFrames;

        private void SetRenderInterval(int intervalMs)
        {
            if (_renderTimer.Interval != intervalMs)
                _renderTimer.Interval = intervalMs;
        }

        private void UpdateRenderCadence(bool frameChanged)
        {
            if (frameChanged)
            {
                _unchangedFrames = 0;
                SetRenderInterval(ActiveRenderIntervalMs);
                return;
            }

            if (++_unchangedFrames >= IdleFrameThreshold)
                SetRenderInterval(IdleRenderIntervalMs);
        }

        private bool Render()
        {
            if (Width <= 0 || Height <= 0) return false;

            var surface = AcquireSurface();

            var g = Graphics.FromImage(surface);
            try
            {
                long p = PerfProbe.Begin();
                ClearSurface(g);
                BeginTextFrame();
                BeginScanRegions();
                PerfProbe.End(PerfSection.OverlayClear, p);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float centerX = Width / 2f;

                if (ScoreOverlayEnabled)
                {
                    EnterRegion(g, new RectangleF(0, 0, Width, Math.Min(Height, ScoreFadeMaxHeight)));
                    if (_scoreVisibleBlend > 0.01)
                    {
                        p = PerfProbe.Begin();
                        DrawScoreCollapsible(g, centerX);
                        PerfProbe.End(PerfSection.OverlayScore, p);
                    }
                    if (_scoreVisibleBlend < 0.99)
                    {
                        p = PerfProbe.Begin();
                        DrawScoreCollapsedIndicator(g, centerX);
                        PerfProbe.End(PerfSection.OverlayScoreIndicator, p);
                    }
                    g.ResetClip();

                    if (!_inMenu)
                    {
                        p = PerfProbe.Begin();
                        DrawLapScoreBox(g);
                        DrawLapResultPopup(g);
                        PerfProbe.End(PerfSection.OverlayLap, p);
                    }
                }
                if (_scoreVisibleBlend <= 0.01)
                    ReleaseScoreFadeBuffer();

                p = PerfProbe.Begin();
                DrawSpeedoTacho(g);
                PerfProbe.End(PerfSection.OverlaySpeedo, p);
                p = PerfProbe.Begin();
                DrawControllerAssistBar(g, centerX);
                PerfProbe.End(PerfSection.OverlayAssistBar, p);
                p = PerfProbe.Begin();
                DrawControllerAssistNotification(g, centerX);
                DrawAchievementToast(g, centerX);
                PerfProbe.End(PerfSection.OverlayAssistNotice, p);
            }
            finally
            {
                long p = PerfProbe.Begin();
                g.Dispose();
                PerfProbe.End(PerfSection.OverlayFlush, p);
            }

            return PresentSurface();
        }

        private void DrawControllerAssistNotification(Graphics g, float centerX)
        {
            if (_caNotificationText == null) return;

            double remaining = (_caNotificationUntil - DateTime.Now).TotalSeconds;
            if (remaining <= 0)
            {
                _caNotificationText = null;
                return;
            }

            double alpha = remaining < 0.4 ? Math.Clamp(remaining / 0.4, 0, 1) : 1.0;
            var font = _activeFont20;
            var size = MeasureText(g, _caNotificationText, font);
            float x = centerX - size.Width / 2f;
            float y = Height / 2f - size.Height / 2f;

            float pad = font.Size * 0.5f + 4f;
            EnterRegion(g, new RectangleF(x - pad, y - pad, size.Width + pad * 2f, size.Height + pad * 2f));
            DrawOutlinedText(g, _caNotificationText, font, x, y,
                DrawingHelpers.WithAlpha(Color.White, alpha), DrawingHelpers.WithAlpha(Color.Black, alpha * 0.6), outline: true);
            g.ResetClip();
        }

        private readonly SolidBrush _caTrackBrush = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        private readonly Pen _caCenterPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
        private readonly SolidBrush _caRawBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255));
        private readonly SolidBrush _caOutputBrush = new SolidBrush(Color.FromArgb(255, 10, 132, 255));

        private readonly Dictionary<(int Kind, float X, float Y, float W, float H, float R), GraphicsPath> _pathCache = new();

        private const int AchievementCardWidth = 500;
        private const int AchievementCardMinHeight = 76;
        private const float AchievementIconBlockWidth = 78f;
        private const float AchievementCardPadX = 14f;
        private const float AchievementCardPadY = 9f;
        private const float AchievementCardRadius = 6f;
        private const float AchievementGlyphSize = 46f;
        private const int AchievementQueueLimit = 3;
        private const double AchievementSlideInSec = 0.35;
        private const double AchievementHoldSec = 5.0;
        private const double AchievementSlideOutSec = 0.3;
        private const double AchievementGapSec = 0.4;
        private const float AchievementBottomMargin = 90f;

        private readonly Queue<AchievementToast> _achievementQueue = new();
        private AchievementToast? _achievementCurrent;
        private DateTime _achievementStart;
        private DateTime _achievementNextAllowed = DateTime.MinValue;
        private Bitmap? _achievementCard;

        public void ShowAchievement(AchievementToast toast)
        {
            if (_achievementQueue.Count >= AchievementQueueLimit) return;
            _achievementQueue.Enqueue(toast);
            MarkInput();
        }

        private void DrawAchievementToast(Graphics g, float centerX)
        {
            var now = DateTime.Now;
            if (_achievementCurrent == null)
            {
                if (_achievementQueue.Count == 0 || now < _achievementNextAllowed) return;
                _achievementCurrent = _achievementQueue.Dequeue();
                _achievementStart = now;
                _achievementCard?.Dispose();
                _achievementCard = RenderAchievementCard(_achievementCurrent);
            }

            double elapsed = (now - _achievementStart).TotalSeconds;
            double total = AchievementSlideInSec + AchievementHoldSec + AchievementSlideOutSec;
            if (elapsed >= total || _achievementCard == null)
            {
                _achievementCurrent = null;
                _achievementCard?.Dispose();
                _achievementCard = null;
                _achievementNextAllowed = now.AddSeconds(AchievementGapSec);
                return;
            }

            int cardWidth = _achievementCard.Width, cardHeight = _achievementCard.Height;
            float hiddenOffset = cardHeight + AchievementBottomMargin + 10f;
            float offset;
            if (elapsed < AchievementSlideInSec)
                offset = (float)((1.0 - EaseOutBack(elapsed / AchievementSlideInSec)) * hiddenOffset);
            else if (elapsed > AchievementSlideInSec + AchievementHoldSec)
            {
                double t = (elapsed - AchievementSlideInSec - AchievementHoldSec) / AchievementSlideOutSec;
                offset = (float)(t * t * hiddenOffset);
            }
            else
                offset = 0f;

            int x = (int)Math.Round(centerX - cardWidth / 2f);
            int y = (int)Math.Round(Height - AchievementBottomMargin - cardHeight + offset);
            if (y >= Height) return;

            EnterRegion(g, new RectangleF(x, y, cardWidth, cardHeight));
            var quality = g.CompositingQuality;
            var interpolation = g.InterpolationMode;
            var pixelOffset = g.PixelOffsetMode;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_achievementCard, new Rectangle(x, y, cardWidth, cardHeight),
                0, 0, cardWidth, cardHeight, GraphicsUnit.Pixel);
            g.CompositingQuality = quality;
            g.InterpolationMode = interpolation;
            g.PixelOffsetMode = pixelOffset;
            g.ResetClip();
        }

        private Bitmap RenderAchievementCard(AchievementToast toast)
        {
            Color rarity = AchievementIcons.RarityColor(toast.Rarity);
            float textX = AchievementIconBlockWidth + AchievementCardPadX;
            float textW = AchievementCardWidth - textX - AchievementCardPadX;

            SizeF titleSize, nameSize, descSize;
            using (var measureBmp = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(measureBmp))
            {
                mg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                titleSize = mg.MeasureString(toast.Title, _activeFont11);
                nameSize = mg.MeasureString(toast.Name, _activeFont20, (int)textW, AchievementWrapFormat);
                descSize = string.IsNullOrEmpty(toast.Description)
                    ? SizeF.Empty
                    : mg.MeasureString(toast.Description, _activeFont11, (int)textW, AchievementWrapFormat);
            }

            float titleY = AchievementCardPadY;
            float nameY = titleY + titleSize.Height;
            float descY = nameY + nameSize.Height + 1f;
            float contentBottom = descY + descSize.Height + AchievementCardPadY;
            int height = Math.Max(AchievementCardMinHeight, (int)Math.Ceiling(contentBottom));

            var bmp = new Bitmap(AchievementCardWidth, height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;

            using (var iconPath = DrawingHelpers.RoundedLeftRect(new RectangleF(0f, 0f, AchievementIconBlockWidth + 1f, height), AchievementCardRadius))
            using (var iconBrush = new SolidBrush(Color.FromArgb(235, rarity)))
                g.FillPath(iconBrush, iconPath);

            using (var bodyPath = DrawingHelpers.RoundedRightRect(new RectangleF(AchievementIconBlockWidth, 0f, AchievementCardWidth - AchievementIconBlockWidth, height), AchievementCardRadius))
            using (var bodyBrush = new SolidBrush(Color.FromArgb(205, 14, 14, 18)))
                g.FillPath(bodyBrush, bodyPath);

            float glyphSize = Math.Min(AchievementGlyphSize, height - AchievementCardPadY * 2f);
            var glyphRect = new RectangleF((AchievementIconBlockWidth - glyphSize) / 2f, (height - glyphSize) / 2f, glyphSize, glyphSize);
            float luminance = (0.299f * rarity.R + 0.587f * rarity.G + 0.114f * rarity.B) / 255f;
            Color glyphColor = luminance > 0.6f ? Color.FromArgb(255, 22, 22, 26) : Color.White;
            AchievementIcons.DrawGlyph(g, toast.Icon, glyphRect, glyphColor, false);

            DrawCardText(g, toast.Title, _activeFont11, new RectangleF(textX, titleY, textW, titleSize.Height + 2f), rarity, null);

            if (!string.IsNullOrEmpty(toast.MethodLabel))
            {
                var methodSize = g.MeasureString(toast.MethodLabel, _activeFont11);
                Color methodColor = Color.FromArgb(200, 200, 200, 210);
                float mx = AchievementCardWidth - AchievementCardPadX - methodSize.Width;
                if (mx - 22f > textX + titleSize.Width)
                {
                    DrawCardText(g, toast.MethodLabel, _activeFont11, new RectangleF(mx, titleY, methodSize.Width + 2f, methodSize.Height + 2f), methodColor, null);
                    AchievementIcons.DrawMethodGlyph(g, new RectangleF(mx - 22f, titleY + 2f, 18f, 18f), toast.Method, methodColor);
                }
            }

            DrawCardText(g, toast.Name, _activeFont20, new RectangleF(textX, nameY, textW, nameSize.Height + 2f), Color.White, AchievementWrapFormat);

            if (!string.IsNullOrEmpty(toast.Description))
                DrawCardText(g, toast.Description, _activeFont11, new RectangleF(textX, descY, textW, descSize.Height + 2f),
                    Color.FromArgb(225, 215, 215, 222), AchievementWrapFormat);

            return bmp;
        }

        private void DrawCardText(Graphics g, string text, Font font, RectangleF rect, Color color, StringFormat? format)
        {
            _scratchBrush.Color = Color.FromArgb(Math.Min(170, (int)color.A), 0, 0, 0);
            var shadowRect = new RectangleF(rect.X + 1.5f, rect.Y + 1.5f, rect.Width, rect.Height);
            if (format != null) g.DrawString(text, font, _scratchBrush, shadowRect, format);
            else g.DrawString(text, font, _scratchBrush, shadowRect);

            _scratchBrush.Color = color;
            if (format != null) g.DrawString(text, font, _scratchBrush, rect, format);
            else g.DrawString(text, font, _scratchBrush, rect);
        }

        private void EnterRegion(Graphics g, RectangleF area)
        {
            g.SetClip(AddScanRegion(area));
        }

        private void FillOnEmpty(Graphics g, GraphicsPath path)
        {
            var quality = g.CompositingQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.FillPath(_scratchBrush, path);
            g.CompositingQuality = quality;
        }

        private void FillOnEmpty(Graphics g, RectangleF rect)
        {
            var quality = g.CompositingQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.FillRectangle(_scratchBrush, rect);
            g.CompositingQuality = quality;
        }

        private GraphicsPath GetCachedPath(int kind, RectangleF rect, float radius)
        {
            var key = (kind, rect.X, rect.Y, rect.Width, rect.Height, radius);
            if (_pathCache.TryGetValue(key, out var cached)) return cached;

            if (_pathCache.Count >= 128)
            {
                foreach (var path in _pathCache.Values) path.Dispose();
                _pathCache.Clear();
            }

            GraphicsPath created = kind switch
            {
                0 => DrawingHelpers.RoundedRect(rect, radius),
                1 => DrawingHelpers.RoundedLeftRect(rect, radius),
                2 => DrawingHelpers.RoundedRightRect(rect, radius),
                _ => DrawingHelpers.RoundedPath(Rectangle.Round(rect), (int)radius),
            };
            _pathCache[key] = created;
            return created;
        }

        private ImageAttributes? _fadeAttrs;
        private ImageAttributes? _bgAttrs;
        private float _bgAttrsOpacity = -1f;
        private GraphicsPath? _dialClipPath;
        private RectangleF _dialClipRect;

        private GraphicsPath? _shimmerPath;
        private GraphicsPath? _shimmerShadowPath;
        private RectangleF _shimmerBounds;
        private string? _shimmerText;
        private Font? _shimmerFont;
        private float _shimmerX, _shimmerY;
        private void DrawControllerAssistBar(Graphics g, float centerX)
        {
            if (!ControllerAssistBarVisible || !_controllerAssistActive) return;

            const float barW = 260f, barH = 10f, markerW = 4f, markerH = 18f;
            float x = centerX - barW / 2f;
            float y = Height - 44f;

            var track = new RectangleF(x, y, barW, barH);
            EnterRegion(g, new RectangleF(x - 20f, y - 12f, barW + 40f, barH + 32f));
            g.FillPath(_caTrackBrush, GetCachedPath(3, track, (int)(barH / 2)));

            float centerLineX = x + barW / 2f;
            g.DrawLine(_caCenterPen, centerLineX, y - 2f, centerLineX, y + barH + 2f);

            float rawX = x + (float)((_controllerAssistRawPercent + 100.0) / 200.0) * barW;
            g.FillRectangle(_caRawBrush, rawX - markerW / 2f, y + barH / 2f - markerH / 2f, markerW, markerH);

            float outputX = x + (float)((_controllerAssistOutputPercent + 100.0) / 200.0) * barW;
            g.FillRectangle(_caOutputBrush, outputX - markerW / 2f, y + barH / 2f - markerH / 2f - 3f, markerW, markerH + 6f);
            g.ResetClip();
        }

        private const float ScoreCollapseAnchorY = 26f;
        private Bitmap? _scoreFadeBuffer;

        private void DrawScoreContent(Graphics g, float centerX, float scale)
        {
            var savedState = g.Save();
            g.TranslateTransform(centerX, ScoreCollapseAnchorY);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-centerX, -ScoreCollapseAnchorY);

            DrawIdleScore(g, centerX, out float idlebarY);
            DrawActiveHud(g, centerX, out float barX, out float barY, out float barW, out float barH);
            DrawBonusBox(g, centerX, barY + idlebarY, barH);

            g.Restore(savedState);
        }

        private void DrawScoreCollapsible(Graphics g, float centerX)
        {
            float scale = (float)(0.08 + 0.92 * _scoreVisibleBlend);

            if (_scoreVisibleBlend >= 0.995 || Width <= 0 || Height <= 0)
            {
                ReleaseScoreFadeBuffer();
                DrawScoreContent(g, centerX, scale);
                return;
            }

            int bufW = Math.Min(Width, ScoreFadeMaxWidth);
            int bufH = Math.Min(Height, ScoreFadeMaxHeight);
            int originX = (int)Math.Round(centerX - bufW / 2f);

            if (_scoreFadeBuffer == null || _scoreFadeBuffer.Width != bufW || _scoreFadeBuffer.Height != bufH)
            {
                _scoreFadeBuffer?.Dispose();
                _scoreFadeBuffer = new Bitmap(bufW, bufH, PixelFormat.Format32bppArgb);
            }

            using (var fg = Graphics.FromImage(_scoreFadeBuffer))
            {
                fg.Clear(Color.Transparent);
                fg.SmoothingMode = SmoothingMode.AntiAlias;
                fg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                fg.CompositingQuality = CompositingQuality.HighQuality;
                fg.TranslateTransform(-originX, 0);

                DrawScoreContent(fg, centerX, scale);
            }

            _fadeAttrs ??= new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = (float)_scoreVisibleBlend };
            _fadeAttrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(_scoreFadeBuffer, new Rectangle(originX, 0, bufW, bufH), 0, 0, bufW, bufH, GraphicsUnit.Pixel, _fadeAttrs);
        }

        private const int ScoreFadeMaxWidth = 1200;
        private const int ScoreFadeMaxHeight = 500;

        private void ReleaseScoreFadeBuffer()
        {
            if (_scoreFadeBuffer == null) return;

            _scoreFadeBuffer.Dispose();
            _scoreFadeBuffer = null;
        }

        private static double EaseOutBack(double t)
        {
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            double t1 = Math.Clamp(t, 0, 1) - 1;
            return 1 + c3 * t1 * t1 * t1 + c1 * t1 * t1;
        }

        private void DrawScoreCollapsedIndicator(Graphics g, float centerX)
        {
            double revealAmount = 1.0 - _scoreVisibleBlend;
            double bouncy = EaseOutBack(Math.Min(1.0, revealAmount * 1.4));

            const float maxRadius = 10f;
            float radius = maxRadius * (float)Math.Max(0, bouncy);
            if (radius < 0.5f) return;

            double alpha = Math.Clamp(bouncy * 0.55, 0, 0.5);

            _scratchBrush.Color = DrawingHelpers.WithAlpha(Color.White, alpha);
            g.FillEllipse(_scratchBrush, centerX - radius, ScoreCollapseAnchorY - radius, radius * 2, radius * 2);
        }

        private void DrawIdleScore(Graphics g, float centerX, out float idlebarY)
        {
            bool notificationActive = DateTime.Now < _notificationUntil && _notificationText != null;
            double alpha = notificationActive ? 0.9 : 0.75 - _activeBlend;
            float slideX = _isActiveNow ? (float)(-_activeBlend * 160) : (float)(_activeBlend * 160);

            string text = notificationActive
                ? _notificationText!
                : _scoreText.Get((long)Math.Round(_displayedScore));
            var font = _idleFont32;
            var size = MeasureText(g, text, font);
            idlebarY = size.Height;
            if (alpha <= 0.01) return;
            float x = centerX - size.Width / 2f + slideX;
            DrawRevCutTextGlow(g, text, font, x, 5);

            DrawOutlinedText(g, text, font, x, 5, DrawingHelpers.WithAlpha(_accentColor, alpha), DrawingHelpers.WithAlpha(_accentColorBack, alpha), outline: false);
        }

        private void DrawActiveHud(Graphics g, float centerX, out float barX, out float barY, out float barW, out float barH)
        {
            barX = barY = barW = barH = 0;
            double alphacut = 0.25;
            double alpha = _activeBlend;
            if (alpha <= 0.01) return;

            float slideX = _isActiveNow ? (float)((1.0 - _activeBlend) * 220) : (float)(-(1.0 - _activeBlend) * 220);

            string scoreText = _scoreText.Get((long)Math.Round(_displayedScore));
            var scoreFont = _activeFont32;
            var scoreSize = MeasureText(g, scoreText, scoreFont);

            var comboFont = _activeFont24;
            string comboText = _comboText.Get(_combo);
            var comboSize = MeasureText(g, comboText, comboFont);

            float comboH = comboSize.Height;

            float totalW = scoreSize.Width;
            float totalH = scoreSize.Height;

            float startX = centerX - totalW / 2f + slideX;

            DrawRevCutTextGlow(g, scoreText, scoreFont, startX, 5);

            DrawOutlinedText(g, scoreText, scoreFont, startX, 5, DrawingHelpers.WithAlpha(_accentColor, alpha), DrawingHelpers.WithAlpha(_accentColorBack, alpha), outline: false);

            var labelFont = _activeFont22;
            var runFont = _activeFont22;

            string labelText = _driftLabelText ?? "";
            string runText = _runText.Get((long)Math.Round(_displayedRun));

            var labelSize = MeasureText(g, labelText, labelFont);
            var runSize = MeasureText(g, runText, runFont);

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

            var labelPath = GetCachedPath(1, new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6);
            var labelClipState = g.Save();
            {
                g.SetClip(labelPath, CombineMode.Replace);

                _scratchBrush.Color = DrawingHelpers.WithAlpha(_accentColor, alpha - alphacut);
                FillOnEmpty(g, new RectangleF(barX, barY, labelBoxW, barH));

                float consumed = labelBoxW * (1f - (float)comboFraction);
                if (consumed > 0.5f)
                {
                    _scratchBrush.Color = DrawingHelpers.WithAlpha(Color.Black, (alpha * 0.85) - alphacut);
                    g.FillRectangle(_scratchBrush, barX + labelBoxW - consumed, barY, consumed, barH);
                }

                g.Restore(labelClipState);
            }

            {
                var runPath = GetCachedPath(2, new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6);
                _scratchBrush.Color = DrawingHelpers.WithAlpha(DrawingHelpers.Darken(_accentColor, 0.15), alpha - alphacut);
                FillOnEmpty(g, runPath);
            }

            DrawAnimatedCombo(
                g,
                comboFont,
                comboText,
                barX + labelBoxW + runBoxW + 5,
                barY + (barH / 2) - (comboH / 2),
                DrawingHelpers.WithAlpha(_accentColor, alpha),
                DrawingHelpers.WithAlpha(_accentColorBack, alpha));

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
                string angleText = _angleText.Get((long)Math.Round(_displayedAngle));
                var angleFont = _activeFont24;
                var angleSize = MeasureText(g, angleText, angleFont);
                float angleW = angleSize.Width;
                float angleH = angleSize.Height;

                DrawOutlinedText(
                   g,
                   angleText,
                   angleFont,
                   barX - angleW - 2 + ((float)angleOffsetX / 2),
                   barY + (barH / 2) - (angleH / 2),
                   DrawingHelpers.WithAlpha(_accentColor, alpha * angleAlpha),
                   DrawingHelpers.WithAlpha(_accentColorBack, alpha * angleAlpha),
                   outline: false);
            }
            DrawOutlinedText(g, labelText, labelFont, barX + padH, barY + padV,
                DrawingHelpers.WithAlpha(LabelTextColor, alpha), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);

            DrawOutlinedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV, DrawingHelpers.WithAlpha(LabelTextColor, alpha), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);

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

                string current = CharText.Of(c.Current);
                const float LetterSpacing = -12f;
                float w = MeasureText(g, current, font).Width + LetterSpacing;

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
                    CharText.Of(c.Previous),
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

            _bonusItems.RemoveAll(it => (now - it.Start).TotalSeconds > it.DurationSec);
            if (_bonusItems.Count == 0) return;

            var font = _activeFont15;
            const float padH = 7, padV = 4;
            double slideInSec = BonusSlideMs / 1000.0;

            var widths = new float[_bonusItems.Count];
            var heights = new float[_bonusItems.Count];
            var alphas = new double[_bonusItems.Count];
            var slideOffsets = new double[_bonusItems.Count];

            float totalW = 0;
            for (int i = 0; i < _bonusItems.Count; i++)
            {
                var item = _bonusItems[i];
                var sz = MeasureText(g, item.Text, font);
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
                double slideOutStart = item.DurationSec - slideInSec;
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
                    double itemAlphaCut = _bonusItems[i].BoostOpacity ? 0.1 : alphacut;

                    {
                        var path = GetCachedPath(0, new RectangleF(boxX, boxY, w, h), 6);
                        _scratchBrush.Color = DrawingHelpers.WithAlpha(_accentColor, alpha - itemAlphaCut);
                        FillOnEmpty(g, path);
                    }

                    DrawOutlinedText(g, _bonusItems[i].Text, font, boxX + padH, boxY + padV,
                        DrawingHelpers.WithAlpha(LabelTextColor, alpha), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
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
            string lapValue = _lapText.Get((long)Math.Round(_displayedLapScore));
            string lastLapLabel = Localization.T("hud.lastlapscore");
            string lastLapValue = _lastLapText.Get(_lastLapScoreDisplay);
            string bestLabel = Localization.T("hud.bestlapscore");
            string bestValue = _bestLapText.Get(_bestLapScore);
            string contextLabel = _lapContextLabel;

            var lapLabelSize = MeasureText(g, lapLabel, titleFont);
            var lapValueSize = MeasureText(g, lapValue, valueFont);
            var lastLapLabelSize = MeasureText(g, lastLapLabel, titleFont);
            var lastLapValueSize = MeasureText(g, lastLapValue, valueFont);
            var bestLabelSize = MeasureText(g, bestLabel, titleFont);
            var bestValueSize = MeasureText(g, bestValue, valueFont);
            var contextSize = string.IsNullOrEmpty(contextLabel)
                ? SizeF.Empty : MeasureText(g, contextLabel, contextFont);

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
            EnterRegion(g, new RectangleF(boxX - 6f, boxY - 6f, boxW + 12f, boxH + 12f));

            {
                var path = GetCachedPath(0, new RectangleF(boxX, boxY, boxW, boxH), 10);
                _scratchBrush.Color = DrawingHelpers.WithAlpha(Color.FromArgb(255, 18, 18, 22), alpha * 0.59);
                FillOnEmpty(g, path);
            }

            float textX = boxX + 14;
            float y = boxY + 10;

            DrawOutlinedText(g, lapLabel, titleFont, textX, y,
                DrawingHelpers.WithAlpha(Color.White, alpha * 0.82), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            y += lapLabelSize.Height + rowGap;

            DrawOutlinedText(g, lapValue, valueFont, textX, y,
                DrawingHelpers.WithAlpha(_accentColor, alpha), DrawingHelpers.WithAlpha(_accentColorBack, alpha), outline: false);
            y += lapValueSize.Height + rowGap;

            DrawOutlinedText(g, lastLapLabel, titleFont, textX, y,
                DrawingHelpers.WithAlpha(Color.White, alpha * 0.82), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapLabelSize.Height + rowGap;

            DrawOutlinedText(g, lastLapValue, valueFont, textX, y,
                DrawingHelpers.WithAlpha(Color.FromArgb(120, 200, 255), alpha), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            y += lastLapValueSize.Height + rowGap;

            DrawOutlinedText(g, bestLabel, titleFont, textX, y,
                DrawingHelpers.WithAlpha(Color.White, alpha * 0.82), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            y += bestLabelSize.Height + rowGap;

            DrawOutlinedText(g, bestValue, valueFont, textX, y,
                DrawingHelpers.WithAlpha(Color.FromArgb(255, 215, 0), alpha), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            y += bestValueSize.Height;

            if (!string.IsNullOrEmpty(contextLabel))
            {
                y += 8;
                DrawOutlinedText(g, contextLabel, contextFont, textX, y,
                    DrawingHelpers.WithAlpha(Color.FromArgb(255, 170, 170, 180), alpha * 0.85), DrawingHelpers.WithAlpha(Color.Black, alpha), outline: false);
            }
            g.ResetClip();
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

            _scratchPen.Color = DrawingHelpers.WithAlpha(_accentColor, alpha);
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

        private static readonly string[] GearNumberTexts = BuildGearNumberTexts();

        private static string[] BuildGearNumberTexts()
        {
            var texts = new string[16];
            for (int i = 0; i < texts.Length; i++)
                texts[i] = i.ToString();
            return texts;
        }

        private readonly CachedText<long> _scoreText = new("{0:N0}");
        private readonly CachedText<long> _runText = new("{0:N0}");
        private readonly CachedText<long> _angleText = new("{0:N0}°");
        private readonly CachedText<double> _comboText = new("x{0:0.0}");
        private readonly CachedText<long> _lapResultText = new("{0:N0}");
        private readonly CachedText<long> _lapText = new("{0:N0}");
        private readonly CachedText<long> _lastLapText = new("{0:N0}");
        private readonly CachedText<long> _bestLapText = new("{0:N0}");
        private readonly CachedText<int> _speedPlainText = new("{0}");
        private readonly CachedText<int> _speedPaddedText = new("{0:D3}");
        private string? _unitSource;
        private string _unitUpper = "";

        private string UpperUnitText(string unit)
        {
            if (!ReferenceEquals(unit, _unitSource))
            {
                _unitUpper = unit.ToUpperInvariant();
                _unitSource = unit;
            }
            return _unitUpper;
        }

        private static string GearToDisplayText(int gear)
        {
            if (gear <= 0) return "R";
            if (gear == 1) return "N";
            return gear - 1 < GearNumberTexts.Length ? GearNumberTexts[gear - 1] : (gear - 1).ToString();
        }

        private void DrawSpeedoTacho(Graphics g)
        {
            if (!SpeedoTachoEnabled || SpeedoReplacedByInSimHud) return;

            if (!HasFreshOutGaugeData) return;

            float scale = Math.Max(0.3f, SpeedoTachoScale * SpeedoBaseScale);
            float scaledSize = SpeedoTachoDesignSize * scale;

            const float marginX = 0f, marginY = 0f;
            float anchorRight = Width - marginX + SpeedoBaseOffsetX + SpeedoTachoOffsetX;
            float anchorBottom = Height - marginY - (SpeedoBaseOffsetY + SpeedoTachoOffsetY);

            float originX = anchorRight - scaledSize;
            float originY = anchorBottom - scaledSize;
            float glowMargin = 30f * scale;
            EnterRegion(g, new RectangleF(originX - glowMargin, originY - glowMargin, scaledSize + glowMargin * 2f, scaledSize + glowMargin * 2f));

            var savedState = g.Save();
            g.TranslateTransform(originX, originY);
            g.ScaleTransform(scale, scale);

            try
            {
                if (SpeedoTachoType == SpeedoPresetData.TachoType2)
                    DrawTachoType2(g);
                else
                    DrawSpeedoTachoContent(g);
            }
            finally
            {
                g.Restore(savedState);
                g.ResetClip();
            }
        }

        private void TimedSpeedoBackground(Graphics g, RectangleF dialRect)
        {
            long p = PerfProbe.Begin();
            DrawSpeedoBackgroundImage(g, dialRect);
            PerfProbe.End(PerfSection.OverlaySpeedoBackground, p);
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

            if (_dialClipPath == null || _dialClipRect != dialRect)
            {
                _dialClipPath?.Dispose();
                _dialClipPath = new GraphicsPath();
                _dialClipPath.AddEllipse(dialRect);
                _dialClipRect = dialRect;
            }

            float opacity = Math.Clamp(SpeedoBackgroundOpacity, 0f, 1f);
            if (_bgAttrs == null || _bgAttrsOpacity != opacity)
            {
                _bgAttrs ??= new ImageAttributes();
                var matrix = new ColorMatrix { Matrix33 = opacity };
                _bgAttrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                _bgAttrsOpacity = opacity;
            }

            var clipState = g.Save();
            g.SetClip(_dialClipPath, CombineMode.Intersect);
            g.DrawImage(img, new Rectangle((int)drawX, (int)drawY, (int)drawW, (int)drawH),
                0, 0, img.Width, img.Height, GraphicsUnit.Pixel, _bgAttrs);
            g.Restore(clipState);
        }

        private readonly TachoType2State _tachoType2State = new();

        private void DrawTachoType2(Graphics g)
        {
            double displaySpeed = SpeedoTachoUseMph ? _displayedSpeedKmh * KmhToMph : _displayedSpeedKmh;
            var s = _tachoType2State;
            s.Rpm = _currentRpm;
            s.CalibratedMaxRpm = _calibratedMaxRpm;
            s.RevCut = _revCutActive;
            s.GearText = GearToDisplayText(_currentGear);
            s.SpeedText = (SpeedoTachoPlainDigits ? _speedPlainText : _speedPaddedText).Get((int)Math.Round(displaySpeed));
            s.UnitText = SpeedoTachoUseMph ? "mph" : Localization.T("speedometer.unit");
            s.ShowUnit = SpeedoTachoShowUnit;
            s.ShowMarkers = SpeedoTachoShowMajorTicks;
            s.Fuel = _dashFuel;
            s.TurboFraction = _turboFraction;
            s.HighBeam = (_dashShowLights & 0x0002) != 0;
            s.Handbrake = (_dashShowLights & 0x0004) != 0;
            s.TractionControl = (_dashShowLights & 0x0010) != 0;
            s.LeftSignal = (_dashShowLights & 0x0020) != 0;
            s.RightSignal = (_dashShowLights & 0x0040) != 0;
            s.Abs = (_dashShowLights & 0x0400) != 0;
            s.ShowTelltales = SpeedoTachoShowTelltales;
            s.TextColor = SpeedoTachoTextColor;
            s.StartColor = SpeedoTachoIndicatorColor;
            s.MidColor = SpeedoTachoMidColor;
            s.TickColor = SpeedoTachoTickColor;
            s.RedlineColor = RedlineColor;
            s.BackgroundColor = SpeedoTachoBackgroundColor;
            s.FuelColor = SpeedoTachoFuelColor;
            s.TurboColor = SpeedoTachoTurboColor;
            s.LayerKey = (SpeedoBackgroundImage, SpeedoBackgroundZoom, SpeedoBackgroundPanX, SpeedoBackgroundPanY, SpeedoBackgroundOpacity);

            TachoType2Renderer.Draw(g, s, TimedSpeedoBackground);
        }

        private readonly TachoType1State _tachoType1State = new();

        private void DrawSpeedoTachoContent(Graphics g)
        {
            double displaySpeed = SpeedoTachoUseMph ? _displayedSpeedKmh * KmhToMph : _displayedSpeedKmh;
            var s = _tachoType1State;
            s.Rpm = _currentRpm;
            s.CalibratedMaxRpm = _calibratedMaxRpm;
            s.RevCut = _revCutActive;
            s.GlowIntensity = GetRevCutGlowIntensity();
            s.GearText = GearToDisplayText(_currentGear);
            s.SpeedText = (SpeedoTachoPlainDigits ? _speedPlainText : _speedPaddedText).Get((int)Math.Round(displaySpeed));
            s.UnitText = SpeedoTachoUseMph ? "MPH" : UpperUnitText(Localization.T("speedometer.unit"));
            s.ShowUnit = SpeedoTachoShowUnit;
            s.CenterUnit = SpeedoTachoCenterUnit;
            s.ShowRpmDigits = SpeedoTachoShowRpmDigits;
            s.ShowMajorTicks = SpeedoTachoShowMajorTicks;
            s.ShowMinorTicks = SpeedoTachoShowMinorTicks;
            s.ShowTelltales = SpeedoTachoShowTelltales;
            s.Telltales = TelltaleFlags.FromShowLights(_dashShowLights);
            s.RedlineThickness = RedlineThickness;
            s.TextColor = SpeedoTachoTextColor;
            s.IndicatorColor = SpeedoTachoIndicatorColor;
            s.TickColor = SpeedoTachoTickColor;
            s.RedlineColor = RedlineColor;
            s.BackgroundColor = SpeedoTachoBackgroundColor;
            s.LayerKey = (SpeedoBackgroundImage, SpeedoBackgroundZoom, SpeedoBackgroundPanX, SpeedoBackgroundPanY, SpeedoBackgroundOpacity);

            TachoType1Renderer.Draw(g, s, TimedSpeedoBackground);
        }

        private double GetRevCutGlowIntensity()
        {
            if (_revCutBlend <= 0.01) return 0;

            double pulse = 0.85 + 0.15 * Math.Sin((DateTime.Now - _revCutBlendStart).TotalSeconds * 6.0);
            return _revCutBlend * pulse;
        }

        private readonly Dictionary<(string, Font), SizeF> _measureCache = new();

        private SizeF MeasureText(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;

            var key = (text, font);
            if (_measureCache.TryGetValue(key, out var size)) return size;

            if (_measureCache.Count >= 256) _measureCache.Clear();

            size = g.MeasureString(text, font);
            _measureCache[key] = size;
            return size;
        }

        private void DrawRevCutTextGlow(Graphics g, string text, Font font, float x, float y)
        {
            double glowStrength = GetRevCutGlowIntensity();
            if (glowStrength <= 0.01 || string.IsNullOrEmpty(text)) return;

            Color glowColor = DrawingHelpers.WithAlpha(Color.FromArgb(255, 40, 40), glowStrength * 0.05 * TextGlowStep * TextGlowStep);
            if (TryDrawTextGlowSprites(g, text, font, x, y, glowColor)) return;

            _scratchBrush.Color = glowColor;
            foreach (var (ox, oy) in TextGlowOffsets)
                g.DrawString(text, font, _scratchBrush, x + ox, y + oy);
        }

        private const int TextGlowRadius = 5;
        private const int TextGlowStep = 2;
        private static readonly (int X, int Y)[] TextGlowOffsets = BuildTextGlowOffsets();

        private static (int X, int Y)[] BuildTextGlowOffsets()
        {
            var offsets = new List<(int, int)>();
            for (int ox = -TextGlowRadius + 1; ox <= TextGlowRadius; ox += TextGlowStep)
                for (int oy = -TextGlowRadius + 1; oy <= TextGlowRadius; oy += TextGlowStep)
                {
                    if (ox == 0 && oy == 0) continue;
                    if (Math.Sqrt(ox * ox + oy * oy) > TextGlowRadius) continue;
                    offsets.Add((ox, oy));
                }
            return offsets.ToArray();
        }

        private Bitmap? _glowSprite;
        private Rectangle _glowSpriteDevice;
        private (string Text, Font Font, float X, float Y, float Dx, float Dy, int Alpha) _glowSpriteKey;

        private bool TryDrawTextGlowSprites(Graphics g, string text, Font font, float x, float y, Color glowColor)
        {
            float dx, dy;
            using (var m = g.Transform)
            {
                var e = m.Elements;
                if (e[0] != 1f || e[1] != 0f || e[2] != 0f || e[3] != 1f) return false;
                dx = e[4]; dy = e[5];
            }

            var key = (text, font, x, y, dx, dy, (int)glowColor.A);
            if (_glowSprite == null || !_glowSpriteKey.Equals(key))
            {
                var size = MeasureText(g, text, font);
                int left = (int)Math.Floor(x + dx - 2f);
                int top = (int)Math.Floor(y + dy - 2f);
                int width = (int)Math.Ceiling(size.Width) + 6;
                int height = (int)Math.Ceiling(size.Height) + 6;
                if (width <= 0 || height <= 0) return false;

                const int pad = TextGlowRadius;
                using var textSprite = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (var sg = Graphics.FromImage(textSprite))
                {
                    sg.SmoothingMode = g.SmoothingMode;
                    sg.TextRenderingHint = g.TextRenderingHint;
                    sg.CompositingQuality = g.CompositingQuality;
                    sg.TranslateTransform(dx - left, dy - top);
                    _scratchBrush.Color = glowColor;
                    sg.DrawString(text, font, _scratchBrush, x, y);
                }

                _glowSprite?.Dispose();
                _glowSprite = new Bitmap(width + pad * 2, height + pad * 2, PixelFormat.Format32bppPArgb);
                using (var cg = Graphics.FromImage(_glowSprite))
                {
                    cg.CompositingQuality = CompositingQuality.HighSpeed;
                    cg.InterpolationMode = InterpolationMode.NearestNeighbor;
                    cg.PixelOffsetMode = PixelOffsetMode.Half;
                    foreach (var (ox, oy) in TextGlowOffsets)
                        cg.DrawImage(textSprite, new Rectangle(pad + ox, pad + oy, width, height), 0, 0, width, height, GraphicsUnit.Pixel);
                }
                _glowSpriteDevice = new Rectangle(left - pad, top - pad, width + pad * 2, height + pad * 2);
                _glowSpriteKey = key;
            }

            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.DrawImage(_glowSprite, _glowSpriteDevice, 0, 0, _glowSpriteDevice.Width, _glowSpriteDevice.Height, GraphicsUnit.Pixel);
            }
            finally
            {
                g.Restore(state);
            }
            return true;
        }

        private readonly LayerCache _textSprites = new(160);
        private HashSet<(string, Font, int, int, bool, float, float)> _textSeenPrevious = new();
        private HashSet<(string, Font, int, int, bool, float, float)> _textSeenCurrent = new();
        private int _textSpriteDepth;

        private void BeginTextFrame()
        {
            (_textSeenPrevious, _textSeenCurrent) = (_textSeenCurrent, _textSeenPrevious);
            _textSeenCurrent.Clear();
        }

        private void DrawOutlinedText(Graphics g, string text, Font font, float x, float y, Color fill, Color outlineColor, bool outline = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (_textSpriteDepth == 0)
            {
                var key = (text, font, fill.ToArgb(), outlineColor.ToArgb(), outline, x, y);
                _textSeenCurrent.Add(key);
                if (_textSeenPrevious.Contains(key))
                {
                    var size = MeasureText(g, text, font);
                    float pad = font.Size * 0.5f + 2f;
                    var bounds = new RectangleF(x - pad, y - pad, size.Width + pad * 2f + 1.5f, size.Height + pad * 2f + 1.5f);
                    _textSprites.Draw(g, bounds, key, lg =>
                    {
                        _textSpriteDepth++;
                        try { DrawOutlinedTextDirect(lg, text, font, x, y, fill, outlineColor, outline); }
                        finally { _textSpriteDepth--; }
                    });
                    return;
                }
            }

            DrawOutlinedTextDirect(g, text, font, x, y, fill, outlineColor, outline);
        }

        private void DrawOutlinedTextDirect(Graphics g, string text, Font font, float x, float y, Color fill, Color outlineColor, bool outline)
        {
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

                _scratchBrush?.Dispose();
                _scratchPen?.Dispose();
                _renderTimer?.Dispose();
                _textSprites.Dispose();
                _achievementCard?.Dispose();
                _trackTimer?.Dispose();
                _scoreFadeBuffer?.Dispose();
                _glowSprite?.Dispose();

                _caTrackBrush.Dispose();
                _caCenterPen.Dispose();
                _caRawBrush.Dispose();
                _caOutputBrush.Dispose();
                foreach (var cachedPath in _pathCache.Values) cachedPath.Dispose();
                _pathCache.Clear();
                _fadeAttrs?.Dispose();
                _bgAttrs?.Dispose();
                _dialClipPath?.Dispose();
                _shimmerPath?.Dispose();
                _shimmerShadowPath?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}