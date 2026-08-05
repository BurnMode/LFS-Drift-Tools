using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

        private Font _idleFont24;
        private Font _idleFont26;
        private Font _idleFont32;
        private Font _activeFont22;
        private Font _activeFont24;
        private Font _activeFont32;
        private Font _activeFont20;
        private Font _activeFont17;
        private Font _activeFont15;
        private Font _activeFont11;


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
            _activeFont20 = new Font(active, 20f, FontStyle.Bold);
            _activeFont17 = new Font(active, 17f, FontStyle.Bold);
            _activeFont15 = new Font(active, 15f, FontStyle.Bold);
        }

        private IntPtr _targetHwnd = IntPtr.Zero;

        // ── stały kolor tekstu labelki/bonusu (nie zmienia się z InSimColor) ──
        private static readonly Color LabelTextColor = Color.White;

        // ── dynamiczny kolor (InSimColor1..5 wg kąta driftu) ────
        private Color _accentColor = Color.FromArgb(255, 235, 30);
        private Color _accentColorBack = Color.FromArgb(225, 205, 0);
        public void UpdateAccentColor(Color c) {
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

        // ── aktywność (drift/speeding) + countdown combo ────
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

        // ── bonus popup (przełożenie itp.) ───────────────────
        private string _bonusText = "";
        private DateTime _bonusStart = DateTime.MinValue;
        private const double BonusDurationSec = 2.0;
        private const double BonusSlideMs = 220;   // czas wjazdu/wyjazdu

        // ── kąt driftu / strzałki ─────────────────────────────
        private double _driftAngle = 0;
        private bool _isDrifting = false;
        private bool _sideRight = true;

        // ── płynna animacja rozmiaru ramek (label/run box, bonus box) ──
        private bool _hudSizeInit = false;
        private float _dispLabelBoxW = 0, _dispRunBoxW = 0, _dispBarH = 0;
        private const float SizeSmoothFactor = 0.45f; // wyższe = szybsza reakcja

        private bool _bonusSizeInit = false;
        private float _dispBonusBoxW = 0, _dispBonusBoxH = 0;

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

        public void UpdateScore(long total) => _targetScore = total;
        public void UpdateRun(long run) => _targetRun = run;
        public void UpdateLapScore(long lapScore) => _targetLapScore = lapScore;
        public void UpdateBestLapScore(long bestLapScore) => _bestLapScore = bestLapScore;

        public void UpdateLapContextLabel(string label) => _lapContextLabel = label ?? "";
        public void SetLapBoxVisible(bool visible)
        {
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

        /// <summary>Wywoływane co klatkę telemetrii: czy TERAZ trwa drift/szybka jazda.</summary>
        public void SetActive(bool active) => _isActiveNow = active;

        public void ShowBonus(string bonusText)
        {
            if (string.IsNullOrWhiteSpace(bonusText)) return;
            _bonusText = bonusText;
            _bonusStart = DateTime.Now;
            _bonusSizeInit = false; // nowy tekst = rozmiar ma się ustawić od razu, bez "doganiania" starego
        }

        public void SetDriftAngle(double angle, bool drifting, bool sideRight)
        {
            _driftAngle = angle;
            _isDrifting = drifting;
            _sideRight = sideRight;
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

            _displayedScore += (_targetScore - _displayedScore) * 0.18;
            if (Math.Abs(_targetScore - _displayedScore) < 1) _displayedScore = _targetScore;

            _displayedRun += (_targetRun - _displayedRun) * 0.25;
            if (Math.Abs(_targetRun - _displayedRun) < 1) _displayedRun = _targetRun;

            _displayedLapScore += (_targetLapScore - _displayedLapScore) * 0.2;   // ← NOWE
            if (Math.Abs(_targetLapScore - _displayedLapScore) < 1) _displayedLapScore = _targetLapScore;

            _displayedAngle += (_driftAngle - _displayedAngle) * 0.25;
            if (Math.Abs(_driftAngle - _displayedAngle) < 0.1) _displayedAngle = _driftAngle;

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

                DrawIdleScore(g, centerX);
                DrawActiveHud(g, centerX, out float barX, out float barY, out float barW, out float barH);
                DrawBonusBox(g, centerX, barY, barH);
                DrawLapScoreBox(g);   // ← NOWE
            }

            SetBitmap(_buffer);
        }

        // ── góra: sam total score, znika w lewo z fade gdy aktywny HUD wjeżdża ──
        private void DrawIdleScore(Graphics g, float centerX)
        {
            double alpha = 0.75 - _activeBlend;
            if (alpha <= 0.01) return;

            float slideX = (float)(-_activeBlend * 160);
            if (_isActiveNow) { slideX = (float)(-_activeBlend * 160); } else { slideX = (float)-(-_activeBlend * 160); }



            string text = ((long)Math.Round(_displayedScore)).ToString("N0");
            var font = _idleFont32;
            var size = g.MeasureString(text, font);
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
            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW, barH), 6))
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
                   barX - angleW - 2 + ((float)angleOffsetX/2),
                   barY + (barH / 2) - (angleH / 2),
                   WithAlpha(_accentColor, alpha * angleAlpha),
                   WithAlpha(_accentColorBack, alpha * angleAlpha),
                   outline: false);
            }
            DrawOutlinedText(g, labelText, labelFont, barX + padH, barY + padV ,
                WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);

            DrawOutlinedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV ,
                WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);

            if (_isDrifting)
                DrawAngleArrows(g, startX, 5, scoreSize.Width, scoreSize.Height, alpha);

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
        private void DrawBonusBox(Graphics g, float centerX, float labelBarY, float labelBarH)
        {
            double alphaset = 1.0;
            double alphacut = 0.5;
            if (string.IsNullOrEmpty(_bonusText)) return;

            double elapsed = (DateTime.Now - _bonusStart).TotalSeconds;
            if (elapsed < 0 || elapsed > BonusDurationSec) return;

            double alpha, offsetX;
            double slideInSec = BonusSlideMs / 1000.0;
            double slideOutStart = BonusDurationSec - slideInSec;

            if (elapsed < slideInSec)
            {
                double bt = elapsed / slideInSec;
                double eased = alphaset - Math.Pow(1 - bt, 3);
                alpha = eased;
                offsetX = (alphaset - eased) * 140; // wjazd z prawej
            }
            else if (elapsed > slideOutStart)
            {
                double bt = (elapsed - slideOutStart) / slideInSec;
                double eased = Math.Pow(bt, 2);
                alpha = alphaset - eased;
                offsetX = -eased * 140; // wyjazd w lewo
            }
            else
            {
                alpha = alphaset;
                offsetX = 0;
            }

            if (alpha <= 0.01) return;

            var font = _activeFont15;
            var size = g.MeasureString(_bonusText, font);

            float padH = 7, padV = 4;
            float targetBoxW = size.Width + padH * 2;
            float targetBoxH = size.Height + (padV * 2) - 1;

            if (!_bonusSizeInit)
            {
                _dispBonusBoxW = targetBoxW;
                _dispBonusBoxH = targetBoxH;
                _bonusSizeInit = true;
            }
            else
            {
                _dispBonusBoxW += (targetBoxW - _dispBonusBoxW) * SizeSmoothFactor;
                _dispBonusBoxH += (targetBoxH - _dispBonusBoxH) * SizeSmoothFactor;
            }

            float boxW = _dispBonusBoxW;
            float boxH = _dispBonusBoxH;
            float boxX = centerX - boxW / 2f + (float)offsetX;
            float boxY = labelBarY + labelBarH + 8;
            //DrawRevCutGlow(g, new RectangleF(boxX, boxY, boxW, boxH), alpha);
            using (var path = RoundedRect(new RectangleF(boxX, boxY, boxW, boxH), 6))
            using (var bgBrush = new SolidBrush(WithAlpha(_accentColor, alpha - alphacut)))
                g.FillPath(bgBrush, path);

            DrawOutlinedText(g, _bonusText, font, boxX + padH, boxY + padV,
                WithAlpha(LabelTextColor, alpha), WithAlpha(Color.Black, alpha), outline: false);
        }


        private void DrawLapScoreBox(Graphics g)
        {
            if (_lapBoxBlend <= 0.01) return;

            double alpha = _lapBoxBlend;
            float slideX = (float)((1.0 - _lapBoxBlend) * -30);

            var titleFont = _activeFont15;
            var valueFont = _activeFont24;
            var contextFont = _activeFont11;   // ← NOWE

            string lapLabel = Localization.T("hud.lapscore");        // ← zamiast "LAP SCORE"
            string lapValue = ((long)Math.Round(_displayedLapScore)).ToString("N0");
            string bestLabel = Localization.T("hud.bestlapscore");    // ← zamiast "BEST LAP SCORE"
            string bestValue = _bestLapScore.ToString("N0");
            string contextLabel = _lapContextLabel;

            var lapLabelSize = g.MeasureString(lapLabel, titleFont);
            var lapValueSize = g.MeasureString(lapValue, valueFont);
            var bestLabelSize = g.MeasureString(bestLabel, titleFont);
            var bestValueSize = g.MeasureString(bestValue, valueFont);
            var contextSize = string.IsNullOrEmpty(contextLabel)
                ? SizeF.Empty : g.MeasureString(contextLabel, contextFont);   // ← NOWE

            float boxW = Math.Max(
                Math.Max(lapLabelSize.Width, lapValueSize.Width),
                Math.Max(Math.Max(bestLabelSize.Width, bestValueSize.Width), contextSize.Width)) + 28;

            float rowGap = 1;
            float contextExtra = contextSize.Height > 0 ? contextSize.Height + 8 : 0;   // ← NOWE

            float boxH = lapLabelSize.Height + lapValueSize.Height
                       + bestLabelSize.Height + bestValueSize.Height
                       + contextExtra
                       + rowGap * 3 + 20;

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

            DrawOutlinedText(g, bestLabel, titleFont, textX, y,
                WithAlpha(Color.White, alpha * 0.82), WithAlpha(Color.Black, alpha), outline: false);
            y += bestLabelSize.Height + rowGap;

            DrawOutlinedText(g, bestValue, valueFont, textX, y,
                WithAlpha(Color.FromArgb(255, 215, 0), alpha), WithAlpha(Color.Black, alpha), outline: false);
            y += bestValueSize.Height;

            if (!string.IsNullOrEmpty(contextLabel))   // ← NOWE
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
                _activeFont20?.Dispose();
                _activeFont17?.Dispose();
                _activeFont15?.Dispose();

                _fontCollection?.Dispose();
                _renderTimer?.Dispose();
                _trackTimer?.Dispose();
                _buffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}