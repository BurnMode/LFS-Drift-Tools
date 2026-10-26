using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public class SpeedoTachoPreviewControl : Control
    {
        public int TachoType { get; set; } = SpeedoPresetData.TachoType1;
        public Color BackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);
        public Color TextColor { get; set; } = Color.White;
        public Color IndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color MidColor { get; set; } = Color.FromArgb(255, 255, 200, 0);
        public Color TickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color RedlineColor { get; set; } = Color.Red;
        public Color FuelColor { get; set; } = Color.FromArgb(128, 0, 187, 250);
        public Color TurboColor { get; set; } = Color.FromArgb(128, 255, 35, 34);
        public float RedlineThickness { get; set; } = 4f;
        public bool UseMph { get; set; } = false;
        public bool ShowRpmDigits { get; set; } = true;
        public bool ShowUnit { get; set; } = true;
        public bool ShowMinorTicks { get; set; } = true;
        public bool ShowMajorTicks { get; set; } = true;
        public bool PlainDigits { get; set; } = false;
        public bool ShowTelltales { get; set; } = true;
        public bool CenterUnit { get; set; } = true;

        public Image? SpeedoBackgroundImage { get; set; } = null;
        public float SpeedoBackgroundPanX { get; set; } = 0f;
        public float SpeedoBackgroundPanY { get; set; } = 0f;
        public float SpeedoBackgroundZoom { get; set; } = 1.0f;
        public float SpeedoBackgroundOpacity { get; set; } = 1.0f;

        private const float DemoCalibratedMaxRpm = 8000f;
        private const string DemoGear = "3";
        private const int DemoSpeedKmh = 118;
        private const float DemoFuel = 0.65f;
        private const float DemoTurbo = 0.55f;
        private const double KmhToMph = 0.621371;

        private static readonly (float rpm, bool revCut)[] _tachoTiers =
        {
            (5600f, false),
            (7600f, false),
            (8000f, false),
            (8000f, true),
        };
        private int _tachoTierIndex = 0;
        private float DemoRpm => _tachoTiers[_tachoTierIndex].rpm;
        private bool DemoRevCutActive => _tachoTiers[_tachoTierIndex].revCut;
        private readonly System.Windows.Forms.Timer _tachoCycleTimer;

        private const float Cx = TachoType1Renderer.CenterX, Cy = TachoType1Renderer.CenterY, Radius = TachoType1Renderer.Radius;

        private static readonly Image? _previewWallpaper = LoadPreviewWallpaper();

        private static Image? LoadPreviewWallpaper()
        {
            try
            {
                string path = Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Images", "speedo_preview_background.png");
                if (File.Exists(path))
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    using var temp = Image.FromStream(fs);
                    return new Bitmap(temp);
                }
            }
            catch (Exception ex) { ErrorLog.Write("Preview background load", ex); }
            return null;
        }

        public SpeedoTachoPreviewControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;

            _tachoCycleTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _tachoCycleTimer.Tick += (s, e) =>
            {
                if (!CanAnimate()) return;
                _tachoTierIndex = (_tachoTierIndex + 1) % _tachoTiers.Length;
                Invalidate();
            };
            _tachoCycleTimer.Start();
        }

        private bool CanAnimate() => Visible && IsHandleCreated && FindForm()?.WindowState != FormWindowState.Minimized;
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tachoCycleTimer.Dispose();
            }
            base.Dispose(disposing);
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

            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Clamp(SpeedoBackgroundOpacity, 0f, 1f) };
            attrs.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
            g.DrawImage(img, new Rectangle((int)drawX, (int)drawY, (int)drawW, (int)drawH),
                0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);

            g.Clip = oldClip;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_previewWallpaper != null)
            {
                float coverScale = Math.Max((float)Width / _previewWallpaper.Width, (float)Height / _previewWallpaper.Height);
                float wpW = _previewWallpaper.Width * coverScale;
                float wpH = _previewWallpaper.Height * coverScale;
                float wpX = (Width - wpW) / 2f;
                float wpY = (Height - wpH) / 2f;

                using var wallpaperPath = new GraphicsPath();
                wallpaperPath.AddRectangle(ClientRectangle);
                var oldWallpaperClip = g.Clip;
                g.SetClip(wallpaperPath, CombineMode.Intersect);
                g.DrawImage(_previewWallpaper, wpX, wpY, wpW, wpH);
                g.Clip = oldWallpaperClip;
            }

            const float contentSize = Radius * 2f;
            const float previewMargin = 25f;
            float scale = Math.Max(0.01f, Math.Min(Width, Height) - previewMargin * 2f) / contentSize * 1.1f;
            GraphicsState savedState = g.Save();
            g.TranslateTransform(
                (Width - contentSize * scale) / 2f - (Cx - Radius) * scale,
                (Height - contentSize * scale) / 2f - (Cy - Radius) * scale);
            g.ScaleTransform(scale, scale);

            if (TachoType == SpeedoPresetData.TachoType2)
            {
                double demoSpeed = UseMph ? DemoSpeedKmh * KmhToMph : DemoSpeedKmh;
                var state = new TachoType2State
                {
                    Rpm = DemoRpm,
                    CalibratedMaxRpm = DemoCalibratedMaxRpm,
                    RevCut = DemoRevCutActive,
                    GearText = DemoGear,
                    SpeedText = PlainDigits ? ((int)Math.Round(demoSpeed)).ToString() : ((int)Math.Round(demoSpeed)).ToString("D3"),
                    UnitText = UseMph ? "mph" : Localization.T("speedometer.unit"),
                    ShowUnit = ShowUnit,
                    ShowMarkers = ShowMajorTicks,
                    Fuel = DemoFuel,
                    TurboFraction = DemoTurbo,
                    HighBeam = true,
                    ShowTelltales = ShowTelltales,
                    TextColor = TextColor,
                    StartColor = IndicatorColor,
                    MidColor = MidColor,
                    TickColor = TickColor,
                    RedlineColor = RedlineColor,
                    BackgroundColor = BackgroundColor,
                    FuelColor = FuelColor,
                    TurboColor = TurboColor,
                };
                TachoType2Renderer.Draw(g, state, DrawSpeedoBackgroundImage);
                g.Restore(savedState);
                return;
            }

            double displaySpeed = UseMph ? DemoSpeedKmh * KmhToMph : DemoSpeedKmh;
            var type1 = new TachoType1State
            {
                Rpm = DemoRpm,
                CalibratedMaxRpm = DemoCalibratedMaxRpm,
                RevCut = DemoRevCutActive,
                GearText = DemoGear,
                SpeedText = PlainDigits ? ((int)Math.Round(displaySpeed)).ToString() : ((int)Math.Round(displaySpeed)).ToString("D3"),
                UnitText = UseMph ? "MPH" : Localization.T("speedometer.unit").ToUpperInvariant(),
                ShowUnit = ShowUnit,
                CenterUnit = CenterUnit,
                ShowRpmDigits = ShowRpmDigits,
                ShowMajorTicks = ShowMajorTicks,
                ShowMinorTicks = ShowMinorTicks,
                ShowTelltales = ShowTelltales,
                Telltales = new TelltaleFlags { HighBeam = true },
                RedlineThickness = RedlineThickness,
                TextColor = TextColor,
                IndicatorColor = IndicatorColor,
                TickColor = TickColor,
                RedlineColor = RedlineColor,
                BackgroundColor = BackgroundColor,
            };
            TachoType1Renderer.Draw(g, type1, DrawSpeedoBackgroundImage);

            g.Restore(savedState);
        }
    }

    public class ForzaHudPreviewControl : Control
    {

        public Color IdleColor { get; set; } = Color.White;
        public Color Color1 { get; set; } = Color.FromArgb(255, 255, 243, 0);
        public Color Color2 { get; set; } = Color.FromArgb(255, 255, 147, 0);
        public Color Color3 { get; set; } = Color.FromArgb(255, 255, 74, 0);
        public Color Color4 { get; set; } = Color.FromArgb(255, 255, 0, 0);
        public Color Color5 { get; set; } = Color.FromArgb(255, 255, 0, 118);

        public Color Color6 { get; set; } = Color.FromArgb(255, 211, 0, 255);

        private static readonly Color LabelTextColor = Color.White;

        private readonly (string? labelKey, long score, long run, double combo, double angle)[] _tiers =
        {
            (null, 12480, 0, 1.0, 0),
            ("drift.label.generic", 12480, 320, 1.0, 18),
            ("drift.label.angle_good", 13950, 860, 2.0, 32),
            ("drift.label.angle_high", 16200, 1850, 3.0, 58),
            ("drift.label.angle_extreme", 19800, 5200, 4.0, 78),
            ("drift.label.angle_ultraextreme", 24500, 8600, 5.0, 95),
            ("drift.label.angle_backward", 15600, 1200, 2.5, 110),
        };

        private int _tierIndex = 0;
        private readonly System.Windows.Forms.Timer _cycleTimer;

        private const float DesignWidth = 620f, DesignHeight = 150f, CenterX = DesignWidth / 2f;

        public ForzaHudPreviewControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;

            _cycleTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _cycleTimer.Tick += (s, e) =>
            {
                if (!CanAnimate()) return;
                _tierIndex = (_tierIndex + 1) % _tiers.Length;
                Invalidate();
            };
            _cycleTimer.Start();
        }

        private readonly Dictionary<float, Font> _fontCache = new();

        private Font GetFont(float size)
        {
            if (!_fontCache.TryGetValue(size, out var font))
            {
                font = FontLibrary.CreateActiveFont(size, FontStyle.Bold);
                _fontCache[size] = font;
            }
            return font;
        }

        private void DisposeFonts()
        {
            foreach (var font in _fontCache.Values) font.Dispose();
            _fontCache.Clear();
        }

        private bool CanAnimate() => Visible && IsHandleCreated && FindForm()?.WindowState != FormWindowState.Minimized;
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cycleTimer.Dispose();
                DisposeFonts();
            }
            base.Dispose(disposing);
        }

        private Color AccentFor(int tierIndex) => tierIndex switch
        {
            0 => IdleColor,
            1 => Color1,
            2 => Color2,
            3 => Color3,
            4 => Color4,
            5 => Color5,
            _ => Color6,
        };

        private void DrawAngleArrows(Graphics g, float scoreX, float scoreY, float scoreW, float scoreH, double angle, Color accent)
        {
            int count = angle > 90 ? 6 : angle > 65 ? 5 : angle > 55 ? 4 : angle > 45 ? 3 : angle > 25 ? 2 : angle > 15 ? 1 : 0;
            if (count == 0) return;

            float chevW = 14, chevH = 16, spacing = 4;
            float midY = scoreY + scoreH / 2f;
            float startX = scoreX + scoreW + 18;

            using var pen = new Pen(accent, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            for (int i = 0; i < count; i++)
            {
                float px = startX + i * (chevW + spacing);
                g.DrawLines(pen, new[]
                {
                    new PointF(px, midY - chevH / 2),
                    new PointF(px + chevW / 2, midY),
                    new PointF(px, midY + chevH / 2)
                });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float scale = Math.Min(Width / DesignWidth, Height / DesignHeight) * 1.1f;
            GraphicsState savedState = g.Save();
            g.TranslateTransform(
                (Width - DesignWidth * scale) / 2f,
                (Height - DesignHeight * scale) / 2f);
            g.ScaleTransform(scale, scale);

            var (labelKey, score, run, combo, angle) = _tiers[_tierIndex];
            Color accent = AccentFor(_tierIndex);
            Color accentBack = Color.FromArgb(accent.A, (int)(accent.R * 0.35), (int)(accent.G * 0.35), (int)(accent.B * 0.35));

            Font scoreFont = GetFont(32f);
            string scoreText = score.ToString("N0");
            var scoreSize = g.MeasureString(scoreText, scoreFont);
            float scoreX = CenterX - scoreSize.Width / 2f;
            DrawingHelpers.DrawShadowedText(g, scoreText, scoreFont, scoreX, 5, accent, accentBack);

            if (labelKey == null)
            {

                g.Restore(savedState);
                return;
            }

            Font comboFont = GetFont(24f);
            Font labelFont = GetFont(22f);
            Font runFont = GetFont(22f);
            Font angleFont = GetFont(24f);

            string comboText = $"x{combo:0.0}";
            string labelText = Localization.T(labelKey);
            string runText = run.ToString("N0");

            var labelSize = g.MeasureString(labelText, labelFont);
            var runSize = g.MeasureString(runText, runFont);
            var comboSize = g.MeasureString(comboText, comboFont);

            float padH = 7, padV = 4;
            float labelBoxW = labelSize.Width + padH * 2;
            float runBoxW = runSize.Width + padH * 2;
            float barH = Math.Max(labelSize.Height, runSize.Height) + padV * 2 - 1;
            float barW = labelBoxW + runBoxW;
            float barX = CenterX - barW / 2f;
            float barY = 68;

            using (var labelPath = DrawingHelpers.RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            using (var labelBrush = new SolidBrush(accent))
                g.FillPath(labelBrush, labelPath);

            using (var runPath = DrawingHelpers.RoundedRightRect(new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6))
            using (var runBrush = new SolidBrush(DrawingHelpers.Darken(accent, 0.15)))
                g.FillPath(runBrush, runPath);

            DrawingHelpers.DrawShadowedText(g, comboText, comboFont, barX + labelBoxW + runBoxW + 5,
                barY + (barH / 2) - (comboSize.Height / 2), accent, accentBack);

            string angleText = ((long)Math.Round(angle)).ToString("N0") + "°";
            var angleSize = g.MeasureString(angleText, angleFont);
            DrawingHelpers.DrawShadowedText(g, angleText, angleFont, barX - angleSize.Width - 2,
                barY + (barH / 2) - (angleSize.Height / 2), accent, accentBack);

            DrawingHelpers.DrawShadowedText(g, labelText, labelFont, barX + padH, barY + padV, LabelTextColor, Color.Black);
            DrawingHelpers.DrawShadowedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV, LabelTextColor, Color.Black);

            DrawAngleArrows(g, scoreX, 5, scoreSize.Width, scoreSize.Height, angle, accent);

            g.Restore(savedState);
        }
    }
}
