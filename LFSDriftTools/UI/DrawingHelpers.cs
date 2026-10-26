using System;
using System.Drawing;
using System.Drawing.Drawing2D;

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

    public static GraphicsPath RoundedRect(RectangleF rect, float radius, bool clampToRect = false)
    {
        var path = new GraphicsPath();
        float d = clampToRect
            ? Math.Max(0.1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)))
            : radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static GraphicsPath RoundedLeftRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddLine(rect.Right, rect.Y, rect.Right, rect.Bottom);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static GraphicsPath RoundedRightRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddLine(rect.X, rect.Y, rect.Right - d, rect.Y);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom - radius);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddLine(rect.Right - d, rect.Bottom, rect.X, rect.Bottom);
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

    public static Color WithAlpha(Color c, double alpha)
    {
        alpha = Math.Max(0, Math.Min(1, alpha));
        return Color.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);
    }

    public static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
    {
        if (string.IsNullOrEmpty(text)) return;
        using (var shadowBrush = new SolidBrush(shadow))
            g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
        using (var fillBrush = new SolidBrush(fill))
            g.DrawString(text, font, fillBrush, x, y);
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
