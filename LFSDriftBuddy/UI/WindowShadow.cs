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

using LFSDriftTools;

public class WindowShadow : Form
{
    private const int ShadowMargin = 200;
    private const int CornerRadius = 30;
    private readonly Form _owner;
    private Size _lastRenderedSize = Size.Empty;

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int ULW_ALPHA = 2;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint SWP_NOACTIVATE = 0x0010;

    public WindowShadow(Form owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public void Reposition()
    {
        if (!_owner.IsHandleCreated || _owner.WindowState == FormWindowState.Minimized || !_owner.Visible)
        {
            if (Visible) Hide();
            return;
        }

        var newBounds = new Rectangle(
            _owner.Left - ShadowMargin,
            _owner.Top - ShadowMargin + 12,
            _owner.Width + ShadowMargin * 2,
            _owner.Height + ShadowMargin * 2);

        bool sizeChanged = newBounds.Size != _lastRenderedSize;

        if (!Visible)
            Show();

        if (sizeChanged)
        {
            Bounds = newBounds;
            Render(newBounds);
            _lastRenderedSize = newBounds.Size;
        }
        else
        {

            MoveOnly(newBounds.Location);
        }

        SetWindowPos(Handle, _owner.Handle, 0, 0, 0, 0,
            0x0001 | 0x0002 | SWP_NOACTIVATE);
    }

    private void MoveOnly(Point location)
    {
        Left = location.X;
        Top = location.Y;
    }

    private void Render(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var windowRect = new Rectangle(ShadowMargin, ShadowMargin - 12, _owner.Width, _owner.Height);

            for (int i = ShadowMargin; i >= 1; i--)
            {
                int alpha = (int)(55 * Math.Pow(0.35 - (double)i / ShadowMargin, 2.2));
                if (alpha <= 0) continue;

                var layerRect = Rectangle.Inflate(windowRect, i, i);
                using var path = DrawingHelpers.RoundedPath(layerRect, CornerRadius + i);
                using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(brush, path);
            }
        }

        DrawBitmap(bmp, bounds.Location);
    }

    private void DrawBitmap(Bitmap bmp, Point location)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr oldBitmap = SelectObject(memDc, hBitmap);

        var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
        var pptSrc = new POINT { X = 0, Y = 0 };
        var pptDst = new POINT { X = location.X, Y = location.Y };
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };

        UpdateLayeredWindow(Handle, screenDc, ref pptDst, ref size, memDc, ref pptSrc, 0, ref blend, ULW_ALPHA);

        SelectObject(memDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

}
