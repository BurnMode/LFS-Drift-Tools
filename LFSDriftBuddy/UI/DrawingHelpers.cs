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

internal static class DrawingHelpers
{
    public static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
            return path;

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintSoftShadow(Graphics g, int width, int height)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 30; i >= 1; i--)
        {
            int alpha = (int)(22 * (1.0 - i / 30.0));
            Rectangle shadowRect = new Rectangle(12 - i, 12 - i, width - 24 + i * 2, height - 24 + i * 2);
            using (GraphicsPath p = RoundedPath(shadowRect, 20 + i))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                g.FillPath(b, p);
        }
    }

    public static Color Lighten(Color c, double amount)
    {
        return Color.FromArgb(
            c.A,
            Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
            Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
            Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
    }

    public static Color Darken(Color c, double amount)
    {
        return Color.FromArgb(
            c.A,
            (int)(c.R * (1 - amount)),
            (int)(c.G * (1 - amount)),
            (int)(c.B * (1 - amount)));
    }
}
