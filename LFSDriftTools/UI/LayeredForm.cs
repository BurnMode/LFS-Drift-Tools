using System;
using System.Collections.Generic;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct UPDATELAYEREDWINDOWINFO
        {
            public int cbSize;
            public IntPtr hdcDst;
            public POINT* pptDst;
            public SIZE* psize;
            public IntPtr hdcSrc;
            public POINT* pptSrc;
            public int crKey;
            public BLENDFUNCTION* pblend;
            public int dwFlags;
            public RECT* prcDirty;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern unsafe bool UpdateLayeredWindowIndirect(IntPtr hwnd, UPDATELAYEREDWINDOWINFO* info);

        private const int TileWidth = 128;
        private const int TileHeight = 64;
        private const int MaxDirtyRects = 4;
        private const int TileMergeReach = 2;
        private static readonly byte[] ZeroTileRow = new byte[TileWidth * 4];

        private int _tilesX;
        private int _tilesY;
        private bool[] _tileOccupied = Array.Empty<bool>();
        private bool[] _tileDirty = Array.Empty<bool>();
        private bool[] _tileVisited = Array.Empty<bool>();
        private int[] _tileStack = Array.Empty<int>();
        private readonly List<Rectangle> _dirtyRects = new();
        private bool _surfaceSynced = true;

        protected unsafe void ClearSurface(Graphics g)
        {
            if (_surfaceBits == IntPtr.Zero)
            {
                g.Clear(Color.Transparent);
                return;
            }

            byte* bits = (byte*)_surfaceBits;
            int stride = _surfaceWidth * 4;

            if (!_surfaceSynced)
            {
                new Span<byte>(bits, stride * _surfaceHeight).Clear();
                _forcePresent = true;
                return;
            }

            _surfaceSynced = false;
            for (int ty = 0; ty < _tilesY; ty++)
            {
                int y0 = ty * TileHeight;
                int y1 = Math.Min(_surfaceHeight, y0 + TileHeight);
                for (int tx = 0; tx < _tilesX; tx++)
                {
                    if (!_tileOccupied[ty * _tilesX + tx]) continue;
                    int x0 = tx * TileWidth * 4;
                    int len = Math.Min(TileWidth, _surfaceWidth - tx * TileWidth) * 4;
                    for (int y = y0; y < y1; y++)
                        new Span<byte>(bits + y * stride + x0, len).Clear();
                }
            }
        }

        protected unsafe bool PresentSurface()
        {
            if (_surface == null) return false;

            if (_surfaceBits == IntPtr.Zero)
            {
                SetBitmap(_surface);
                return true;
            }

            _surfaceSynced = true;
            long scanStart = PerfProbe.Begin();
            bool changed = CollectDirtyTiles();
            _useScanRegions = false;
            if (changed) BuildDirtyRects();
            PerfProbe.End(PerfSection.OverlayScan, scanStart);
            if (!changed)
            {
                PerfProbe.Count(PerfCounter.OverlayFramesSkipped);
                return false;
            }

            bool full = _forcePresent;
            _forcePresent = false;
            long presentStart = PerfProbe.Begin();
            try
            {
                return PresentDirty(full);
            }
            finally
            {
                PerfProbe.End(PerfSection.OverlayUpdateWindow, presentStart);
            }
        }

        private unsafe bool PresentDirty(bool full)
        {
            PerfProbe.Count(PerfCounter.OverlayFramesPresented);
            if (PerfProbe.Enabled)
            {
                long pixels = 0;
                foreach (var r in _dirtyRects) pixels += (long)r.Width * r.Height;
                PerfProbe.Count(PerfCounter.OverlayDirtyPixels, full ? (long)_surfaceWidth * _surfaceHeight : pixels);
                PerfProbe.Count(PerfCounter.OverlayDirtyRects, full ? 1 : _dirtyRects.Count);
            }

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

            if (!full)
            {
                bool ok = true;
                foreach (var r in _dirtyRects)
                {
                    var dirty = new RECT { left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom };
                    var info = new UPDATELAYEREDWINDOWINFO
                    {
                        cbSize = sizeof(UPDATELAYEREDWINDOWINFO),
                        pptDst = &topPos,
                        psize = &size,
                        hdcSrc = _surfaceDc,
                        pptSrc = &pointSource,
                        pblend = &blend,
                        dwFlags = ULW_ALPHA,
                        prcDirty = &dirty
                    };
                    if (!UpdateLayeredWindowIndirect(Handle, &info))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }

            PerfProbe.Count(PerfCounter.OverlayFullPresents);
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref topPos, ref size, _surfaceDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            return true;
        }

        private bool[] _tileCandidate = Array.Empty<bool>();
        private readonly List<Rectangle> _scanRegions = new();
        private bool _useScanRegions;

        protected void BeginScanRegions()
        {
            _scanRegions.Clear();
            _useScanRegions = true;
        }

        protected Rectangle AddScanRegion(RectangleF area)
        {
            var r = Rectangle.Intersect(
                Rectangle.FromLTRB((int)Math.Floor(area.Left) - 2, (int)Math.Floor(area.Top) - 2, (int)Math.Ceiling(area.Right) + 2, (int)Math.Ceiling(area.Bottom) + 2),
                new Rectangle(0, 0, Math.Max(1, _surfaceWidth), Math.Max(1, _surfaceHeight)));
            if (r.Width > 0 && r.Height > 0) _scanRegions.Add(r);
            return r;
        }

        private void BuildCandidateTiles()
        {
            Array.Copy(_tileOccupied, _tileCandidate, _tileOccupied.Length);
            foreach (var r in _scanRegions)
            {
                int tx0 = Math.Max(0, r.Left / TileWidth);
                int tx1 = Math.Min(_tilesX - 1, (r.Right - 1) / TileWidth);
                int ty0 = Math.Max(0, r.Top / TileHeight);
                int ty1 = Math.Min(_tilesY - 1, (r.Bottom - 1) / TileHeight);
                for (int ty = ty0; ty <= ty1; ty++)
                    for (int tx = tx0; tx <= tx1; tx++)
                        _tileCandidate[ty * _tilesX + tx] = true;
            }
        }

        private unsafe bool CollectDirtyTiles()
        {
            byte* cur = (byte*)_surfaceBits;
            byte* last = (byte*)_lastFrameBits;
            int stride = _surfaceWidth * 4;
            bool any = false;

            if (_forcePresent)
            {
                Array.Fill(_tileDirty, true);
                any = true;
            }
            else if (_useScanRegions)
            {
                Array.Clear(_tileDirty);
                BuildCandidateTiles();
                for (int ty = 0; ty < _tilesY; ty++)
                {
                    int rowTile = ty * _tilesX;
                    int minTx = -1, maxTx = -1;
                    for (int tx = 0; tx < _tilesX; tx++)
                    {
                        if (!_tileCandidate[rowTile + tx]) continue;
                        if (minTx < 0) minTx = tx;
                        maxTx = tx;
                    }
                    if (minTx < 0) continue;

                    int spanStart = minTx * TileWidth * 4;
                    int spanLen = (Math.Min(_surfaceWidth, (maxTx + 1) * TileWidth) - minTx * TileWidth) * 4;
                    int y0 = ty * TileHeight;
                    int y1 = Math.Min(_surfaceHeight, y0 + TileHeight);
                    for (int y = y0; y < y1; y++)
                    {
                        var rowCur = new ReadOnlySpan<byte>(cur + y * stride, stride);
                        var rowLast = new ReadOnlySpan<byte>(last + y * stride, stride);
                        if (rowCur.Slice(spanStart, spanLen).SequenceEqual(rowLast.Slice(spanStart, spanLen))) continue;

                        for (int tx = minTx; tx <= maxTx; tx++)
                        {
                            if (!_tileCandidate[rowTile + tx] || _tileDirty[rowTile + tx]) continue;
                            int x0 = tx * TileWidth * 4;
                            int len = Math.Min(TileWidth, _surfaceWidth - tx * TileWidth) * 4;
                            if (!rowCur.Slice(x0, len).SequenceEqual(rowLast.Slice(x0, len)))
                            {
                                _tileDirty[rowTile + tx] = true;
                                any = true;
                            }
                        }
                    }
                }
            }
            else
            {
                Array.Clear(_tileDirty);
                for (int y = 0; y < _surfaceHeight; y++)
                {
                    var rowCur = new ReadOnlySpan<byte>(cur + y * stride, stride);
                    var rowLast = new ReadOnlySpan<byte>(last + y * stride, stride);
                    if (rowCur.SequenceEqual(rowLast)) continue;

                    int rowTile = (y / TileHeight) * _tilesX;
                    for (int tx = 0; tx < _tilesX; tx++)
                    {
                        if (_tileDirty[rowTile + tx]) continue;
                        int x0 = tx * TileWidth * 4;
                        int len = Math.Min(TileWidth, _surfaceWidth - tx * TileWidth) * 4;
                        if (!rowCur.Slice(x0, len).SequenceEqual(rowLast.Slice(x0, len)))
                        {
                            _tileDirty[rowTile + tx] = true;
                            any = true;
                        }
                    }
                }
            }

            if (!any) return false;

            var zero = new ReadOnlySpan<byte>(ZeroTileRow);
            for (int ty = 0; ty < _tilesY; ty++)
            {
                int y0 = ty * TileHeight;
                int y1 = Math.Min(_surfaceHeight, y0 + TileHeight);
                for (int tx = 0; tx < _tilesX; tx++)
                {
                    int index = ty * _tilesX + tx;
                    if (!_tileDirty[index]) continue;
                    int x0 = tx * TileWidth * 4;
                    int len = Math.Min(TileWidth, _surfaceWidth - tx * TileWidth) * 4;
                    bool occupied = false;
                    for (int y = y0; y < y1; y++)
                    {
                        var src = new ReadOnlySpan<byte>(cur + y * stride + x0, len);
                        src.CopyTo(new Span<byte>(last + y * stride + x0, len));
                        if (!occupied && !src.SequenceEqual(zero.Slice(0, len)))
                            occupied = true;
                    }
                    _tileOccupied[index] = occupied;
                }
            }
            return true;
        }

        private void BuildDirtyRects()
        {
            _dirtyRects.Clear();
            Array.Clear(_tileVisited);

            for (int start = 0; start < _tileDirty.Length; start++)
            {
                if (!_tileDirty[start] || _tileVisited[start]) continue;

                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                int top = 0;
                _tileStack[top++] = start;
                _tileVisited[start] = true;
                while (top > 0)
                {
                    int index = _tileStack[--top];
                    int tx = index % _tilesX, ty = index / _tilesX;
                    minX = Math.Min(minX, tx); maxX = Math.Max(maxX, tx);
                    minY = Math.Min(minY, ty); maxY = Math.Max(maxY, ty);

                    for (int ny = Math.Max(0, ty - TileMergeReach); ny <= Math.Min(_tilesY - 1, ty + TileMergeReach); ny++)
                    {
                        for (int nx = Math.Max(0, tx - TileMergeReach); nx <= Math.Min(_tilesX - 1, tx + TileMergeReach); nx++)
                        {
                            int n = ny * _tilesX + nx;
                            if (!_tileDirty[n] || _tileVisited[n]) continue;
                            _tileVisited[n] = true;
                            _tileStack[top++] = n;
                        }
                    }
                }

                int left = minX * TileWidth, topPx = minY * TileHeight;
                int right = Math.Min(_surfaceWidth, (maxX + 1) * TileWidth);
                int bottom = Math.Min(_surfaceHeight, (maxY + 1) * TileHeight);
                _dirtyRects.Add(Rectangle.FromLTRB(left, topPx, right, bottom));
            }

            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < _dirtyRects.Count && !merged; i++)
                {
                    for (int j = i + 1; j < _dirtyRects.Count; j++)
                    {
                        if (!_dirtyRects[i].IntersectsWith(_dirtyRects[j])) continue;
                        _dirtyRects[i] = Rectangle.Union(_dirtyRects[i], _dirtyRects[j]);
                        _dirtyRects.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            while (_dirtyRects.Count > MaxDirtyRects)
            {
                int bestI = 0, bestJ = 1;
                long bestCost = long.MaxValue;
                for (int i = 0; i < _dirtyRects.Count; i++)
                {
                    for (int j = i + 1; j < _dirtyRects.Count; j++)
                    {
                        var u = Rectangle.Union(_dirtyRects[i], _dirtyRects[j]);
                        long cost = (long)u.Width * u.Height
                            - (long)_dirtyRects[i].Width * _dirtyRects[i].Height
                            - (long)_dirtyRects[j].Width * _dirtyRects[j].Height;
                        if (cost < bestCost) { bestCost = cost; bestI = i; bestJ = j; }
                    }
                }
                _dirtyRects[bestI] = Rectangle.Union(_dirtyRects[bestI], _dirtyRects[bestJ]);
                _dirtyRects.RemoveAt(bestJ);
            }
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
            _tilesX = (w + TileWidth - 1) / TileWidth;
            _tilesY = (h + TileHeight - 1) / TileHeight;
            int tiles = _tilesX * _tilesY;
            _tileOccupied = new bool[tiles];
            _tileDirty = new bool[tiles];
            _tileVisited = new bool[tiles];
            _tileStack = new int[tiles];
            _tileCandidate = new bool[tiles];
            _surfaceSynced = true;
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