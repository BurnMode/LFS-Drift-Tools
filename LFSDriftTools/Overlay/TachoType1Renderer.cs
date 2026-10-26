using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LFSDriftTools
{
    public sealed class TachoType1State
    {
        public float Rpm;
        public float CalibratedMaxRpm;
        public bool RevCut;
        public double GlowIntensity;
        public string GearText = "";
        public string SpeedText = "";
        public string UnitText = "";
        public bool ShowUnit = true;
        public bool CenterUnit = true;
        public bool ShowRpmDigits = true;
        public bool ShowMajorTicks = true;
        public bool ShowMinorTicks = true;
        public bool ShowTelltales = true;
        public TelltaleFlags Telltales;
        public float RedlineThickness = 4f;
        public object? LayerKey;
        public Color TextColor = Color.White;
        public Color IndicatorColor = Color.FromArgb(255, 225, 225, 230);
        public Color TickColor = Color.FromArgb(255, 215, 215, 218);
        public Color RedlineColor = Color.Red;
        public Color BackgroundColor = Color.FromArgb(50, 15, 15, 20);
    }

    public static class TachoType1Renderer
    {
        public const float CenterX = 140f;
        public const float CenterY = 118f;
        public const float Radius = 102f;

        private const float StartAngle = 135f;
        private const float SweepAngle = 270f;
        private const float GearRadius = 32f;
        private const int MaxMeasureCacheEntries = 256;

        public static RectangleF DialRect => new(CenterX - Radius, CenterY - Radius, Radius * 2, Radius * 2);

        private static readonly Font TickFont = FontLibrary.CreateActiveFont(12f, FontStyle.Bold);
        private static readonly Font GearFont = FontLibrary.CreateActiveFont(26f, FontStyle.Bold);
        private static readonly Font SpeedFont = FontLibrary.CreateActiveFont(40f, FontStyle.Bold);
        private static readonly Font UnitFont = FontLibrary.CreateActiveFont(11f, FontStyle.Bold);

        private static readonly Pen ScratchPen = new(Color.Black, 1f);
        private static readonly SolidBrush ScratchBrush = new(Color.Black);
        private static readonly Dictionary<(string, Font), SizeF> MeasureCache = new();

        private static readonly LayerCache StaticLayer = new(1);
        private static readonly LayerCache GearSprites = new(8);
        private static readonly LayerCache SpeedSprites = new(12);
        private static readonly LayerCache UnitSprites = new(2);
        private static readonly LayerCache GlowLayer = new(1);
        private const float GlowLayerMargin = 22f;

        public static float GaugeMaxRpm(float calibratedMaxRpm)
        {
            if (calibratedMaxRpm <= 0) return 10000f;

            float withMargin = calibratedMaxRpm * 1.15f;
            float rounded = (float)(Math.Ceiling(withMargin / 1000.0) * 1000.0);
            return Math.Max(rounded, calibratedMaxRpm + 1000f);
        }

        private static Color StateColor(TachoType1State s)
        {
            if (s.RevCut || (s.CalibratedMaxRpm > 0 && s.Rpm >= s.CalibratedMaxRpm))
                return s.RedlineColor;

            if (s.CalibratedMaxRpm > 0)
            {
                float lower = s.CalibratedMaxRpm - 1000;
                float upper = s.CalibratedMaxRpm - 100;
                if (s.Rpm >= lower && s.Rpm < upper)
                    return Color.FromArgb(255, 150, 230, 170);
            }

            return s.IndicatorColor;
        }

        private static Pen PenOf(Color color, float width, LineCap cap)
        {
            ScratchPen.Color = color;
            ScratchPen.Width = width;
            ScratchPen.StartCap = cap;
            ScratchPen.EndCap = cap;
            return ScratchPen;
        }

        private static SolidBrush BrushOf(Color color)
        {
            ScratchBrush.Color = color;
            return ScratchBrush;
        }

        private static SizeF MeasureText(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;

            var key = (text, font);
            if (MeasureCache.TryGetValue(key, out var size)) return size;

            if (MeasureCache.Count >= MaxMeasureCacheEntries) MeasureCache.Clear();

            size = g.MeasureString(text, font);
            MeasureCache[key] = size;
            return size;
        }

        private static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            g.DrawString(text, font, BrushOf(shadow), x + 1.5f, y + 1.5f);
            g.DrawString(text, font, BrushOf(fill), x, y);
        }

        private static void DrawText(Graphics g, LayerCache? sprites, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (sprites == null)
            {
                DrawShadowedText(g, text, font, x, y, fill, shadow);
                return;
            }

            var size = MeasureText(g, text, font);
            float pad = font.Size * 0.5f;
            var bounds = new RectangleF(x - pad, y - pad, size.Width + pad * 2f + 1.5f, size.Height + pad * 2f + 1.5f);
            var key = (text, font, fill.ToArgb(), shadow.ToArgb());
            sprites.Draw(g, bounds, key, lg => DrawShadowedText(lg, text, font, x, y, fill, shadow));
        }

        public static void Draw(Graphics g, TachoType1State s, Action<Graphics, RectangleF>? drawBackgroundImage)
        {
            const float cx = CenterX, cy = CenterY, radius = Radius;

            float gaugeMaxRpm = GaugeMaxRpm(s.CalibratedMaxRpm);
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = StateColor(s);
            var dialRect = DialRect;
            bool cached = s.LayerKey != null;

            if (cached)
            {
                var staticKey = (s.LayerKey, s.BackgroundColor.ToArgb(), s.CalibratedMaxRpm, gaugeMaxRpm, s.RedlineColor.ToArgb(), s.RedlineThickness,
                    s.TickColor.ToArgb(), s.TextColor.ToArgb(), s.ShowMajorTicks, s.ShowMinorTicks, s.ShowRpmDigits);
                StaticLayer.Draw(g, RectangleF.Inflate(dialRect, 4f, 4f), staticKey,
                    lg => DrawStaticLayer(lg, s, gaugeMaxRpm, majorTicks, dialRect, drawBackgroundImage));
            }
            else
            {
                DrawStaticLayer(g, s, gaugeMaxRpm, majorTicks, dialRect, drawBackgroundImage);
            }

            long glowStart = PerfProbe.Begin();
            DrawRevCutRingGlow(g, dialRect, s.GlowIntensity, s.RedlineColor);
            PerfProbe.End(PerfSection.OverlaySpeedoGlow, glowStart);

            if (s.ShowTelltales)
                TachoTelltales.DrawType1(g, s.Telltales, TachoType2Renderer.Type1TextFamily);

            float rpmFrac = Math.Clamp(s.Rpm / gaugeMaxRpm, 0f, 1f);
            double needleAngleRad = (StartAngle + rpmFrac * SweepAngle) * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            g.DrawLine(PenOf(Color.FromArgb(190, 0, 0, 0), 4f, LineCap.Round), cx + 1.5f, cy + 1.5f, nx + 1.5f, ny + 1.5f);
            g.DrawLine(PenOf(stateColor, 4f, LineCap.Round), cx, cy, nx, ny);

            Color hubColor = s.RevCut
                ? DrawingHelpers.WithAlpha(s.RedlineColor, s.BackgroundColor.A / 255.0)
                : s.BackgroundColor;
            g.FillEllipse(BrushOf(hubColor), cx - GearRadius + 3, cy - GearRadius + 3, (GearRadius - 3) * 2, (GearRadius - 3) * 2);

            var gearSize = MeasureText(g, s.GearText, GearFont);
            DrawText(g, cached ? GearSprites : null, s.GearText, GearFont, (cx - 2) - gearSize.Width / 2f, cy - gearSize.Height / 2f,
                s.TextColor, Color.FromArgb(200, 0, 0, 0));

            var speedSize = MeasureText(g, s.SpeedText, SpeedFont);
            float speedX = cx - speedSize.Width / 2f;
            float speedY = cy + GearRadius + 10;
            DrawText(g, cached ? SpeedSprites : null, s.SpeedText, SpeedFont, speedX, speedY,
                s.TextColor, Color.FromArgb(200, 0, 0, 0));

            if (s.ShowUnit)
            {
                var unitSize = MeasureText(g, s.UnitText, UnitFont);
                DrawText(g, cached ? UnitSprites : null, s.UnitText, UnitFont,
                    s.CenterUnit ? cx - unitSize.Width / 2f : cx + speedSize.Width / 2.5f - unitSize.Width, speedY,
                    DrawingHelpers.WithAlpha(s.TextColor, (double)s.TextColor.A / 255.0 * 0.8), Color.FromArgb(180, 0, 0, 0));
            }
        }

        private static void DrawStaticLayer(Graphics g, TachoType1State s, float gaugeMaxRpm, int majorTicks, RectangleF dialRect,
            Action<Graphics, RectangleF>? drawBackgroundImage)
        {
            const float cx = CenterX, cy = CenterY, radius = Radius;

            g.FillEllipse(BrushOf(s.BackgroundColor), dialRect);
            drawBackgroundImage?.Invoke(g, dialRect);

            if (s.CalibratedMaxRpm > 0 && s.CalibratedMaxRpm < gaugeMaxRpm)
            {
                float redlineStartFrac = s.CalibratedMaxRpm / gaugeMaxRpm;
                float redlineStartAngle = StartAngle + redlineStartFrac * SweepAngle;
                float redlineSweep = SweepAngle - redlineStartFrac * SweepAngle;

                float redlineWidth = Math.Max(1f, s.RedlineThickness);
                float redlineRadius = radius - redlineWidth / 2f;
                var redlineRect = new RectangleF(cx - redlineRadius, cy - redlineRadius, redlineRadius * 2, redlineRadius * 2);
                g.DrawArc(PenOf(DrawingHelpers.WithAlpha(s.RedlineColor, 0.9), redlineWidth, LineCap.Round), redlineRect, redlineStartAngle, redlineSweep);
            }

            for (int i = 0; i <= majorTicks; i++)
            {
                double angleRad = (StartAngle + (float)i / majorTicks * SweepAngle) * Math.PI / 180.0;

                bool inRedline = s.CalibratedMaxRpm > 0 && (i * 1000f) >= s.CalibratedMaxRpm;
                Color tickColor = inRedline ? s.RedlineColor : s.TickColor;

                float outerR = radius;
                float innerR = radius - 14;

                if (s.ShowMajorTicks)
                {
                    float x1 = cx + (float)Math.Cos(angleRad) * outerR;
                    float y1 = cy + (float)Math.Sin(angleRad) * outerR;
                    float x2 = cx + (float)Math.Cos(angleRad) * innerR;
                    float y2 = cy + (float)Math.Sin(angleRad) * innerR;

                    g.DrawLine(PenOf(Color.FromArgb(150, 0, 0, 0), 2.8f, LineCap.Round), x1 + 1.5f, y1 + 1.5f, x2 + 1.5f, y2 + 1.5f);
                    g.DrawLine(PenOf(tickColor, 2.8f, LineCap.Round), x1, y1, x2, y2);
                }

                if (s.ShowRpmDigits)
                {
                    string label = i.ToString();
                    var labelSize = MeasureText(g, label, TickFont);
                    float labelR = innerR - 15;
                    float lx = cx + (float)Math.Cos(angleRad) * labelR - labelSize.Width / 2f;
                    float ly = cy + (float)Math.Sin(angleRad) * labelR - labelSize.Height / 2f;
                    DrawShadowedText(g, label, TickFont, lx, ly,
                        DrawingHelpers.WithAlpha(s.TextColor, (double)s.TextColor.A / 255.0 * 0.85), Color.FromArgb(180, 0, 0, 0));
                }

                if (i < majorTicks && s.ShowMinorTicks)
                {
                    double midAngleRad = (StartAngle + (i + 0.5f) / majorTicks * SweepAngle) * Math.PI / 180.0;
                    float mInnerR = radius - 8;
                    float mx1 = cx + (float)Math.Cos(midAngleRad) * outerR;
                    float my1 = cy + (float)Math.Sin(midAngleRad) * outerR;
                    float mx2 = cx + (float)Math.Cos(midAngleRad) * mInnerR;
                    float my2 = cy + (float)Math.Sin(midAngleRad) * mInnerR;

                    g.DrawLine(PenOf(Color.FromArgb(120, 0, 0, 0), 1.4f, LineCap.Flat), mx1 + 1.2f, my1 + 1.2f, mx2 + 1.2f, my2 + 1.2f);
                    g.DrawLine(PenOf(DrawingHelpers.WithAlpha(s.TickColor, 0.65), 1.4f, LineCap.Flat), mx1, my1, mx2, my2);
                }
            }
        }

        private static void DrawRevCutRingGlow(Graphics g, RectangleF dialRect, double glowStrength, Color redlineColor)
        {
            if (glowStrength <= 0.01) return;

            var bounds = RectangleF.Inflate(dialRect, GlowLayerMargin, GlowLayerMargin);
            var key = (dialRect, redlineColor.ToArgb());
            if (GlowLayer.TryDraw(g, bounds, key, lg => DrawRingGlowLayers(lg, dialRect, 1.0, redlineColor), (float)glowStrength))
                return;

            DrawRingGlowLayers(g, dialRect, glowStrength, redlineColor);
        }

        private static void DrawRingGlowLayers(Graphics g, RectangleF dialRect, double glowStrength, Color redlineColor)
        {
            const float baseInflate = -1f;
            const int layers = 10;
            float spread = 20f;
            for (int i = layers; i >= 1; i--)
            {
                spread--;
                float grow = baseInflate + (spread / layers) * i;
                double layerAlpha = glowStrength * (1.0 - (double)i / (layers + 1)) * 0.50;
                g.DrawEllipse(PenOf(DrawingHelpers.WithAlpha(redlineColor, layerAlpha), 3f, LineCap.Flat), RectangleF.Inflate(dialRect, grow, grow));
            }
        }
    }
}
