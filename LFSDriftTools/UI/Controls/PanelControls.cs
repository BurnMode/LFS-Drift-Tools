using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 20;

        public RoundedPanel()
        {
            DoubleBuffered = true;

            BackColor = ApplePalette.Card;

            Padding = new Padding(15);
        }

        private Bitmap? _backgroundCache;
        private Size _cachedSize;
        private int _cachedRadius;
        private Color _cachedParentColor;
        private Color _cachedFillColor;
        private Color _cachedBorderColor;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;

            Color parentColor = Parent?.BackColor ?? Color.Black;
            Color borderColor = ApplePalette.Border;

            if (_backgroundCache == null
                || _cachedSize != Size
                || _cachedRadius != Radius
                || _cachedParentColor != parentColor
                || _cachedFillColor != BackColor
                || _cachedBorderColor != borderColor)
            {
                RebuildBackgroundCache(parentColor, borderColor);
            }

            e.Graphics.DrawImageUnscaled(_backgroundCache!, 0, 0);
        }

        private void RebuildBackgroundCache(Color parentColor, Color borderColor)
        {
            _backgroundCache?.Dispose();
            _backgroundCache = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            using (Graphics g = Graphics.FromImage(_backgroundCache))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                g.Clear(parentColor);

                Rectangle shadow = new Rectangle(
                    4,
                    6,
                    Width - 8,
                    Height - 8);

                using (GraphicsPath shadowPath = DrawingHelpers.RoundedPath(shadow, Radius))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
                {
                    g.FillPath(sb, shadowPath);
                }

                Rectangle rect = new Rectangle(
                    0,
                    0,
                    Width - 1,
                    Height - 1);

                using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, Radius))
                {
                    using (SolidBrush b = new SolidBrush(BackColor))
                        g.FillPath(b, path);

                    using (Pen p = new Pen(borderColor))
                        g.DrawPath(p, path);
                }
            }

            _cachedSize = Size;
            _cachedRadius = Radius;
            _cachedParentColor = parentColor;
            _cachedFillColor = BackColor;
            _cachedBorderColor = borderColor;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _backgroundCache?.Dispose();
                _backgroundCache = null;
            }
            base.Dispose(disposing);
        }

    }

    public class NoScrollBarPanel : Panel
    {
        private int _scrollY;
        private int _contentHeight;

        public int ContentHeight
        {
            get => _contentHeight;
            set
            {
                _contentHeight = Math.Max(0, value);
                int clamped = Math.Clamp(_scrollY, 0, MaxScrollY);
                if (clamped != _scrollY)
                    ScrollTo(clamped);
                else
                    ScrollChanged?.Invoke(_scrollY);
            }
        }

        public int ScrollY => _scrollY;
        public int MaxScrollY => Math.Max(0, _contentHeight - ClientSize.Height);

        public event Action<int>? ScrollChanged;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr BeginDeferWindowPos(int nNumWindows);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const double WheelStepPx = 55.0;
        private const double SmoothTauMs = 70.0;
        private const double MaxFrameMs = 100.0;

        private readonly System.Windows.Forms.Timer _smoothTimer = new() { Interval = 8 };
        private double _animPos;
        private int _targetY;
        private long _lastAnimTimestamp;

        public NoScrollBarPanel()
        {
            DoubleBuffered = true;
            _smoothTimer.Tick += (s, e) => AnimateStep();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _smoothTimer.Stop();
                _smoothTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void ScrollTo(int y)
        {
            _smoothTimer.Stop();
            ApplyScroll(y);
            _animPos = _scrollY;
            _targetY = _scrollY;
        }

        private void ApplyScroll(int y)
        {
            int clamped = Math.Clamp(y, 0, MaxScrollY);
            int delta = clamped - _scrollY;
            _scrollY = clamped;
            if (delta == 0) return;

            SuspendLayout();
            try
            {
                MoveChildren(delta);
            }
            finally
            {
                ResumeLayout(performLayout: false);
            }

            Invalidate();
            ScrollChanged?.Invoke(_scrollY);
        }

        private void MoveChildren(int delta)
        {
            int count = Controls.Count;
            var originalTops = new int[count];
            for (int i = 0; i < count; i++)
                originalTops[i] = Controls[i].Top;

            IntPtr batch = IsHandleCreated && count > 0 ? BeginDeferWindowPos(count) : IntPtr.Zero;

            if (batch != IntPtr.Zero)
            {
                bool failed = false;
                for (int i = 0; i < count; i++)
                {
                    Control c = Controls[i];
                    if (!c.IsHandleCreated) continue;

                    IntPtr next = DeferWindowPos(batch, c.Handle, IntPtr.Zero,
                        c.Left, originalTops[i] - delta, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    if (next == IntPtr.Zero)
                    {
                        failed = true;
                        break;
                    }
                    batch = next;
                }

                if (!failed)
                    EndDeferWindowPos(batch);
            }

            for (int i = 0; i < count; i++)
            {
                Control c = Controls[i];
                if (c.Top == originalTops[i])
                    c.Top -= delta;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int stepPx = (int)Math.Round(e.Delta / 120.0 * WheelStepPx);
            if (stepPx == 0) return;

            if (!_smoothTimer.Enabled)
            {
                _animPos = _scrollY;
                _targetY = _scrollY;
                _lastAnimTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            _targetY = Math.Clamp(_targetY - stepPx, 0, MaxScrollY);
            _smoothTimer.Start();
        }

        private void AnimateStep()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double dtMs = (now - _lastAnimTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _lastAnimTimestamp = now;
            dtMs = Math.Min(dtMs, MaxFrameMs);

            double k = 1.0 - Math.Exp(-dtMs / SmoothTauMs);
            _animPos += (_targetY - _animPos) * k;

            if (Math.Abs(_targetY - _animPos) < 0.5)
            {
                _animPos = _targetY;
                _smoothTimer.Stop();
            }

            ApplyScroll((int)Math.Round(_animPos));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ScrollTo(_scrollY);
        }
    }

    public class NoWheelRichTextBox : RichTextBox
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_MOUSEWHEEL = 0x020A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                Control? target = Parent;
                while (target != null && target is not NoScrollBarPanel)
                    target = target.Parent;

                if (target != null)
                    SendMessage(target.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class MinimalScrollbar : Control
    {
        public const int TrackWidth = 14;
        private const int ThumbWidth = 6;
        private const float HoverGrowFactor = 1.10f;
        private const int MinThumbHeight = 24;

        private NoScrollBarPanel? _target;
        private bool _hover;
        private bool _widened;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartScroll;
        private int _knownScrollValue;
        private int _lastDragScrollTick;
        private float _thumbWidthMul = 1.0f;
        private float _thumbWidthMulTarget = 1.0f;
        private readonly System.Windows.Forms.Timer _hoverAnimTimer;

        public MinimalScrollbar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = ApplePalette.Background;
            Width = TrackWidth;
            Cursor = Cursors.Default;

            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;

            _hoverAnimTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _hoverAnimTimer.Tick += (s, e) =>
            {
                float diff = _thumbWidthMulTarget - _thumbWidthMul;
                if (Math.Abs(diff) < 0.002f)
                {
                    _thumbWidthMul = _thumbWidthMulTarget;
                    _hoverAnimTimer.Stop();
                }
                else
                {
                    _thumbWidthMul += diff * 0.3f;
                }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hoverAnimTimer.Dispose();
            base.Dispose(disposing);
        }

        private void RefreshScrollValue()
        {
            if (_target != null) _knownScrollValue = _target.ScrollY;
        }

        public void AttachTo(NoScrollBarPanel target)
        {
            _target = target;
            target.Resize += (s, e) => { SyncBounds(); RefreshScrollValue(); Invalidate(); };
            target.ScrollChanged += v => { _knownScrollValue = v; Invalidate(); };

            SyncBounds();
            RefreshScrollValue();
            Invalidate();
        }

        public void SyncToTarget()
        {
            RefreshScrollValue();
            Invalidate();
        }

        public void ApplyThemeColor(Color background)
        {
            BackColor = background;
            Invalidate();
        }

        private void SetWidened(bool widened)
        {
            if (_widened == widened) return;
            _widened = widened;
            _thumbWidthMulTarget = widened ? HoverGrowFactor : 1.0f;
            _hoverAnimTimer.Start();
        }

        private void SyncBounds()
        {
            if (_target?.Parent == null) return;
            Location = new Point(_target.Right, _target.Top);
            Size = new Size(TrackWidth, _target.Height);
        }

        private (int top, int height) ComputeThumb()
        {
            if (_target == null) return (0, Height);
            int trackH = Height;
            int contentHeight = Math.Max(1, _target.ContentHeight);
            int viewportHeight = Math.Max(1, _target.ClientSize.Height);
            if (contentHeight <= viewportHeight) return (0, trackH);

            int thumbH = Math.Max(MinThumbHeight, (int)((long)trackH * viewportHeight / contentHeight));
            int scrollableRange = Math.Max(1, _target.MaxScrollY);
            int value = Math.Clamp(_knownScrollValue, 0, scrollableRange);
            int thumbY = (int)((long)(trackH - thumbH) * value / scrollableRange);
            return (thumbY, thumbH);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var (top, height) = ComputeThumb();

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Color thumbColor = _hover || _dragging
                ? Color.FromArgb(220, 150, 150, 150)
                : Color.FromArgb(130, 150, 150, 150);

            int thumbWidth = Math.Max(1, (int)Math.Round(ThumbWidth * _thumbWidthMul));
            int thumbX = (Width - thumbWidth) / 2;
            Rectangle rect = new Rectangle(thumbX, top, thumbWidth, height);
            using var path = DrawingHelpers.RoundedPath(rect, thumbWidth / 2);
            using var brush = new SolidBrush(thumbColor);
            e.Graphics.FillPath(brush, path);
        }

        private bool CanScroll => _target != null && _target.ContentHeight > _target.ClientSize.Height;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!CanScroll) return;
            var (top, height) = ComputeThumb();
            if (e.Y < top || e.Y > top + height) return;

            _dragging = true;
            _dragStartY = e.Y;
            _dragStartScroll = _knownScrollValue;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var (top, height) = ComputeThumb();
            bool nowHover = CanScroll && e.Y >= top && e.Y <= top + height;
            if (nowHover != _hover) { _hover = nowHover; Invalidate(); }

            if (!_dragging || _target == null) return;

            int scrollableRange = Math.Max(1, _target.MaxScrollY);
            int trackRange = Math.Max(1, Height - height);

            int deltaPixels = e.Y - _dragStartY;
            int deltaValue = (int)((long)deltaPixels * scrollableRange / trackRange);
            int newValue = Math.Clamp(_dragStartScroll + deltaValue, 0, scrollableRange);

            if (newValue == _knownScrollValue) return;

            int now = Environment.TickCount;
            if (now - _lastDragScrollTick < 16) return;
            _lastDragScrollTick = now;

            _target.ScrollTo(newValue);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
            RefreshScrollValue();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetWidened(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            _hover = false;
            SetWidened(false);
            Invalidate();
        }
    }

    public class MinimalNumericUpDown : Panel
    {
        public decimal Minimum { get; set; }
        public decimal Maximum { get; set; }
        public decimal Increment { get; set; } = 1;

        private int _decimalPlaces = 0;
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set { _decimalPlaces = value; UpdateText(); }
        }

        private decimal _value;
        public decimal Value
        {
            get => _value;
            set => SetValue(value, raiseEvent: true);
        }

        public event Action<decimal>? ValueChanged;

        private const int StepBtnW = 14;
        private const int Pad = 3;

        private readonly TextBox _text;
        private readonly Button _minusBtn;
        private readonly Button _plusBtn;

        public MinimalNumericUpDown(HorizontalAlignment textAlign = HorizontalAlignment.Right)
        {
            BackColor = ApplePalette.Card;
            Tag = "theme:card";

            _minusBtn = MakeStepButton("−");
            _plusBtn = MakeStepButton("+");
            _minusBtn.Click += (s, e) => Step(-1);
            _plusBtn.Click += (s, e) => Step(1);

            _text = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,
                Font = new Font("Segoe UI", 10f),
                TextAlign = textAlign,
            };
            _text.KeyPress += Text_KeyPress;
            _text.Leave += (s, e) => CommitTextInput();
            _text.KeyDown += Text_KeyDown;

            _text.GotFocus += (s, e) =>
            {
                BackColor = DrawingHelpers.Lighten(ApplePalette.Card, 0.02);
                Invalidate();
            };
            _text.LostFocus += (s, e) =>
            {
                BackColor = ApplePalette.Card;
                Invalidate();
            };

            Controls.Add(_text);
            Controls.Add(_minusBtn);
            Controls.Add(_plusBtn);

            Resize += (s, e) => LayoutChildren();

            Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, 10))
                using (Pen border = new Pen(ApplePalette.Border))
                    e.Graphics.DrawPath(border, path);
            };
        }

        private Button MakeStepButton(string glyph)
        {
            var b = new Button
            {

                Text = "",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ApplePalette.Secondary,
                Font = new Font("Segoe UI Semibold", 9f),
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 240);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 220, 225);
            b.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                SizeF size = e.Graphics.MeasureString(glyph, b.Font);
                float x = (b.Width - size.Width) / 2f;
                float y = (b.Height - size.Height) / 2f;
                using var brush = new SolidBrush(b.ForeColor);
                e.Graphics.DrawString(glyph, b.Font, brush, x, y);
            };
            return b;
        }

        private void LayoutChildren()
        {
            int btnH = Math.Max(1, Height - 12);
            _minusBtn.SetBounds(Pad, 6, StepBtnW, btnH);
            _plusBtn.SetBounds(Width - Pad - StepBtnW, 6, StepBtnW, btnH);

            int textX = Pad + StepBtnW + Pad;
            int textW = Math.Max(1, Width - 2 * textX);
            _text.SetBounds(textX, 6, textW, btnH);
        }

        private void Step(int direction) => SetValue(_value + Increment * direction, raiseEvent: true);

        private void SetValue(decimal v, bool raiseEvent)
        {
            v = Math.Clamp(v, Minimum, Maximum);
            bool changed = v != _value;
            _value = v;
            UpdateText();
            if (changed && raiseEvent)
                ValueChanged?.Invoke(_value);
        }

        private void UpdateText()
        {
            _text.Text = _decimalPlaces > 0 ? _value.ToString("F" + _decimalPlaces) : _value.ToString("0");
        }

        private void Text_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (char.IsDigit(e.KeyChar)) return;
            if (e.KeyChar == '-' && _text.SelectionStart == 0 && Minimum < 0 && !_text.Text.Contains('-')) return;
            if (_decimalPlaces > 0 && (e.KeyChar == '.' || e.KeyChar == ',') && !_text.Text.Contains('.') && !_text.Text.Contains(','))
                return;
            e.Handled = true;
        }

        private void Text_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { CommitTextInput(); e.SuppressKeyPress = true; }
        }

        private void CommitTextInput()
        {
            string normalized = _text.Text.Replace(',', '.');
            if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                SetValue(v, raiseEvent: true);
            else
                UpdateText();
        }
    }
}
