using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LFSDriftBuddy
{
    public enum DashLampKind { TurnLeft, TurnRight, HighBeam }

    public sealed class DashboardLampIcon : Control
    {
        public DashLampKind Kind { get; set; } = DashLampKind.TurnLeft;

        private bool _lit;
        public bool Lit
        {
            get => _lit;
            set
            {
                if (_lit == value) return;
                _lit = value;
                Invalidate();
            }
        }

        public DashboardLampIcon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(48, 40);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color litColor = Kind == DashLampKind.HighBeam
                ? Color.FromArgb(90, 170, 255)
                : Color.FromArgb(90, 235, 120);
            Color offColor = Color.FromArgb(60, 63, 74);
            Color drawColor = Lit ? litColor : offColor;

            if (Lit)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb(45, litColor.R, litColor.G, litColor.B));
                g.FillEllipse(glowBrush, 1, 1, Width - 2, Height - 2);
            }

            using var pen = new Pen(drawColor, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(drawColor);

            switch (Kind)
            {
                case DashLampKind.TurnLeft:
                    DrawChevrons(g, brush, pointLeft: true);
                    break;
                case DashLampKind.TurnRight:
                    DrawChevrons(g, brush, pointLeft: false);
                    break;
                case DashLampKind.HighBeam:
                    DrawHighBeam(g, pen);
                    break;
            }
        }

        private void DrawChevrons(Graphics g, Brush brush, bool pointLeft)
        {
            float cy = Height / 2f;
            float cx = Width / 2f;
            const float w = 8.5f, h = 12f, step = 8f;

            for (int i = 0; i < 2; i++)
            {
                float ox = pointLeft ? cx + step - i * step * 1.7f : cx - step + i * step * 1.7f;
                PointF[] tri = pointLeft
                    ? new[] { new PointF(ox + w / 2, cy - h / 2), new PointF(ox - w / 2, cy), new PointF(ox + w / 2, cy + h / 2) }
                    : new[] { new PointF(ox - w / 2, cy - h / 2), new PointF(ox + w / 2, cy), new PointF(ox - w / 2, cy + h / 2) };
                g.FillPolygon(brush, tri);
            }
        }

        private void DrawHighBeam(Graphics g, Pen pen)
        {
            float cx = Width / 2f, cy = Height / 2f;

            g.DrawArc(pen, cx - 13, cy - 7, 14, 14, 90, 180);
            g.DrawLine(pen, cx - 13, cy - 7, cx - 13, cy + 7);

            for (int i = 0; i < 3; i++)
            {
                float ly = cy - 6 + i * 6;
                float lx2 = cx + 13 - i * 1.5f;
                g.DrawLine(pen, cx - 2, ly, lx2, ly);
            }
        }
    }
}
