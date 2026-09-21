using LFSDriftTools.InSim;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static LFSDriftTools.DriftEngine;
using static LFSDriftTools.IndicatorManager;
using System.Reflection;

namespace LFSDriftTools
{
    public class SpeedoTachoPreviewControl : Control
    {
        public Color BackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);
        public Color TextColor { get; set; } = Color.White;
        public Color IndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color TickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color RedlineColor { get; set; } = Color.Red;
        public float RedlineThickness { get; set; } = 4f;
        public bool UseMph { get; set; } = false;
        public bool ShowRpmDigits { get; set; } = true;
        public bool ShowUnit { get; set; } = true;
        public bool ShowMinorTicks { get; set; } = true;
        public bool ShowMajorTicks { get; set; } = true;
        public bool PlainDigits { get; set; } = false;

        public Image? SpeedoBackgroundImage { get; set; } = null;
        public float SpeedoBackgroundPanX { get; set; } = 0f;
        public float SpeedoBackgroundPanY { get; set; } = 0f;
        public float SpeedoBackgroundZoom { get; set; } = 1.0f;
        public float SpeedoBackgroundOpacity { get; set; } = 1.0f;

        private const float DemoCalibratedMaxRpm = 8000f;
        private const string DemoGear = "3";
        private const int DemoSpeedKmh = 118;
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

        private const float Cx = 140f, Cy = 118f, Radius = 102f;

        private static readonly System.Drawing.Text.PrivateFontCollection _fontCollection = new();
        private static readonly FontFamily? _activeFontFamily = LoadActiveFontFamily();

        private static FontFamily? LoadActiveFontFamily()
        {
            try
            {
                string fontPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Fonts", "active.otf");
                if (File.Exists(fontPath))
                {
                    _fontCollection.AddFontFile(fontPath);
                    return _fontCollection.Families[0];
                }
            }
            catch { }
            return null;
        }

        private static Font MakeFont(float size) =>
            _activeFontFamily != null ? new Font(_activeFontFamily, size, FontStyle.Bold) : new Font("Segoe UI", size, FontStyle.Bold);

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
            catch { }
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

        private readonly Dictionary<float, Font> _fontCache = new();

        private Font GetFont(float size)
        {
            if (!_fontCache.TryGetValue(size, out var font))
            {
                font = MakeFont(size);
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
                _tachoCycleTimer.Dispose();
                DisposeFonts();
            }
            base.Dispose(disposing);
        }

        private static float ComputeGaugeMaxRpm(float calibratedMaxRpm)
        {
            float withMargin = calibratedMaxRpm * 1.15f;
            float rounded = (float)(Math.Ceiling(withMargin / 1000.0) * 1000.0);
            return Math.Max(rounded, calibratedMaxRpm + 1000f);
        }

        private Color GetTachoStateColor()
        {
            if (DemoRevCutActive || DemoRpm >= DemoCalibratedMaxRpm) return RedlineColor;

            float lower = DemoCalibratedMaxRpm - 1000;
            float upper = DemoCalibratedMaxRpm - 100;
            if (DemoRpm >= lower && DemoRpm < upper)
                return Color.FromArgb(255, 150, 230, 170);

            return IndicatorColor;
        }

        private static Color WithAlpha(Color c, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            return Color.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);
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

        private static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var shadowBrush = new SolidBrush(shadow))
                g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
            using (var fillBrush = new SolidBrush(fill))
                g.DrawString(text, font, fillBrush, x, y);
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

            const float cx = Cx, cy = Cy, radius = Radius;
            const float startAngle = 135f, sweepAngle = 270f;

            float gaugeMaxRpm = ComputeGaugeMaxRpm(DemoCalibratedMaxRpm);
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = GetTachoStateColor();

            var dialRect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            using (var bgBrush = new SolidBrush(BackgroundColor))
                g.FillEllipse(bgBrush, dialRect);

            DrawSpeedoBackgroundImage(g, dialRect);

            if (DemoCalibratedMaxRpm > 0 && DemoCalibratedMaxRpm < gaugeMaxRpm)
            {
                float redlineStartFrac = DemoCalibratedMaxRpm / gaugeMaxRpm;
                float redlineStartAngle = startAngle + redlineStartFrac * sweepAngle;
                float redlineSweep = sweepAngle - redlineStartFrac * sweepAngle;

                float redlineWidth = Math.Max(1f, RedlineThickness);
                float redlineRadius = radius - redlineWidth / 2f;
                var redlineRect = new RectangleF(cx - redlineRadius, cy - redlineRadius, redlineRadius * 2, redlineRadius * 2);

                using var redlinePen = new Pen(WithAlpha(RedlineColor, 0.9), redlineWidth)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(redlinePen, redlineRect, redlineStartAngle, redlineSweep);
            }

            Font tickFont = GetFont(12f);
            for (int i = 0; i <= majorTicks; i++)
            {
                float frac = (float)i / majorTicks;
                float angleDeg = startAngle + frac * sweepAngle;
                double angleRad = angleDeg * Math.PI / 180.0;

                bool inRedline = DemoCalibratedMaxRpm > 0 && (i * 1000f) >= DemoCalibratedMaxRpm;
                Color tickColor = inRedline ? RedlineColor : TickColor;

                float outerR = radius;
                float innerR = radius - 14;

                if (ShowMajorTicks)
                {
                    float x1 = cx + (float)Math.Cos(angleRad) * outerR;
                    float y1 = cy + (float)Math.Sin(angleRad) * outerR;
                    float x2 = cx + (float)Math.Cos(angleRad) * innerR;
                    float y2 = cy + (float)Math.Sin(angleRad) * innerR;

                    using (var shadowPen = new Pen(Color.FromArgb(150, 0, 0, 0), 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(shadowPen, x1 + 1.5f, y1 + 1.5f, x2 + 1.5f, y2 + 1.5f);

                    using (var tickPen = new Pen(tickColor, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(tickPen, x1, y1, x2, y2);
                }

                if (ShowRpmDigits)
                {
                    string label = i.ToString();
                    var labelSize = g.MeasureString(label, tickFont);
                    float labelR = innerR - 15;
                    float lx = cx + (float)Math.Cos(angleRad) * labelR - labelSize.Width / 2f;
                    float ly = cy + (float)Math.Sin(angleRad) * labelR - labelSize.Height / 2f;
                    DrawShadowedText(g, label, tickFont, lx, ly,
                        WithAlpha(TextColor, (double)TextColor.A / 255.0 * 0.85), Color.FromArgb(180, 0, 0, 0));
                }

                if (i < majorTicks && ShowMinorTicks)
                {
                    float midFrac = (i + 0.5f) / majorTicks;
                    float midAngleDeg = startAngle + midFrac * sweepAngle;
                    double midAngleRad = midAngleDeg * Math.PI / 180.0;
                    float mInnerR = radius - 8;
                    float mx1 = cx + (float)Math.Cos(midAngleRad) * outerR;
                    float my1 = cy + (float)Math.Sin(midAngleRad) * outerR;
                    float mx2 = cx + (float)Math.Cos(midAngleRad) * mInnerR;
                    float my2 = cy + (float)Math.Sin(midAngleRad) * mInnerR;

                    using (var shadowPen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.4f))
                        g.DrawLine(shadowPen, mx1 + 1.2f, my1 + 1.2f, mx2 + 1.2f, my2 + 1.2f);

                    using var minorPen = new Pen(WithAlpha(TickColor, 0.65), 1.4f);
                    g.DrawLine(minorPen, mx1, my1, mx2, my2);
                }
            }

            float rpmFrac = Math.Clamp(DemoRpm / gaugeMaxRpm, 0f, 1f);
            float needleAngleDeg = startAngle + rpmFrac * sweepAngle;
            double needleAngleRad = needleAngleDeg * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            using (var needleShadowPen = new Pen(Color.FromArgb(190, 0, 0, 0), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(needleShadowPen, cx + 1.5f, cy + 1.5f, nx + 1.5f, ny + 1.5f);

            using (var needlePen = new Pen(stateColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(needlePen, cx, cy, nx, ny);

            const float gearRadius = 32f;
            Color hubColor = DemoRevCutActive
                ? WithAlpha(RedlineColor, BackgroundColor.A / 255.0)
                : BackgroundColor;
            using (var hubBrush = new SolidBrush(hubColor))
                g.FillEllipse(hubBrush, cx - gearRadius + 3, cy - gearRadius + 3,
                    (gearRadius - 3) * 2, (gearRadius - 3) * 2);

            Font gearFont = GetFont(26f);
            {
                var gearSize = g.MeasureString(DemoGear, gearFont);
                DrawShadowedText(g, DemoGear, gearFont, (cx - 2) - gearSize.Width / 2f, cy - gearSize.Height / 2f,
                    TextColor, Color.FromArgb(200, 0, 0, 0));
            }

            Font speedFont = GetFont(40f);
            Font unitFont = GetFont(11f);

            double displaySpeed = UseMph ? DemoSpeedKmh * KmhToMph : DemoSpeedKmh;
            string speedText = PlainDigits
                ? ((int)Math.Round(displaySpeed)).ToString()
                : ((int)Math.Round(displaySpeed)).ToString("D3");
            var speedSize = g.MeasureString(speedText, speedFont);
            float speedX = cx - speedSize.Width / 2f;
            float speedY = cy + gearRadius + 10;

            DrawShadowedText(g, speedText, speedFont, speedX, speedY,
                TextColor, Color.FromArgb(200, 0, 0, 0));

            if (ShowUnit)
            {
                string unitText = UseMph ? "MPH" : Localization.T("speedometer.unit").ToUpperInvariant();
                var unitSize = g.MeasureString(unitText, unitFont);
                DrawShadowedText(g, unitText, unitFont,
                    cx + speedSize.Width / 2.5f - unitSize.Width, speedY,
                    WithAlpha(TextColor, (double)TextColor.A / 255.0 * 0.8), Color.FromArgb(180, 0, 0, 0));
            }

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

        private static readonly System.Drawing.Text.PrivateFontCollection _fontCollection = new();
        private static readonly FontFamily? _activeFontFamily = LoadActiveFontFamily();

        private static FontFamily? LoadActiveFontFamily()
        {
            try
            {
                string fontPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Fonts", "active.otf");
                if (File.Exists(fontPath))
                {
                    _fontCollection.AddFontFile(fontPath);
                    return _fontCollection.Families[0];
                }
            }
            catch { }
            return null;
        }

        private static Font MakeFont(float size) =>
            _activeFontFamily != null ? new Font(_activeFontFamily, size, FontStyle.Bold) : new Font("Segoe UI", size, FontStyle.Bold);

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
                font = MakeFont(size);
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

        private static Color Darken(Color c, double amount) => Color.FromArgb(
            c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

        private static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var shadowBrush = new SolidBrush(shadow))
                g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
            using (var fillBrush = new SolidBrush(fill))
                g.DrawString(text, font, fillBrush, x, y);
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
            DrawShadowedText(g, scoreText, scoreFont, scoreX, 5, accent, accentBack);

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

            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            using (var labelBrush = new SolidBrush(accent))
                g.FillPath(labelBrush, labelPath);

            using (var runPath = RoundedRightRect(new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6))
            using (var runBrush = new SolidBrush(Darken(accent, 0.15)))
                g.FillPath(runBrush, runPath);

            DrawShadowedText(g, comboText, comboFont, barX + labelBoxW + runBoxW + 5,
                barY + (barH / 2) - (comboSize.Height / 2), accent, accentBack);

            string angleText = ((long)Math.Round(angle)).ToString("N0") + "°";
            var angleSize = g.MeasureString(angleText, angleFont);
            DrawShadowedText(g, angleText, angleFont, barX - angleSize.Width - 2,
                barY + (barH / 2) - (angleSize.Height / 2), accent, accentBack);

            DrawShadowedText(g, labelText, labelFont, barX + padH, barY + padV, LabelTextColor, Color.Black);
            DrawShadowedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV, LabelTextColor, Color.Black);

            DrawAngleArrows(g, scoreX, 5, scoreSize.Width, scoreSize.Height, angle, accent);

            g.Restore(savedState);
        }
    }
}
