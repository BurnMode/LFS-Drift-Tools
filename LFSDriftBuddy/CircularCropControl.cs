using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LFSDriftBuddy
{
    public class CircularCropControl : Control
    {
        public Image SourceImage { get; set; } = null;

        private float _panX = 0f;
        public float PanX
        {
            get => _panX;
            set { _panX = Math.Clamp(value, -1f, 1f); Invalidate(); }
        }

        private float _panY = 0f;
        public float PanY
        {
            get => _panY;
            set { _panY = Math.Clamp(value, -1f, 1f); Invalidate(); }
        }

        private float _zoom = 1f;
        public float Zoom
        {
            get => _zoom;
            set { _zoom = Math.Clamp(value, 0.5f, 3f); Invalidate(); }
        }

        public event EventHandler PanZoomChanged;

        private bool _dragging;
        private Point _dragStart;
        private float _dragStartPanX, _dragStartPanY;

        public CircularCropControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(30, 30, 34);
            Cursor = Cursors.SizeAll;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float diameter = Math.Min(Width, Height) - 4;
            var dialRect = new RectangleF((Width - diameter) / 2f, (Height - diameter) / 2f, diameter, diameter);

            using (var bg = new SolidBrush(Color.FromArgb(20, 20, 24)))
                g.FillRectangle(bg, ClientRectangle);

            if (SourceImage != null)
            {
                float coverScale = diameter / Math.Min(SourceImage.Width, SourceImage.Height) * _zoom;
                float drawW = SourceImage.Width * coverScale;
                float drawH = SourceImage.Height * coverScale;
                float drawX = dialRect.X + diameter / 2f - drawW / 2f + _panX * diameter;
                float drawY = dialRect.Y + diameter / 2f - drawH / 2f + _panY * diameter;

                using var path = new GraphicsPath();
                path.AddEllipse(dialRect);
                var oldClip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                g.DrawImage(SourceImage, drawX, drawY, drawW, drawH);
                g.Clip = oldClip;
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(45, 45, 50));
                g.FillEllipse(brush, dialRect);
                using var textBrush = new SolidBrush(Color.FromArgb(140, 140, 150));
                var text = "No image";
                var font = new Font("Segoe UI", 9f);
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush, dialRect.X + diameter / 2f - size.Width / 2f,
                    dialRect.Y + diameter / 2f - size.Height / 2f);
            }

            using var ringPen = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
            g.DrawEllipse(ringPen, dialRect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (SourceImage == null) return;
            _dragging = true;
            _dragStart = e.Location;
            _dragStartPanX = _panX;
            _dragStartPanY = _panY;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            float diameter = Math.Min(Width, Height) - 4;
            if (diameter <= 0) return;

            float dx = (e.X - _dragStart.X) / diameter;
            float dy = (e.Y - _dragStart.Y) / diameter;
            PanX = _dragStartPanX + dx;
            PanY = _dragStartPanY + dy;
            PanZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Zoom += e.Delta > 0 ? 0.05f : -0.05f;
            PanZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
