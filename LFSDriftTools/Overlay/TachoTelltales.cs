using System.Drawing;
using System.Drawing.Drawing2D;

namespace LFSDriftTools
{
    public struct TelltaleFlags
    {
        public bool LeftSignal;
        public bool RightSignal;
        public bool HighBeam;
        public bool Handbrake;
        public bool TractionControl;
        public bool Abs;

        public static TelltaleFlags FromShowLights(uint lights) => new()
        {
            HighBeam = (lights & 0x0002) != 0,
            Handbrake = (lights & 0x0004) != 0,
            TractionControl = (lights & 0x0010) != 0,
            LeftSignal = (lights & 0x0020) != 0,
            RightSignal = (lights & 0x0040) != 0,
            Abs = (lights & 0x0400) != 0,
        };
    }

    public static class TachoTelltales
    {
        public static readonly Color SignalColor = Color.FromArgb(255, 136, 250, 21);
        public static readonly Color HighBeamColor = Color.FromArgb(255, 67, 199, 255);
        public static readonly Color WarningColor = Color.FromArgb(242, 255, 35, 34);

        public static void DrawType1(Graphics g, TelltaleFlags f, FontFamily family)
        {
            const float size = 16f;
            if (f.LeftSignal) FillArrow(g, 100f - size / 2f, 96f - size / 2f, size, left: true);
            if (f.RightSignal) FillArrow(g, 180f - size / 2f, 96f - size / 2f, size, left: false);
            if (f.HighBeam) DrawHighBeam(g, 140f - 9f, 72f - 9f, 18f);
            if (f.Handbrake) DrawHandbrake(g, 98f - 8.5f, 136f - 8.5f, 17f);
            if (f.TractionControl) DrawText(g, "TC", family, 12f, 182f, 128f, centered: true);
            if (f.Abs) DrawText(g, "ABS", family, 12f, 182f, 143f, centered: true);
        }

        private static readonly LayerCache _sprites = new(32);

        private static RectangleF IconBounds(float x, float y, float size) => new(x - 3f, y - 3f, size + 6f, size + 6f);

        public static void FillArrow(Graphics g, float x, float y, float size, bool left) =>
            _sprites.Draw(g, IconBounds(x, y, size), (1, x, y, size, left), lg => FillArrowDirect(lg, x, y, size, left));

        public static void DrawHighBeam(Graphics g, float x, float y, float size) =>
            _sprites.Draw(g, IconBounds(x, y, size), (2, x, y, size), lg => DrawHighBeamDirect(lg, x, y, size));

        public static void DrawHandbrake(Graphics g, float x, float y, float size) =>
            _sprites.Draw(g, IconBounds(x, y, size), (3, x, y, size), lg => DrawHandbrakeDirect(lg, x, y, size));

        public static void DrawText(Graphics g, string text, FontFamily family, float emPx, float x, float y, bool centered)
        {
            float width = emPx * text.Length + 6f;
            var bounds = new RectangleF(centered ? x - width / 2f : x - 3f, y - emPx, width, emPx * 2f);
            _sprites.Draw(g, bounds, (4, text, family, emPx, x, y, centered), lg => DrawTextDirect(lg, text, family, emPx, x, y, centered));
        }

        private static void FillArrowDirect(Graphics g, float x, float y, float size, bool left)
        {
            float k = size / 26f;
            float[] xs = left ? new[] { 15f, 0f, 15f, 15f, 26f, 26f, 15f } : new[] { 11f, 26f, 11f, 11f, 0f, 0f, 11f };
            float[] ys = { 0f, 13f, 26f, 18f, 18f, 8f, 8f };
            var pts = new PointF[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                pts[i] = new PointF(x + xs[i] * k, y + ys[i] * k);
            using var brush = new SolidBrush(SignalColor);
            g.FillPolygon(brush, pts);
        }

        private static void DrawHighBeamDirect(Graphics g, float x, float y, float size)
        {
            float k = size / 52f;
            float oy = y + (size - 40f * k) / 2f;
            PointF P(float px, float py) => new(x + px * k, oy + py * k);
            using var pen = new Pen(HighBeamColor, 4f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using (var path = new GraphicsPath())
            {
                path.AddBezier(P(30, 4), P(54, 4), P(54, 32), P(30, 32));
                path.AddBezier(P(30, 32), P(22, 32), P(22, 4), P(30, 4));
                g.DrawPath(pen, path);
            }
            float[] x1 = { 20f, 17.8f, 17f, 17.8f, 20f };
            float[] yy = { 4f, 11f, 18f, 25f, 32f };
            for (int i = 0; i < 5; i++)
                g.DrawLine(pen, P(x1[i], yy[i]), P(4f, yy[i]));
        }

        private static void DrawHandbrakeDirect(Graphics g, float x, float y, float size)
        {
            float k = size / 64f;
            PointF P(float px, float py) => new(x + px * k, y + py * k);
            using var pen = new Pen(WarningColor, 5f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float r = 22f * k;
            var c = P(32f, 32f);
            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2f, r * 2f);
            float r2 = 30f * k;
            var outer = new RectangleF(c.X - r2, c.Y - r2, r2 * 2f, r2 * 2f);
            g.DrawArc(pen, outer, 140f, 80f);
            g.DrawArc(pen, outer, -40f, 80f);
            using var path = new GraphicsPath();
            path.AddLine(P(28.3f, 44f), P(28.3f, 21f));
            path.AddLine(P(28.3f, 21f), P(33.3f, 21f));
            var arcOrigin = P(26.3f, 21f);
            path.AddArc(new RectangleF(arcOrigin.X, arcOrigin.Y, 14f * k, 14f * k), -90f, 180f);
            path.AddLine(P(33.3f, 35f), P(28.3f, 35f));
            g.DrawPath(pen, path);
        }

        private static void DrawTextDirect(Graphics g, string text, FontFamily family, float emPx, float x, float y, bool centered)
        {
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Bold, emPx, PointF.Empty, StringFormat.GenericTypographic);
            var b = path.GetBounds();
            using var m = new Matrix();
            float left = centered ? x - b.Width / 2f : x;
            m.Translate(left - b.X, y - b.Y - b.Height / 2f);
            path.Transform(m);
            using var brush = new SolidBrush(WarningColor);
            g.FillPath(brush, path);
        }
    }
}
