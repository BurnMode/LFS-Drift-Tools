using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LFSDriftTools
{
    public sealed class TachoType2State
    {
        public float Rpm;
        public float CalibratedMaxRpm;
        public bool RevCut;
        public string GearText = "";
        public string SpeedText = "";
        public string UnitText = "";
        public bool ShowUnit = true;
        public bool ShowMarkers = true;
        public float Fuel = -1f;
        public float TurboFraction = -1f;
        public bool LeftSignal;
        public bool RightSignal;
        public bool HighBeam;
        public bool Handbrake;
        public bool TractionControl;
        public bool Abs;
        public bool ShowTelltales = true;
        public object? LayerKey;
        public Color FuelColor = Color.FromArgb(128, 0, 187, 250);
        public Color TurboColor = Color.FromArgb(128, 255, 35, 34);
        public Color TextColor = Color.White;
        public Color StartColor = Color.White;
        public Color MidColor = Color.FromArgb(255, 255, 200, 0);
        public Color TickColor = Color.White;
        public Color RedlineColor = Color.FromArgb(255, 219, 0, 7);
        public Color BackgroundColor = Color.FromArgb(60, 0, 0, 0);
    }

    public static class TachoType2Renderer
    {
        public const float CenterX = 140f;
        public const float CenterY = 118f;
        public const float Scale = 102f / 43.5f;

        private const float ArcStartDeg = 141.34f;
        private const float ArcSweepDeg = 257.32f;
        private const float BandRadius = 38.5f;
        private const float BandWidth = 10f;
        private const float OuterRingRadius = 41.5f;
        private const float OuterRingWidth = 4f;
        private const float InnerRadius = 33.5f;
        private const float GearCenterY = 33.5f;
        private const float GearRadius = 16f;
        private const float GaugeRingRadius = 14f;
        private const float GaugeRingWidth = 4f;
        private const float RedlineFraction = 0.8f;
        private const float CondensedSpeedScaleX = 0.5f;
        private const float GearDigitHeight = 13.2f;
        private const float SpeedDigitHeight = 38f;
        private const float SpeedDigitSpacingFactor = 0.5f;

        private static readonly FontFamily _textFamily = new FontFamily("Segoe UI");
        private static readonly FontFamily _type1Family = FontLibrary.Load(FontLibrary.ActiveFontFile) ?? _textFamily;

        public static FontFamily Type1TextFamily => _type1Family;
        private static readonly FontFamily? _speedFamily = FontLibrary.Load("tachotype2_speed.ttf");
        private static readonly FontFamily? _gearFamily = FontLibrary.Load("tachotype2_gear.ttf");

        private static float X(float u) => CenterX + (u - 50f) * Scale;
        private static float Y(float u) => CenterY + (u - 50f) * Scale;
        private static float S(float u) => u * Scale;

        private static RectangleF Circle(float cxU, float cyU, float rU) =>
            new(X(cxU) - S(rU), Y(cyU) - S(rU), S(rU) * 2f, S(rU) * 2f);

        public static RectangleF DialRect => Circle(50f, 50f, InnerRadius);

        public static float GaugeMaxRpm(float calibratedMaxRpm) =>
            calibratedMaxRpm > 0 ? calibratedMaxRpm / RedlineFraction : 10000f;

        private static readonly Pen _pen = new(Color.White, 1f);
        private static readonly SolidBrush _brush = new(Color.White);
        private static readonly Dictionary<FontFamily, float> _digitHeights = new();
        private static readonly TextPathCache _gearCache = new();
        private static readonly TextPathCache _speedCache = new();
        private static readonly TextPathCache _unitCache = new();

        private sealed class TextPathCache
        {
            public string Text = "";
            public FontFamily? Family;
            public float Size;
            public float Cx;
            public float Cy;
            public float ScaleX;
            public GraphicsPath? Path;
            public GraphicsPath? Shadow;
            public RectangleF Bounds;

            public bool Matches(string text, FontFamily family, float size, float cx, float cy, float scaleX) =>
                Path != null && Text == text && ReferenceEquals(Family, family) && Size == size && Cx == cx && Cy == cy && ScaleX == scaleX;

            public void Store(string text, FontFamily family, float size, float cx, float cy, float scaleX, GraphicsPath path, GraphicsPath? shadow)
            {
                Path?.Dispose();
                Shadow?.Dispose();
                Text = text;
                Family = family;
                Size = size;
                Cx = cx;
                Cy = cy;
                ScaleX = scaleX;
                Path = path;
                Shadow = shadow;
                var b = path.GetBounds();
                Bounds = shadow != null
                    ? RectangleF.FromLTRB(b.Left - 1f, b.Top - 1f, b.Right + 2.5f, b.Bottom + 2.5f)
                    : RectangleF.Inflate(b, 1f, 1f);
            }
        }

        private static Pen PenOf(Color color, float width)
        {
            _pen.Color = color;
            _pen.Width = width;
            return _pen;
        }

        private static SolidBrush BrushOf(Color color)
        {
            _brush.Color = color;
            return _brush;
        }

        public static void Draw(Graphics g, TachoType2State s, Action<Graphics, RectangleF>? drawBackgroundImage)
        {
            float gaugeMax = GaugeMaxRpm(s.CalibratedMaxRpm);
            float frac = Math.Clamp(s.Rpm / gaugeMax, 0f, 1f);
            Color rpmColor = s.RevCut ? s.RedlineColor : RpmColor(frac, s.StartColor, s.MidColor, s.RedlineColor);
            bool cached = s.LayerKey != null;
            var band = Circle(50f, 50f, BandRadius);

            if (cached)
            {
                var underKey = (s.LayerKey, s.BackgroundColor.ToArgb(), s.TickColor.ToArgb(), s.RedlineColor.ToArgb(), s.CalibratedMaxRpm);
                _underLayer.Draw(g, LayerBounds, underKey, lg => DrawUnderLayer(lg, s, drawBackgroundImage));
            }
            else
            {
                DrawUnderLayer(g, s, drawBackgroundImage);
            }

            if (frac > 0.001f)
                g.DrawArc(PenOf(WithAlpha(rpmColor, RpmBandAlpha), S(BandWidth)), band, ArcStartDeg, ArcSweepDeg * frac);

            if (cached)
            {
                var overKey = (s.TickColor.ToArgb(), gaugeMax, s.ShowMarkers);
                _overLayer.Draw(g, LayerBounds, overKey, lg => DrawOverLayer(lg, s, gaugeMax));
            }
            else
            {
                DrawOverLayer(g, s, gaugeMax);
            }

            g.FillEllipse(BrushOf(WithAlpha(rpmColor, 0.6)), Circle(50f, GearCenterY, GearRadius));

            var gaugeRing = Circle(50f, GearCenterY, GaugeRingRadius);
            if (s.Fuel >= 0)
                g.DrawArc(PenOf(s.FuelColor, S(GaugeRingWidth)), gaugeRing, 90f, 180f * Math.Clamp(s.Fuel, 0f, 1f));
            if (s.TurboFraction > 0f)
                g.DrawArc(PenOf(s.TurboColor, S(GaugeRingWidth)), gaugeRing, 90f, -180f * Math.Clamp(s.TurboFraction, 0f, 1f));

            DrawFittedCenteredText(g, s.GearText, _gearFamily ?? _textFamily, S(GearDigitHeight), X(50f), Y(GearCenterY), s.TextColor,
                cached ? _gearSprites : null);

            if (s.ShowUnit)
                DrawCenteredText(g, s.UnitText, _textFamily, S(4.2f), X(50f), Y(47.5f), 1f, WithAlpha(s.TextColor, 0.94),
                    cached ? _unitSprites : null);

            DrawFittedBaselineText(g, s.SpeedText, _speedFamily ?? _textFamily, S(SpeedDigitHeight),
                X(50f), Y(89.5f), _speedFamily != null ? 1f : CondensedSpeedScaleX, s.TextColor,
                cached ? _speedSprites : null);

            if (s.ShowTelltales)
                DrawIcons(g, s);
        }

        private static readonly LayerCache _underLayer = new(2);
        private static readonly LayerCache _overLayer = new(2);
        private static readonly LayerCache _gearSprites = new(8);
        private static readonly LayerCache _speedSprites = new(12);
        private static readonly LayerCache _unitSprites = new(2);

        private static RectangleF LayerBounds => Circle(50f, 50f, OuterRingRadius + OuterRingWidth);

        private static void DrawUnderLayer(Graphics g, TachoType2State s, Action<Graphics, RectangleF>? drawBackgroundImage)
        {
            g.FillEllipse(BrushOf(s.BackgroundColor), DialRect);
            drawBackgroundImage?.Invoke(g, DialRect);

            var band = Circle(50f, 50f, BandRadius);
            g.DrawArc(PenOf(WithAlpha(s.TickColor, 0.2), S(BandWidth)), band, ArcStartDeg, ArcSweepDeg);

            if (s.CalibratedMaxRpm > 0)
                g.DrawArc(PenOf(WithAlpha(s.RedlineColor, RpmBandAlpha), S(BandWidth)), band, ArcStartDeg + ArcSweepDeg * RedlineFraction, ArcSweepDeg * (1f - RedlineFraction));
        }

        private static void DrawOverLayer(Graphics g, TachoType2State s, float gaugeMax)
        {
            if (s.ShowMarkers)
            {
                var pen = PenOf(WithAlpha(s.TickColor, 0.55), Math.Max(1f, S(0.5f)));
                int count = (int)Math.Floor(gaugeMax / 1000f);
                for (int i = 0; i <= count; i++)
                {
                    double a = (ArcStartDeg + ArcSweepDeg * (i * 1000f / gaugeMax)) * Math.PI / 180.0;
                    float r1 = BandRadius - BandWidth / 2f, r2 = BandRadius + BandWidth / 2f;
                    g.DrawLine(pen,
                        X(50f + (float)Math.Cos(a) * r1), Y(50f + (float)Math.Sin(a) * r1),
                        X(50f + (float)Math.Cos(a) * r2), Y(50f + (float)Math.Sin(a) * r2));
                }
            }

            g.DrawArc(PenOf(WithAlpha(s.TickColor, 0.5), S(OuterRingWidth)), Circle(50f, 50f, OuterRingRadius), ArcStartDeg, ArcSweepDeg);
        }

        private static void DrawIcons(Graphics g, TachoType2State s)
        {
            if (s.LeftSignal)
                TachoTelltales.FillArrow(g, X(25f), Y(26f), S(8f), left: true);
            if (s.RightSignal)
                TachoTelltales.FillArrow(g, X(67f), Y(26f), S(8f), left: false);
            if (s.HighBeam)
                TachoTelltales.DrawHighBeam(g, X(71f), Y(44.5f), S(9f));
            if (s.Handbrake)
                TachoTelltales.DrawHandbrake(g, X(22.5f), Y(35.5f), S(9f));
            if (s.Abs)
                TachoTelltales.DrawText(g, "ABS", _textFamily, S(5.5f), X(21f), Y(46.5f), centered: false);
            if (s.TractionControl)
                TachoTelltales.DrawText(g, "TC", _textFamily, S(5.5f), X(21f), Y(52.5f), centered: false);
        }

        private static FontStyle StyleFor(FontFamily family) =>
            ReferenceEquals(family, _textFamily) && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;

        private static void DrawCenteredText(Graphics g, string text, FontFamily family, float emPx, float cx, float cy, float scaleX, Color color, LayerCache? sprites)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_unitCache.Matches(text, family, emPx, cx, cy, scaleX))
            {
                var path = new GraphicsPath();
                path.AddString(text, family, (int)StyleFor(family), emPx, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                using var m = new Matrix();
                m.Translate(cx, cy);
                m.Scale(scaleX, 1f);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                path.Transform(m);
                _unitCache.Store(text, family, emPx, cx, cy, scaleX, path, null);
            }
            FillText(g, _unitCache, color, sprites);
        }

        private const float ReferenceEmPx = 100f;

        private static float DigitHeight(FontFamily family)
        {
            if (_digitHeights.TryGetValue(family, out float cached)) return cached;
            using var reference = new GraphicsPath();
            reference.AddString("0", family, (int)StyleFor(family), ReferenceEmPx, PointF.Empty, StringFormat.GenericTypographic);
            float height = Math.Max(1f, reference.GetBounds().Height);
            _digitHeights[family] = height;
            return height;
        }

        private static GraphicsPath ShadowOf(GraphicsPath path)
        {
            var shadowPath = (GraphicsPath)path.Clone();
            using var sm = new Matrix();
            sm.Translate(1.5f, 1.5f);
            shadowPath.Transform(sm);
            return shadowPath;
        }

        private static void FillDirect(Graphics g, TextPathCache cache, Color color)
        {
            if (cache.Shadow != null)
                g.FillPath(BrushOf(Color.FromArgb(Math.Min(110, (int)color.A), 0, 0, 0)), cache.Shadow);
            g.FillPath(BrushOf(color), cache.Path!);
        }

        private static void FillText(Graphics g, TextPathCache cache, Color color, LayerCache? sprites)
        {
            if (sprites == null)
            {
                FillDirect(g, cache, color);
                return;
            }
            var key = (cache.Text, cache.Family, cache.Size, cache.ScaleX, cache.Shadow != null, color.ToArgb());
            sprites.Draw(g, cache.Bounds, key, lg => FillDirect(lg, cache, color));
        }

        private static void DrawFittedCenteredText(Graphics g, string text, FontFamily family, float digitHeightPx, float cx, float cy, Color color, LayerCache? sprites)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_gearCache.Matches(text, family, digitHeightPx, cx, cy, 1f))
            {
                var path = new GraphicsPath();
                path.AddString(text, family, (int)StyleFor(family), ReferenceEmPx, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                float k = digitHeightPx / DigitHeight(family);
                using var m = new Matrix();
                m.Translate(cx, cy);
                m.Scale(k, k);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                path.Transform(m);
                _gearCache.Store(text, family, digitHeightPx, cx, cy, 1f, path, ShadowOf(path));
            }
            FillText(g, _gearCache, color, sprites);
        }

        private static void DrawFittedBaselineText(Graphics g, string text, FontFamily family, float digitHeightPx, float cx, float baselineY, float scaleX, Color color, LayerCache? sprites)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_speedCache.Matches(text, family, digitHeightPx, cx, baselineY, scaleX))
            {
                var (cell, ascent) = DigitLayout(family);
                int style = (int)StyleFor(family);
                var path = new GraphicsPath();
                float total = cell * text.Length;
                for (int i = 0; i < text.Length; i++)
                {
                    using var glyph = new GraphicsPath();
                    glyph.AddString(text[i].ToString(), family, style, ReferenceEmPx, PointF.Empty, StringFormat.GenericTypographic);
                    var gb = glyph.GetBounds();
                    if (gb.Width <= 0f) continue;
                    float cellCenter = -total / 2f + (i + 0.5f) * cell;
                    using var place = new Matrix();
                    place.Translate(cellCenter - (gb.X + gb.Width / 2f), 0f);
                    glyph.Transform(place);
                    path.AddPath(glyph, false);
                }
                float k = digitHeightPx / DigitHeight(family);
                using var m = new Matrix();
                m.Translate(cx, baselineY);
                m.Scale(k * scaleX, k);
                m.Translate(0f, -ascent);
                path.Transform(m);
                _speedCache.Store(text, family, digitHeightPx, cx, baselineY, scaleX, path, ShadowOf(path));
            }
            FillText(g, _speedCache, color, sprites);
        }

        private static readonly Dictionary<FontFamily, (float Cell, float Ascent)> _digitLayouts = new();

        private static (float Cell, float Ascent) DigitLayout(FontFamily family)
        {
            if (_digitLayouts.TryGetValue(family, out var cached)) return cached;
            var fontStyle = StyleFor(family);
            float cell = 0f;
            var inks = new float[10];
            for (char c = '0'; c <= '9'; c++)
            {
                float single = InkWidth(family, fontStyle, c.ToString());
                float pair = InkWidth(family, fontStyle, new string(c, 2));
                inks[c - '0'] = single;
                cell = Math.Max(cell, Math.Max(pair - single, single));
            }
            Array.Sort(inks);
            float typicalInk = (inks[4] + inks[5]) / 2f;
            cell = Math.Max(inks[9], typicalInk + Math.Max(0f, cell - typicalInk) * SpeedDigitSpacingFactor);
            float ascent = ReferenceEmPx * family.GetCellAscent(fontStyle) / Math.Max(1, family.GetEmHeight(fontStyle));
            var layout = (Math.Max(1f, cell), ascent);
            _digitLayouts[family] = layout;
            return layout;
        }

        private static float InkWidth(FontFamily family, FontStyle fontStyle, string text)
        {
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)fontStyle, ReferenceEmPx, PointF.Empty, StringFormat.GenericTypographic);
            return path.GetBounds().Width;
        }

        private const double RpmColorFullAtCutFraction = 0.95;
        private const double RpmMidStop = 0.5;
        private const double RpmBandAlpha = 0.7;

        private static Color RpmColor(float frac, Color start, Color mid, Color redline)
        {
            double fullFrac = RedlineFraction * RpmColorFullAtCutFraction;
            double p = Math.Clamp(frac / fullFrac, 0.0, 1.0);
            return p <= RpmMidStop
                ? BlendHsl(start, mid, p / RpmMidStop)
                : BlendHsl(mid, redline, (p - RpmMidStop) / (1.0 - RpmMidStop));
        }

        private static Color BlendHsl(Color a, Color b, double t)
        {
            var (hueA, satA, lightA) = ToHsl(a);
            var (hueB, satB, lightB) = ToHsl(b);
            if (IsAchromatic(satA, lightA)) hueA = hueB;
            if (IsAchromatic(satB, lightB)) hueB = hueA;
            double hueDelta = ((hueB - hueA) % 360 + 540) % 360 - 180;
            var rgb = FromHsl(hueA + hueDelta * t,
                Math.Clamp(satA + (satB - satA) * t, 0, 1),
                Math.Clamp(lightA + (lightB - lightA) * t, 0, 1));
            int alpha = (int)Math.Round(a.A + (b.A - a.A) * t);
            return Color.FromArgb(Math.Clamp(alpha, 0, 255), rgb.R, rgb.G, rgb.B);
        }

        private static bool IsAchromatic(double sat, double light) => sat < 0.05 || light < 0.02 || light > 0.98;

        private static (double h, double s, double l) ToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2;
            double d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double s = d / (1 - Math.Abs(2 * l - 1));
            double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            return ((h + 360) % 360, Math.Min(1, s), l);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            return Color.FromArgb(255, (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        private static Color WithAlpha(Color c, double alpha) =>
            Color.FromArgb((int)Math.Round(c.A * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
    }
}
