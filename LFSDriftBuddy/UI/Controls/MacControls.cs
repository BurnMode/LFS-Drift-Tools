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
    public class MacToggleSwitch : Control
    {
        private bool _checked = false;
        private float _knobProgress = 0f;
        private float _animFrom, _animTo;
        private DateTime _animStart;
        private readonly System.Windows.Forms.Timer _animTimer;
        private const int AnimDurationMs = 160;

        private bool _isPressed = false;

        public event EventHandler? CheckedChanged;

        public Color OnColor { get; set; } = Color.FromArgb(52, 199, 89);
        public Color OffColor { get; set; } = Color.FromArgb(210, 210, 215);
        public Color KnobColor { get; set; } = Color.White;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                AnimateTo(value ? 1f : 0f);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public MacToggleSwitch()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Cursor = Cursors.Hand;
            Size = new Size(51, 30);

            _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _animTimer.Tick += AnimTimer_Tick;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetCheckedSilent(bool value)
        {
            if (_checked == value) return;
            _checked = value;
            AnimateTo(value ? 1f : 0f);

        }

        private void AnimateTo(float target)
        {
            _animFrom = _knobProgress;
            _animTo = target;
            _animStart = DateTime.Now;
            _animTimer.Start();
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            double t = Math.Min(1.0, elapsed / AnimDurationMs);

            double eased = 1 - Math.Pow(1 - t, 3);

            _knobProgress = (float)(_animFrom + (_animTo - _animFrom) * eased);
            Invalidate();

            if (t >= 1.0)
            {
                _knobProgress = _animTo;
                _animTimer.Stop();
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _isPressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            Rectangle trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = (Height - 1) / 2;

            Color trackColor = Blend(OffColor, OnColor, _knobProgress);

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (Pen border = new Pen(Color.FromArgb(25, 0, 0, 0), 1f))
            {
                g.DrawPath(border, trackPath);
            }

            int knobDiameter = Height - 5;
            int knobTravel = Width - knobDiameter - 5;
            int knobX = 2 + (int)(knobTravel * _knobProgress);
            int knobY = 2;

            Rectangle knobRect = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);

            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            Color knob = _isPressed ? DrawingHelpers.Darken(KnobColor, 0.05) : KnobColor;

            using (GraphicsPath knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knobRect);
                using (SolidBrush knobBrush = new SolidBrush(knob))
                    g.FillPath(knobBrush, knobPath);

                using (Pen knobBorder = new Pen(Color.FromArgb(20, 0, 0, 0), 1f))
                    g.DrawPath(knobBorder, knobPath);
            }

            if (!Enabled)
            {
                using GraphicsPath dimPath = DrawingHelpers.RoundedPath(trackRect, radius);
                using SolidBrush dimBrush = new SolidBrush(Color.FromArgb(120, Parent?.BackColor ?? BackColor));
                g.FillPath(dimBrush, dimPath);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

    }
    public class MacSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private bool _isDragging = false;
        private bool _isHoverKnob = false;

        public event EventHandler? ValueChanged;

        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == clamped) return;
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Color TrackColor { get; set; } = Color.FromArgb(225, 225, 230);
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255);
        public Color KnobColor { get; set; } = Color.White;

        public int? CenterValue { get; set; } = null;

        public MacSlider()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Cursor = Cursors.Hand;
            Height = 22;
        }

        public void SetValueSilent(int value)
        {
            int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
        }

        private int TrackY => Height / 2;
        private int KnobDiameter => Math.Min(18, Height - 2);
        private int TrackHeight => 6;

        private int ValueToX(int value)
        {
            int knobRadius = KnobDiameter / 2;
            float fraction = (_maximum > _minimum)
                ? (float)(value - _minimum) / (_maximum - _minimum)
                : 0f;
            int travel = Width - KnobDiameter;
            return knobRadius + (int)(travel * fraction);
        }

        private int XToValue(int x)
        {
            int knobRadius = KnobDiameter / 2;
            int travel = Width - KnobDiameter;
            if (travel <= 0) return _minimum;

            float fraction = (float)(x - knobRadius) / travel;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Value = XToValue(e.X);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int knobX = ValueToX(_value);
            bool overKnob = Math.Abs(e.X - knobX) <= KnobDiameter / 2 + 4;
            if (overKnob != _isHoverKnob)
            {
                _isHoverKnob = overKnob;
                Invalidate();
            }

            if (_isDragging)
            {
                Value = XToValue(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHoverKnob = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            int knobDiameter = KnobDiameter;
            int trackHeight = TrackHeight;
            int trackY = (Height - trackHeight) / 2;
            int knobRadius = knobDiameter / 2;

            Rectangle trackRect = new Rectangle(knobRadius, trackY, Width - knobDiameter, trackHeight);
            int radius = trackHeight / 2;

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            int knobX = ValueToX(_value);

            int fillStart, fillWidth;
            if (CenterValue.HasValue)
            {
                int centerX = ValueToX(CenterValue.Value);
                fillStart = Math.Min(centerX, knobX);
                fillWidth = Math.Abs(knobX - centerX);
            }
            else
            {
                fillStart = knobRadius;
                fillWidth = Math.Max(0, knobX - knobRadius);
            }

            if (fillWidth > 0)
            {
                Rectangle fillRect = new Rectangle(fillStart, trackY, fillWidth, trackHeight);
                using (GraphicsPath fillPath = DrawingHelpers.RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           new Rectangle(fillStart, trackY, Math.Max(fillWidth, 1), trackHeight),
                           DrawingHelpers.Lighten(FillColor, 0.10),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }

            Rectangle knobRect = new Rectangle(knobX - knobRadius, (Height - knobDiameter) / 2, knobDiameter, knobDiameter);
            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            Color knob = _isDragging ? DrawingHelpers.Darken(KnobColor, 0.05)
                       : _isHoverKnob ? DrawingHelpers.Lighten(KnobColor, 0.0)
                       : KnobColor;

            using (GraphicsPath knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knobRect);
                using (SolidBrush knobBrush = new SolidBrush(knob))
                    g.FillPath(knobBrush, knobPath);

                using (Pen knobBorder = new Pen(Color.FromArgb(35, 0, 0, 0), 1f))
                    g.DrawPath(knobBorder, knobPath);
            }

            if (_isHoverKnob || _isDragging)
            {
                using (GraphicsPath ringPath = new GraphicsPath())
                {
                    Rectangle ringRect = Rectangle.Inflate(knobRect, 3, 3);
                    ringPath.AddEllipse(ringRect);
                    using (Pen ringPen = new Pen(Color.FromArgb(60, FillColor), 2f))
                        g.DrawPath(ringPen, ringPath);
                }
            }
        }

    }

    public class MacProgressBar : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;

        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == clamped) return;
                _value = clamped;
                Invalidate();
            }
        }

        public Color TrackColor { get; set; } = Color.FromArgb(225, 225, 230);
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255);

        public MacProgressBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Height = 8;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            int radius = Height / 2;
            Rectangle trackRect = new Rectangle(0, 0, Width, Height);

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            float percent = (_maximum > _minimum)
                ? (float)(_value - _minimum) / (_maximum - _minimum)
                : 0f;
            percent = Math.Max(0f, Math.Min(1f, percent));

            int fillWidth = (int)(Width * percent);

            if (fillWidth > 0)
            {

                fillWidth = Math.Max(fillWidth, Height);

                Rectangle fillRect = new Rectangle(0, 0, fillWidth, Height);

                using (GraphicsPath fillPath = DrawingHelpers.RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           fillRect,
                           DrawingHelpers.Lighten(FillColor, 0.10f),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }
        }

    }

    public class MacCheckBox : CheckBox
    {
        public Color CheckedColor { get; set; } = Color.FromArgb(0, 122, 255);

        public MacCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9.5f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? ApplePalette.Card))
                g.FillRectangle(parentBrush, ClientRectangle);

            const int boxSize = 18;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(0, boxY, boxSize, boxSize);

            using (GraphicsPath boxPath = DrawingHelpers.RoundedPath(boxRect, 5))
            {
                if (Checked)
                {
                    using (SolidBrush fill = new SolidBrush(CheckedColor))
                        g.FillPath(fill, boxPath);
                }
                else
                {

                    using (Pen border = new Pen(ApplePalette.Border, 1.5f))
                        g.DrawPath(border, boxPath);
                }
            }

            if (Checked)
            {
                using (Pen check = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    Point p1 = new Point(boxRect.X + 4, boxRect.Y + 9);
                    Point p2 = new Point(boxRect.X + 7, boxRect.Y + 13);
                    Point p3 = new Point(boxRect.X + 14, boxRect.Y + 5);
                    g.DrawLines(check, new[] { p1, p2, p3 });
                }
            }

            Rectangle textRect = new Rectangle(boxSize + 8, 0, Width - boxSize - 8, Height);

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                ApplePalette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

    }
}
