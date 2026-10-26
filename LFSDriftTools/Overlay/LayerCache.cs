using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LFSDriftTools
{
    public sealed class LayerCache : IDisposable
    {
        private const int MaxLayerPixels = 4096 * 4096;

        private readonly struct EntryKey : IEquatable<EntryKey>
        {
            public readonly object Key;
            public readonly float ScaleX, ScaleY, OffsetX, OffsetY;
            public readonly RectangleF Bounds;

            public EntryKey(object key, float sx, float sy, float dx, float dy, RectangleF bounds)
            {
                Key = key;
                ScaleX = sx;
                ScaleY = sy;
                OffsetX = dx;
                OffsetY = dy;
                Bounds = bounds;
            }

            public bool Equals(EntryKey other) =>
                ScaleX == other.ScaleX && ScaleY == other.ScaleY && OffsetX == other.OffsetX && OffsetY == other.OffsetY
                && Bounds == other.Bounds && Equals(Key, other.Key);

            public override bool Equals(object? obj) => obj is EntryKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Key, ScaleX, ScaleY, OffsetX, OffsetY, Bounds);
        }

        private sealed class Entry
        {
            public Bitmap Bitmap = null!;
            public Rectangle Device;
            public long LastUse;
        }

        private readonly Dictionary<EntryKey, Entry> _entries = new();
        private readonly int _capacity;
        private long _useCounter;

        public LayerCache(int capacity = 1)
        {
            _capacity = Math.Max(1, capacity);
        }

        private ImageAttributes? _opacityAttrs;
        private float _opacityAttrsValue = -1f;

        public void Draw(Graphics g, RectangleF bounds, object key, Action<Graphics> render)
        {
            if (!TryDraw(g, bounds, key, render, 1f))
                render(g);
        }

        public bool TryDraw(Graphics g, RectangleF bounds, object key, Action<Graphics> render, float opacity)
        {
            if (opacity <= 0f) return true;
            opacity = Math.Min(1f, opacity);

            float sx, sy, dx, dy;
            using (var m = g.Transform)
            {
                var e = m.Elements;
                if (e[1] != 0f || e[2] != 0f || e[0] <= 0f || e[3] <= 0f)
                    return false;
                sx = e[0]; sy = e[3]; dx = e[4]; dy = e[5];
            }

            var entryKey = new EntryKey(key, sx, sy, dx, dy, bounds);
            if (!_entries.TryGetValue(entryKey, out var hit))
            {
                hit = Build(g, bounds, render, sx, sy, dx, dy);
                if (hit == null)
                    return false;
                if (_entries.Count >= _capacity) EvictOldest();
                _entries[entryKey] = hit;
            }
            hit.LastUse = ++_useCounter;

            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                if (opacity >= 1f)
                {
                    g.DrawImage(hit.Bitmap, hit.Device, 0, 0, hit.Device.Width, hit.Device.Height, GraphicsUnit.Pixel);
                }
                else
                {
                    if (_opacityAttrs == null || _opacityAttrsValue != opacity)
                    {
                        _opacityAttrs ??= new ImageAttributes();
                        _opacityAttrs.SetColorMatrix(new ColorMatrix { Matrix33 = opacity }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        _opacityAttrsValue = opacity;
                    }
                    g.DrawImage(hit.Bitmap, hit.Device, 0, 0, hit.Device.Width, hit.Device.Height, GraphicsUnit.Pixel, _opacityAttrs);
                }
            }
            finally
            {
                g.Restore(state);
            }
            return true;
        }

        private void EvictOldest()
        {
            EntryKey oldestKey = default;
            long oldest = long.MaxValue;
            bool found = false;
            foreach (var pair in _entries)
            {
                if (pair.Value.LastUse < oldest)
                {
                    oldest = pair.Value.LastUse;
                    oldestKey = pair.Key;
                    found = true;
                }
            }
            if (!found) return;
            _entries[oldestKey].Bitmap.Dispose();
            _entries.Remove(oldestKey);
        }

        private static Entry? Build(Graphics g, RectangleF bounds, Action<Graphics> render, float sx, float sy, float dx, float dy)
        {
            int left = (int)Math.Floor(bounds.Left * sx + dx);
            int top = (int)Math.Floor(bounds.Top * sy + dy);
            int right = (int)Math.Ceiling(bounds.Right * sx + dx);
            int bottom = (int)Math.Ceiling(bounds.Bottom * sy + dy);
            int w = right - left, h = bottom - top;
            if (w <= 0 || h <= 0 || (long)w * h > MaxLayerPixels) return null;

            var bitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var lg = Graphics.FromImage(bitmap))
            {
                lg.SmoothingMode = g.SmoothingMode;
                lg.TextRenderingHint = g.TextRenderingHint;
                lg.CompositingQuality = g.CompositingQuality;
                lg.InterpolationMode = g.InterpolationMode;
                lg.PixelOffsetMode = g.PixelOffsetMode;
                using var matrix = new Matrix(sx, 0f, 0f, sy, dx - left, dy - top);
                lg.Transform = matrix;
                render(lg);
            }

            return new Entry { Bitmap = bitmap, Device = new Rectangle(left, top, w, h) };
        }

        public void Clear()
        {
            foreach (var entry in _entries.Values) entry.Bitmap.Dispose();
            _entries.Clear();
        }

        public void Dispose()
        {
            Clear();
            _opacityAttrs?.Dispose();
            _opacityAttrs = null;
        }
    }
}
