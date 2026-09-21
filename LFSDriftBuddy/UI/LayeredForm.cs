using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LFSDriftTools
{
        public class LayeredForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public void ForceTopmost()
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        private const int ULW_ALPHA = 0x02;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        protected LayeredForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }


        public void SetClickThrough(bool enabled)
        {
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            ex = enabled ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
        }

                protected void SetBitmap(Bitmap bitmap)
        {
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Bitmap must be 32bppArgb.");

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                var size = new SIZE(bitmap.Width, bitmap.Height);
                var pointSource = new POINT(0, 0);
                var topPos = new POINT(Left, Top);

                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero) { SelectObject(memDc, oldBitmap); DeleteObject(hBitmap); }
                DeleteDC(memDc);
            }
        }

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("ntdll.dll")]
        private static extern UIntPtr RtlCompareMemory(IntPtr source1, IntPtr source2, UIntPtr length);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);

        [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
        private static extern void ZeroMemory(IntPtr destination, UIntPtr length);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        private IntPtr _surfaceDc = IntPtr.Zero;
        private IntPtr _surfaceHBitmap = IntPtr.Zero;
        private IntPtr _surfaceOldBitmap = IntPtr.Zero;
        private IntPtr _surfaceBits = IntPtr.Zero;
        private IntPtr _lastFrameBits = IntPtr.Zero;
        private Bitmap? _surface;
        private int _surfaceWidth;
        private int _surfaceHeight;
        private bool _forcePresent = true;

        protected Bitmap AcquireSurface()
        {
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);

            if (_surface == null || _surfaceWidth != w || _surfaceHeight != h)
                CreateSurface(w, h);

            return _surface!;
        }

        protected bool PresentSurface()
        {
            if (_surface == null) return false;

            if (_surfaceBits == IntPtr.Zero)
            {
                SetBitmap(_surface);
                return true;
            }

            var byteCount = new UIntPtr((uint)(_surfaceWidth * _surfaceHeight * 4));

            if (!_forcePresent && RtlCompareMemory(_surfaceBits, _lastFrameBits, byteCount) == byteCount)
                return false;

            CopyMemory(_lastFrameBits, _surfaceBits, byteCount);
            _forcePresent = false;

            var size = new SIZE(_surfaceWidth, _surfaceHeight);
            var pointSource = new POINT(0, 0);
            var topPos = new POINT(Left, Top);

            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(Handle, IntPtr.Zero, ref topPos, ref size, _surfaceDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            return true;
        }

        private void CreateSurface(int w, int h)
        {
            FreeSurface();

            var info = new BITMAPINFO
            {
                biSize = Marshal.SizeOf<BITMAPINFO>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                _surfaceHBitmap = CreateDIBSection(screenDc, ref info, 0, out _surfaceBits, IntPtr.Zero, 0);
                if (_surfaceHBitmap != IntPtr.Zero && _surfaceBits != IntPtr.Zero)
                    _surfaceDc = CreateCompatibleDC(screenDc);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (_surfaceHBitmap == IntPtr.Zero || _surfaceBits == IntPtr.Zero || _surfaceDc == IntPtr.Zero)
            {
                FreeSurface();
                _surface = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                _surfaceWidth = w;
                _surfaceHeight = h;
                return;
            }

            _surfaceOldBitmap = SelectObject(_surfaceDc, _surfaceHBitmap);
            _surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _surfaceBits);

            var byteCount = new UIntPtr((uint)(w * h * 4));
            _lastFrameBits = Marshal.AllocHGlobal(w * h * 4);
            ZeroMemory(_lastFrameBits, byteCount);

            _surfaceWidth = w;
            _surfaceHeight = h;
            _forcePresent = true;
        }

        private void FreeSurface()
        {
            _surface?.Dispose();
            _surface = null;

            if (_surfaceDc != IntPtr.Zero)
            {
                if (_surfaceOldBitmap != IntPtr.Zero)
                    SelectObject(_surfaceDc, _surfaceOldBitmap);
                DeleteDC(_surfaceDc);
            }
            if (_surfaceHBitmap != IntPtr.Zero)
                DeleteObject(_surfaceHBitmap);
            if (_lastFrameBits != IntPtr.Zero)
                Marshal.FreeHGlobal(_lastFrameBits);

            _surfaceDc = IntPtr.Zero;
            _surfaceHBitmap = IntPtr.Zero;
            _surfaceOldBitmap = IntPtr.Zero;
            _surfaceBits = IntPtr.Zero;
            _lastFrameBits = IntPtr.Zero;
            _surfaceWidth = 0;
            _surfaceHeight = 0;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _forcePresent = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _forcePresent = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                FreeSurface();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e) {  }
    }
}