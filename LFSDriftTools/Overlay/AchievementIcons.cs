using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;

namespace LFSDriftTools
{
    public static class AchievementIcons
    {
        private static readonly Color LockedGlyph = Color.FromArgb(255, 120, 120, 126);
        private static readonly Color DarkDetail = Color.FromArgb(255, 30, 31, 38);
        private const int MaxCachedFonts = 64;
        private static readonly Dictionary<int, Font> FontCache = new();
        private static readonly object FontCacheLock = new();
        private static readonly StringFormat CenterFormat = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private static readonly StringFormat CenterNoWrapFormat = new(StringFormatFlags.NoWrap | StringFormatFlags.NoClip) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        private static Font GlyphFont(float sizePx)
        {
            int key = (int)MathF.Round(Math.Max(1f, sizePx) * 4f);
            lock (FontCacheLock)
            {
                if (!FontCache.TryGetValue(key, out Font? font))
                {
                    if (FontCache.Count >= MaxCachedFonts)
                    {
                        foreach (Font cached in FontCache.Values) cached.Dispose();
                        FontCache.Clear();
                    }
                    font = new Font("Segoe UI", key / 4f, FontStyle.Bold, GraphicsUnit.Pixel);
                    FontCache[key] = font;
                }
                return font;
            }
        }
        private static readonly Color HeartColor = Color.FromArgb(255, 255, 92, 120);
        private static readonly Color RedZone = Color.FromArgb(255, 255, 72, 72);

        public static Color RarityColor(AchievementRarity rarity) => rarity switch
        {
            AchievementRarity.Rare => Color.FromArgb(255, 64, 156, 255),
            AchievementRarity.Epic => Color.FromArgb(255, 178, 102, 255),
            AchievementRarity.Legendary => Color.FromArgb(255, 255, 196, 40),
            _ => Color.FromArgb(255, 190, 195, 205),
        };

        public static void DrawBadge(Graphics g, RectangleF r, AchievementIcon icon, AchievementRarity rarity, bool locked)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = r.Width * 0.22f;
            using (var path = DrawingHelpers.RoundedRect(r, radius, clampToRect: true))
            {
                using (var bg = new LinearGradientBrush(r,
                    locked ? Color.FromArgb(255, 62, 62, 66) : Color.FromArgb(255, 54, 58, 70),
                    locked ? Color.FromArgb(255, 36, 36, 40) : Color.FromArgb(255, 22, 24, 30),
                    LinearGradientMode.Vertical))
                    g.FillPath(bg, path);
                using var border = new Pen(locked ? Color.FromArgb(255, 96, 96, 100) : RarityColor(rarity), Math.Max(1.5f, r.Width * 0.05f));
                g.DrawPath(border, path);
            }

            var inner = RectangleF.Inflate(r, -r.Width * 0.2f, -r.Height * 0.2f);
            Color glyph = locked ? LockedGlyph : Color.White;
            DrawGlyph(g, icon, inner, glyph, locked);
            g.Restore(state);
        }

        public static void DrawGlyph(Graphics g, AchievementIcon icon, RectangleF r, Color color, bool locked)
        {
            float s = Math.Min(r.Width, r.Height);
            float ox = r.X + (r.Width - s) / 2f, oy = r.Y + (r.Height - s) / 2f;
            PointF P(float x, float y) => new(ox + x * s, oy + y * s);
            RectangleF R(float x, float y, float w, float h) => new(ox + x * s, oy + y * s, w * s, h * s);

            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, s * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var dark = new SolidBrush(DarkDetail);

            switch (icon)
            {
                case AchievementIcon.DriftCar:
                {
                    using (var lines = new Pen(Color.FromArgb(170, color), s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(lines, P(0.02f, 0.34f), P(0.2f, 0.34f));
                        g.DrawLine(lines, P(0.0f, 0.52f), P(0.16f, 0.52f));
                        g.DrawLine(lines, P(0.04f, 0.7f), P(0.2f, 0.7f));
                    }
                    var st = g.Save();
                    var c = P(0.58f, 0.52f);
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform(-22f);
                    float bw = s * 0.62f, bh = s * 0.34f;
                    using (var body = DrawingHelpers.RoundedRect(new RectangleF(-bw / 2f, -bh / 2f, bw, bh), bh * 0.35f, clampToRect: true))
                        g.FillPath(brush, body);
                    float ww = s * 0.14f, wh = s * 0.08f;
                    foreach (var (wx, wy) in new[] { (-0.2f, -0.21f), (0.14f, -0.21f), (-0.2f, 0.13f), (0.14f, 0.13f) })
                        g.FillRectangle(brush, wx * s, wy * s, ww, wh);
                    g.FillRectangle(dark, s * 0.02f, -bh * 0.32f, s * 0.12f, bh * 0.64f);
                    g.Restore(st);
                    break;
                }
                case AchievementIcon.Speedometer:
                {
                    g.DrawArc(pen, R(0.06f, 0.1f, 0.88f, 0.88f), 150f, 240f);
                    var c = P(0.5f, 0.54f);
                    double a = -40.0 * Math.PI / 180.0;
                    g.DrawLine(pen, c, new PointF(c.X + (float)Math.Cos(a) * s * 0.34f, c.Y + (float)Math.Sin(a) * s * 0.34f));
                    g.FillEllipse(brush, R(0.42f, 0.46f, 0.16f, 0.16f));
                    break;
                }
                case AchievementIcon.Rocket:
                {
                    var st = g.Save();
                    var c = P(0.5f, 0.5f);
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform(45f);
                    using (var body = new GraphicsPath())
                    {
                        body.AddBezier(0, -s * 0.46f, s * 0.2f, -s * 0.3f, s * 0.18f, s * 0.1f, s * 0.14f, s * 0.24f);
                        body.AddLine(s * 0.14f, s * 0.24f, -s * 0.14f, s * 0.24f);
                        body.AddBezier(-s * 0.14f, s * 0.24f, -s * 0.18f, s * 0.1f, -s * 0.2f, -s * 0.3f, 0, -s * 0.46f);
                        g.FillPath(brush, body);
                    }
                    g.FillPolygon(brush, new[] { new PointF(-s * 0.14f, s * 0.06f), new PointF(-s * 0.28f, s * 0.3f), new PointF(-s * 0.12f, s * 0.24f) });
                    g.FillPolygon(brush, new[] { new PointF(s * 0.14f, s * 0.06f), new PointF(s * 0.28f, s * 0.3f), new PointF(s * 0.12f, s * 0.24f) });
                    g.FillEllipse(dark, -s * 0.07f, -s * 0.16f, s * 0.14f, s * 0.14f);
                    using (var flame = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 150, 40)))
                        g.FillPolygon(flame, new[] { new PointF(-s * 0.09f, s * 0.27f), new PointF(0, s * 0.48f), new PointF(s * 0.09f, s * 0.27f) });
                    g.Restore(st);
                    break;
                }
                case AchievementIcon.Tachometer:
                {
                    g.DrawArc(pen, R(0.06f, 0.1f, 0.88f, 0.88f), 150f, 180f);
                    using (var red = new Pen(locked ? color : RedZone, s * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(red, R(0.06f, 0.1f, 0.88f, 0.88f), 335f, 55f);
                    var c = P(0.5f, 0.54f);
                    double a = 5.0 * Math.PI / 180.0;
                    g.DrawLine(pen, c, new PointF(c.X + (float)Math.Cos(a) * s * 0.34f, c.Y + (float)Math.Sin(a) * s * 0.34f));
                    g.FillEllipse(brush, R(0.42f, 0.46f, 0.16f, 0.16f));
                    break;
                }
                case AchievementIcon.Cone:
                {
                    var cone = new[] { P(0.43f, 0.06f), P(0.57f, 0.06f), P(0.74f, 0.8f), P(0.26f, 0.8f) };
                    g.FillPolygon(brush, cone);
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddPolygon(cone);
                        var st = g.Save();
                        g.SetClip(clip, CombineMode.Intersect);
                        g.FillRectangle(dark, R(0f, 0.32f, 1f, 0.1f));
                        g.FillRectangle(dark, R(0f, 0.56f, 1f, 0.1f));
                        g.Restore(st);
                    }
                    using (var basePath = DrawingHelpers.RoundedRect(R(0.14f, 0.8f, 0.72f, 0.12f), s * 0.04f, clampToRect: true))
                        g.FillPath(brush, basePath);
                    break;
                }
                case AchievementIcon.Heart:
                {
                    using var heart = new GraphicsPath();
                    heart.AddBezier(P(0.5f, 0.9f), P(0.12f, 0.62f), P(0.0f, 0.32f), P(0.24f, 0.16f));
                    heart.AddBezier(P(0.24f, 0.16f), P(0.38f, 0.07f), P(0.5f, 0.18f), P(0.5f, 0.3f));
                    heart.AddBezier(P(0.5f, 0.3f), P(0.5f, 0.18f), P(0.62f, 0.07f), P(0.76f, 0.16f));
                    heart.AddBezier(P(0.76f, 0.16f), P(1.0f, 0.32f), P(0.88f, 0.62f), P(0.5f, 0.9f));
                    heart.CloseFigure();
                    using var fill = new SolidBrush(locked ? color : HeartColor);
                    g.FillPath(fill, heart);
                    break;
                }
                case AchievementIcon.UpsideDown:
                {
                    g.DrawLine(pen, P(0.04f, 0.92f), P(0.96f, 0.92f));
                    { var w = R(0.16f, 0.2f, 0.22f, 0.22f); g.FillPie(brush, w.X, w.Y, w.Width, w.Height, 180f, 180f); }
                    { var w = R(0.62f, 0.2f, 0.22f, 0.22f); g.FillPie(brush, w.X, w.Y, w.Width, w.Height, 180f, 180f); }
                    using (var body = DrawingHelpers.RoundedRect(R(0.06f, 0.32f, 0.88f, 0.22f), s * 0.07f, clampToRect: true))
                        g.FillPath(brush, body);
                    g.FillPolygon(brush, new[] { P(0.24f, 0.53f), P(0.76f, 0.53f), P(0.64f, 0.82f), P(0.36f, 0.82f) });
                    g.FillPolygon(dark, new[] { P(0.31f, 0.58f), P(0.48f, 0.58f), P(0.48f, 0.76f), P(0.39f, 0.76f) });
                    g.FillPolygon(dark, new[] { P(0.52f, 0.58f), P(0.69f, 0.58f), P(0.61f, 0.76f), P(0.52f, 0.76f) });
                    break;
                }
                case AchievementIcon.Reset:
                {
                    var arc = R(0.18f, 0.2f, 0.64f, 0.64f);
                    const float start = 30f, sweep = 290f;
                    g.DrawArc(pen, arc, start, sweep);
                    double end = (start + sweep) * Math.PI / 180.0;
                    float cx = arc.X + arc.Width / 2f, cy = arc.Y + arc.Height / 2f, rad = arc.Width / 2f;
                    var tip0 = new PointF(cx + (float)Math.Cos(end) * rad, cy + (float)Math.Sin(end) * rad);
                    var dir = new PointF(-(float)Math.Sin(end), (float)Math.Cos(end));
                    var nrm = new PointF((float)Math.Cos(end), (float)Math.Sin(end));
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(tip0.X + dir.X * s * 0.16f, tip0.Y + dir.Y * s * 0.16f),
                        new PointF(tip0.X + nrm.X * s * 0.13f, tip0.Y + nrm.Y * s * 0.13f),
                        new PointF(tip0.X - nrm.X * s * 0.13f, tip0.Y - nrm.Y * s * 0.13f),
                    });
                    break;
                }
                case AchievementIcon.Wings:
                {
                    using (var ring = new Pen(color, s * 0.08f))
                        g.DrawEllipse(ring, R(0.37f, 0.42f, 0.26f, 0.26f));
                    for (int side = 0; side < 2; side++)
                    {
                        float M(float x) => side == 0 ? x : 1f - x;
                        using var wing = new GraphicsPath();
                        wing.AddBezier(P(M(0.36f), 0.52f), P(M(0.26f), 0.3f), P(M(0.12f), 0.22f), P(M(0.0f), 0.26f));
                        wing.AddLine(P(M(0.0f), 0.26f), P(M(0.1f), 0.38f));
                        wing.AddLine(P(M(0.1f), 0.38f), P(M(0.03f), 0.44f));
                        wing.AddLine(P(M(0.03f), 0.44f), P(M(0.15f), 0.52f));
                        wing.AddLine(P(M(0.15f), 0.52f), P(M(0.09f), 0.6f));
                        wing.AddLine(P(M(0.09f), 0.6f), P(M(0.36f), 0.6f));
                        wing.CloseFigure();
                        g.FillPath(brush, wing);
                    }
                    break;
                }
                case AchievementIcon.Owl:
                {
                    g.FillPolygon(brush, new[] { P(0.22f, 0.3f), P(0.26f, 0.06f), P(0.4f, 0.22f) });
                    g.FillPolygon(brush, new[] { P(0.78f, 0.3f), P(0.74f, 0.06f), P(0.6f, 0.22f) });
                    g.FillEllipse(brush, R(0.18f, 0.16f, 0.64f, 0.8f));
                    g.FillEllipse(dark, R(0.25f, 0.3f, 0.23f, 0.23f));
                    g.FillEllipse(dark, R(0.52f, 0.3f, 0.23f, 0.23f));
                    g.FillEllipse(brush, R(0.33f, 0.38f, 0.07f, 0.07f));
                    g.FillEllipse(brush, R(0.6f, 0.38f, 0.07f, 0.07f));
                    g.FillPolygon(dark, new[] { P(0.45f, 0.55f), P(0.55f, 0.55f), P(0.5f, 0.65f) });
                    break;
                }
                case AchievementIcon.Donut:
                {
                    using (var ring = new GraphicsPath())
                    {
                        ring.AddEllipse(R(0.06f, 0.1f, 0.88f, 0.84f));
                        ring.AddEllipse(R(0.36f, 0.38f, 0.28f, 0.26f));
                        ring.FillMode = FillMode.Alternate;
                        g.FillPath(brush, ring);
                    }
                    using (var icing = new GraphicsPath())
                    {
                        icing.AddEllipse(R(0.14f, 0.16f, 0.72f, 0.66f));
                        icing.AddEllipse(R(0.33f, 0.35f, 0.34f, 0.32f));
                        icing.FillMode = FillMode.Alternate;
                        using var icingBrush = new SolidBrush(locked ? Color.FromArgb(255, 150, 150, 156) : Color.FromArgb(255, 255, 132, 184));
                        g.FillPath(icingBrush, icing);
                    }
                    var sprinkleColors = new[] { Color.FromArgb(255, 255, 230, 90), Color.FromArgb(255, 110, 220, 255), Color.FromArgb(255, 140, 255, 150) };
                    var sprinkles = new[] { (0.26f, 0.3f, 0.34f, 0.26f), (0.62f, 0.22f, 0.7f, 0.26f), (0.74f, 0.46f, 0.78f, 0.54f), (0.6f, 0.7f, 0.68f, 0.72f), (0.28f, 0.66f, 0.34f, 0.72f), (0.2f, 0.46f, 0.22f, 0.54f) };
                    for (int i = 0; i < sprinkles.Length; i++)
                    {
                        using var sp = new Pen(locked ? DarkDetail : sprinkleColors[i % sprinkleColors.Length], s * 0.045f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        var (x1, y1, x2, y2) = sprinkles[i];
                        g.DrawLine(sp, P(x1, y1), P(x2, y2));
                    }
                    break;
                }
                case AchievementIcon.Protractor:
                {
                    g.DrawArc(pen, R(0.06f, 0.2f, 0.88f, 0.88f), 180f, 180f);
                    g.DrawLine(pen, P(0.04f, 0.64f), P(0.96f, 0.64f));
                    g.DrawLine(pen, P(0.5f, 0.64f), P(0.5f, 0.24f));
                    using (var thin = new Pen(color, s * 0.06f))
                        g.DrawLines(thin, new[] { P(0.5f, 0.5f), P(0.64f, 0.5f), P(0.64f, 0.64f) });
                    break;
                }
                case AchievementIcon.ReverseArrow:
                {
                    using (var shaft = new Pen(color, s * 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                        g.DrawLine(shaft, P(0.86f, 0.62f), P(0.34f, 0.62f));
                    g.FillPolygon(brush, new[] { P(0.08f, 0.62f), P(0.38f, 0.38f), P(0.38f, 0.86f) });
                    Font font = GlyphFont(s * 0.36f);
                    var st = g.Save();
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString("R", font, brush, R(0.52f, 0.02f, 0.4f, 0.4f), CenterFormat);
                    g.Restore(st);
                    break;
                }
                case AchievementIcon.Cannonball:
                {
                    using (var lines = new Pen(Color.FromArgb(170, color), s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(lines, P(0.02f, 0.4f), P(0.24f, 0.4f));
                        g.DrawLine(lines, P(0.0f, 0.56f), P(0.2f, 0.56f));
                        g.DrawLine(lines, P(0.04f, 0.72f), P(0.24f, 0.72f));
                    }
                    g.FillEllipse(brush, R(0.3f, 0.22f, 0.64f, 0.64f));
                    g.FillEllipse(dark, R(0.44f, 0.34f, 0.12f, 0.12f));
                    break;
                }
                case AchievementIcon.Bricks:
                {
                    var wall = R(0.04f, 0.16f, 0.92f, 0.7f);
                    g.FillRectangle(brush, wall);
                    using var mortar = new Pen(DarkDetail, s * 0.05f);
                    for (int row = 1; row < 3; row++)
                        g.DrawLine(mortar, P(0.04f, 0.16f + row * 0.2333f), P(0.96f, 0.16f + row * 0.2333f));
                    for (int row = 0; row < 3; row++)
                    {
                        float offset = row % 2 == 0 ? 0.35f : 0.2f;
                        for (float x = offset; x < 0.96f; x += 0.3f)
                            g.DrawLine(mortar, P(x, 0.16f + row * 0.2333f), P(x, 0.16f + (row + 1) * 0.2333f));
                    }
                    using (var crack = new Pen(DarkDetail, s * 0.035f) { LineJoin = LineJoin.Round })
                        g.DrawLines(crack, new[] { P(0.62f, 0.2f), P(0.54f, 0.36f), P(0.64f, 0.48f), P(0.56f, 0.66f) });
                    break;
                }
                case AchievementIcon.Handbrake:
                {
                    using (var basePath = DrawingHelpers.RoundedRect(R(0.14f, 0.74f, 0.72f, 0.14f), s * 0.05f, clampToRect: true))
                        g.FillPath(brush, basePath);
                    using (var arm = new Pen(color, s * 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(arm, P(0.34f, 0.76f), P(0.66f, 0.26f));
                    using (var grip = new Pen(color, s * 0.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(grip, P(0.6f, 0.36f), P(0.72f, 0.17f));
                    g.FillEllipse(dark, R(0.69f, 0.1f, 0.08f, 0.08f));
                    break;
                }
                case AchievementIcon.Speaker:
                {
                    g.FillPolygon(brush, new[] { P(0.06f, 0.38f), P(0.24f, 0.38f), P(0.46f, 0.18f), P(0.46f, 0.82f), P(0.24f, 0.62f), P(0.06f, 0.62f) });
                    using var wave = new Pen(color, s * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(wave, R(0.36f, 0.34f, 0.3f, 0.32f), -50f, 100f);
                    g.DrawArc(wave, R(0.3f, 0.22f, 0.5f, 0.56f), -50f, 100f);
                    g.DrawArc(wave, R(0.24f, 0.1f, 0.7f, 0.8f), -50f, 100f);
                    break;
                }
                case AchievementIcon.Blinker:
                {
                    using var amber = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 176, 40));
                    g.FillPolygon(amber, new[] { P(0.08f, 0.5f), P(0.46f, 0.16f), P(0.46f, 0.36f), P(0.76f, 0.36f), P(0.76f, 0.64f), P(0.46f, 0.64f), P(0.46f, 0.84f) });
                    using var rays = new Pen(locked ? color : Color.FromArgb(255, 255, 176, 40), s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLine(rays, P(0.86f, 0.3f), P(0.96f, 0.22f));
                    g.DrawLine(rays, P(0.88f, 0.5f), P(0.98f, 0.5f));
                    g.DrawLine(rays, P(0.86f, 0.7f), P(0.96f, 0.78f));
                    break;
                }
                case AchievementIcon.Lightning:
                {
                    using var bolt = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 214, 64));
                    g.FillPolygon(bolt, new[] { P(0.6f, 0.02f), P(0.2f, 0.56f), P(0.46f, 0.56f), P(0.36f, 0.98f), P(0.8f, 0.4f), P(0.54f, 0.4f), P(0.68f, 0.02f) });
                    break;
                }
                case AchievementIcon.Barrel:
                {
                    var arc = R(0.06f, 0.06f, 0.88f, 0.88f);
                    const float start = 200f, sweep = 300f;
                    using (var ring = new Pen(color, s * 0.08f) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                        g.DrawArc(ring, arc, start, sweep);
                    double end = (start + sweep) * Math.PI / 180.0;
                    float cx = arc.X + arc.Width / 2f, cy = arc.Y + arc.Height / 2f, rad = arc.Width / 2f;
                    var tip0 = new PointF(cx + (float)Math.Cos(end) * rad, cy + (float)Math.Sin(end) * rad);
                    var dir = new PointF(-(float)Math.Sin(end), (float)Math.Cos(end));
                    var nrm = new PointF((float)Math.Cos(end), (float)Math.Sin(end));
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(tip0.X + dir.X * s * 0.14f, tip0.Y + dir.Y * s * 0.14f),
                        new PointF(tip0.X + nrm.X * s * 0.11f, tip0.Y + nrm.Y * s * 0.11f),
                        new PointF(tip0.X - nrm.X * s * 0.11f, tip0.Y - nrm.Y * s * 0.11f),
                    });
                    var st = g.Save();
                    var c = P(0.5f, 0.5f);
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform(35f);
                    using (var body = DrawingHelpers.RoundedRect(new RectangleF(-s * 0.24f, -s * 0.1f, s * 0.48f, s * 0.18f), s * 0.05f, clampToRect: true))
                        g.FillPath(brush, body);
                    g.FillEllipse(brush, -s * 0.18f, s * 0.04f, s * 0.12f, s * 0.12f);
                    g.FillEllipse(brush, s * 0.06f, s * 0.04f, s * 0.12f, s * 0.12f);
                    g.Restore(st);
                    break;
                }
                case AchievementIcon.Smoke:
                {
                    using (var puff = new SolidBrush(Color.FromArgb(locked ? 255 : 230, color)))
                    {
                        g.FillEllipse(puff, R(0.3f, 0.12f, 0.36f, 0.34f));
                        g.FillEllipse(puff, R(0.52f, 0.22f, 0.4f, 0.36f));
                        g.FillEllipse(puff, R(0.2f, 0.3f, 0.4f, 0.34f));
                        g.FillEllipse(puff, R(0.44f, 0.4f, 0.34f, 0.26f));
                    }
                    using (var tyre = new Pen(color, s * 0.1f))
                        g.DrawEllipse(tyre, R(0.06f, 0.56f, 0.36f, 0.36f));
                    g.FillEllipse(brush, R(0.19f, 0.69f, 0.1f, 0.1f));
                    break;
                }
                case AchievementIcon.Stopwatch:
                {
                    g.FillRectangle(brush, R(0.42f, 0.04f, 0.16f, 0.1f));
                    using (var ring = new Pen(color, s * 0.09f))
                        g.DrawEllipse(ring, R(0.14f, 0.16f, 0.72f, 0.72f));
                    var c = P(0.5f, 0.52f);
                    g.DrawLine(pen, c, P(0.5f, 0.3f));
                    g.DrawLine(pen, c, P(0.66f, 0.6f));
                    g.FillEllipse(brush, R(0.45f, 0.47f, 0.1f, 0.1f));
                    break;
                }
                case AchievementIcon.Carousel:
                {
                    g.FillPolygon(brush, new[] { P(0.5f, 0.04f), P(0.94f, 0.34f), P(0.06f, 0.34f) });
                    g.FillEllipse(brush, R(0.47f, 0.0f, 0.06f, 0.06f));
                    using (var poles = new Pen(color, s * 0.05f))
                    {
                        g.DrawLine(poles, P(0.2f, 0.34f), P(0.2f, 0.8f));
                        g.DrawLine(poles, P(0.5f, 0.34f), P(0.5f, 0.8f));
                        g.DrawLine(poles, P(0.8f, 0.34f), P(0.8f, 0.8f));
                    }
                    g.FillEllipse(brush, R(0.14f, 0.5f, 0.12f, 0.1f));
                    g.FillEllipse(brush, R(0.44f, 0.58f, 0.12f, 0.1f));
                    g.FillEllipse(brush, R(0.74f, 0.46f, 0.12f, 0.1f));
                    using (var platform = DrawingHelpers.RoundedRect(R(0.04f, 0.8f, 0.92f, 0.12f), s * 0.05f, clampToRect: true))
                        g.FillPath(brush, platform);
                    break;
                }
                case AchievementIcon.SmokeDrift:
                {
                    using (var puff = new SolidBrush(Color.FromArgb(locked ? 255 : 210, color)))
                    {
                        g.FillEllipse(puff, R(0.02f, 0.44f, 0.3f, 0.28f));
                        g.FillEllipse(puff, R(0.14f, 0.32f, 0.3f, 0.3f));
                        g.FillEllipse(puff, R(0.18f, 0.54f, 0.28f, 0.26f));
                    }
                    using (var curve = new Pen(color, s * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                        g.DrawBezier(curve, P(0.42f, 0.62f), P(0.58f, 0.62f), P(0.66f, 0.46f), P(0.7f, 0.3f));
                    g.FillPolygon(brush, new[] { P(0.62f, 0.3f), P(0.82f, 0.3f), P(0.7f, 0.1f) });
                    break;
                }
                case AchievementIcon.Planet:
                {
                    g.FillEllipse(brush, R(0.24f, 0.24f, 0.52f, 0.52f));
                    using (var ring = new Pen(locked ? color : Color.FromArgb(255, 255, 196, 90), s * 0.07f))
                        g.DrawEllipse(ring, R(0.02f, 0.4f, 0.96f, 0.22f));
                    g.FillEllipse(dark, R(0.36f, 0.34f, 0.1f, 0.1f));
                    g.FillEllipse(dark, R(0.54f, 0.52f, 0.07f, 0.07f));
                    break;
                }
                case AchievementIcon.Airplane:
                {
                    var st = g.Save();
                    var c = P(0.5f, 0.5f);
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform(-35f);
                    using (var body = DrawingHelpers.RoundedRect(new RectangleF(-s * 0.46f, -s * 0.07f, s * 0.92f, s * 0.14f), s * 0.07f, clampToRect: true))
                        g.FillPath(brush, body);
                    g.FillPolygon(brush, new[] { new PointF(-s * 0.06f, 0), new PointF(-s * 0.2f, -s * 0.42f), new PointF(s * 0.06f, -s * 0.42f), new PointF(s * 0.16f, 0) });
                    g.FillPolygon(brush, new[] { new PointF(-s * 0.06f, 0), new PointF(-s * 0.2f, s * 0.42f), new PointF(s * 0.06f, s * 0.42f), new PointF(s * 0.16f, 0) });
                    g.FillPolygon(brush, new[] { new PointF(-s * 0.36f, 0), new PointF(-s * 0.46f, -s * 0.18f), new PointF(-s * 0.34f, -s * 0.18f), new PointF(-s * 0.26f, 0) });
                    g.FillPolygon(brush, new[] { new PointF(-s * 0.36f, 0), new PointF(-s * 0.46f, s * 0.18f), new PointF(-s * 0.34f, s * 0.18f), new PointF(-s * 0.26f, 0) });
                    g.Restore(st);
                    break;
                }
                case AchievementIcon.Cat:
                {
                    g.FillPolygon(brush, new[] { P(0.14f, 0.42f), P(0.2f, 0.06f), P(0.42f, 0.26f) });
                    g.FillPolygon(brush, new[] { P(0.86f, 0.42f), P(0.8f, 0.06f), P(0.58f, 0.26f) });
                    g.FillEllipse(brush, R(0.12f, 0.2f, 0.76f, 0.7f));
                    g.FillEllipse(dark, R(0.3f, 0.44f, 0.12f, 0.14f));
                    g.FillEllipse(dark, R(0.58f, 0.44f, 0.12f, 0.14f));
                    g.FillPolygon(dark, new[] { P(0.46f, 0.64f), P(0.54f, 0.64f), P(0.5f, 0.69f) });
                    using (var whisker = new Pen(DarkDetail, s * 0.025f))
                    {
                        g.DrawLine(whisker, P(0.16f, 0.66f), P(0.36f, 0.68f));
                        g.DrawLine(whisker, P(0.64f, 0.68f), P(0.84f, 0.66f));
                    }
                    break;
                }
                case AchievementIcon.ConeStack:
                {
                    foreach (var (cx, cy, k) in new[] { (0.26f, 0.42f, 0.55f), (0.74f, 0.42f, 0.55f), (0.5f, 0.28f, 0.7f) })
                    {
                        float h = k, w = k * 0.62f;
                        var cone = new[] { P(cx - w * 0.14f, cy - h * 0.2f), P(cx + w * 0.14f, cy - h * 0.2f), P(cx + w * 0.5f, cy + h * 0.72f), P(cx - w * 0.5f, cy + h * 0.72f) };
                        g.FillPolygon(brush, cone);
                        using var stripe = new Pen(DarkDetail, s * 0.05f);
                        g.DrawLine(stripe, P(cx - w * 0.3f, cy + h * 0.28f), P(cx + w * 0.3f, cy + h * 0.28f));
                    }
                    using (var basePath = DrawingHelpers.RoundedRect(R(0.04f, 0.86f, 0.92f, 0.08f), s * 0.03f, clampToRect: true))
                        g.FillPath(brush, basePath);
                    break;
                }
                case AchievementIcon.TyreStack:
                {
                    for (int i = 2; i >= 0; i--)
                    {
                        float y = 0.08f + i * 0.26f;
                        g.FillRectangle(brush, R(0.12f, y + 0.1f, 0.76f, 0.12f));
                        g.FillEllipse(brush, R(0.12f, y + 0.12f, 0.76f, 0.2f));
                        g.FillEllipse(brush, R(0.12f, y, 0.76f, 0.2f));
                        g.FillEllipse(dark, R(0.34f, y + 0.05f, 0.32f, 0.1f));
                        using var groove = new Pen(DarkDetail, s * 0.025f);
                        g.DrawArc(groove, R(0.12f, y + 0.12f, 0.76f, 0.2f), 20f, 140f);
                    }
                    break;
                }
                case AchievementIcon.Barrier:
                {
                    g.FillRectangle(brush, R(0.04f, 0.3f, 0.92f, 0.14f));
                    g.FillRectangle(brush, R(0.04f, 0.5f, 0.92f, 0.08f));
                    g.FillRectangle(brush, R(0.14f, 0.3f, 0.08f, 0.6f));
                    g.FillRectangle(brush, R(0.46f, 0.3f, 0.08f, 0.6f));
                    g.FillRectangle(brush, R(0.78f, 0.3f, 0.08f, 0.6f));
                    using (var dent = new Pen(DarkDetail, s * 0.04f))
                        g.DrawLines(dent, new[] { P(0.28f, 0.32f), P(0.34f, 0.4f), P(0.4f, 0.32f) });
                    break;
                }
                case AchievementIcon.RoadSign:
                {
                    g.FillRectangle(brush, R(0.46f, 0.5f, 0.08f, 0.46f));
                    g.FillPolygon(brush, new[] { P(0.5f, 0.02f), P(0.92f, 0.62f), P(0.08f, 0.62f) });
                    g.FillPolygon(dark, new[] { P(0.5f, 0.16f), P(0.78f, 0.56f), P(0.22f, 0.56f) });
                    g.FillRectangle(brush, R(0.47f, 0.28f, 0.06f, 0.16f));
                    g.FillEllipse(brush, R(0.47f, 0.47f, 0.06f, 0.06f));
                    break;
                }
                case AchievementIcon.Bin:
                {
                    g.FillRectangle(brush, R(0.16f, 0.16f, 0.68f, 0.1f));
                    g.FillRectangle(brush, R(0.4f, 0.06f, 0.2f, 0.08f));
                    g.FillPolygon(brush, new[] { P(0.2f, 0.3f), P(0.8f, 0.3f), P(0.72f, 0.94f), P(0.28f, 0.94f) });
                    using var rib = new Pen(DarkDetail, s * 0.05f);
                    g.DrawLine(rib, P(0.38f, 0.4f), P(0.4f, 0.84f));
                    g.DrawLine(rib, P(0.5f, 0.4f), P(0.5f, 0.84f));
                    g.DrawLine(rib, P(0.62f, 0.4f), P(0.6f, 0.84f));
                    break;
                }
                case AchievementIcon.HayBale:
                {
                    using var hay = new SolidBrush(locked ? color : Color.FromArgb(255, 238, 206, 110));
                    using (var bale = DrawingHelpers.RoundedRect(R(0.06f, 0.24f, 0.88f, 0.56f), s * 0.1f, clampToRect: true))
                        g.FillPath(hay, bale);
                    using var straw = new Pen(Color.FromArgb(150, DarkDetail), s * 0.03f);
                    for (int i = 0; i < 4; i++)
                        g.DrawLine(straw, P(0.14f, 0.34f + i * 0.12f), P(0.86f, 0.34f + i * 0.12f));
                    using var tie = new Pen(DarkDetail, s * 0.05f);
                    g.DrawLine(tie, P(0.34f, 0.24f), P(0.34f, 0.8f));
                    g.DrawLine(tie, P(0.66f, 0.24f), P(0.66f, 0.8f));
                    break;
                }
                case AchievementIcon.Ambulance:
                {
                    using (var body = DrawingHelpers.RoundedRect(R(0.04f, 0.28f, 0.66f, 0.46f), s * 0.05f, clampToRect: true))
                        g.FillPath(brush, body);
                    g.FillPolygon(brush, new[] { P(0.68f, 0.4f), P(0.86f, 0.4f), P(0.96f, 0.56f), P(0.96f, 0.74f), P(0.68f, 0.74f) });
                    g.FillEllipse(brush, R(0.14f, 0.66f, 0.18f, 0.18f));
                    g.FillEllipse(brush, R(0.7f, 0.66f, 0.18f, 0.18f));
                    g.FillEllipse(dark, R(0.19f, 0.71f, 0.08f, 0.08f));
                    g.FillEllipse(dark, R(0.75f, 0.71f, 0.08f, 0.08f));
                    using (var cross = new SolidBrush(locked ? DarkDetail : Color.FromArgb(255, 235, 60, 60)))
                    {
                        g.FillRectangle(cross, R(0.3f, 0.36f, 0.12f, 0.3f));
                        g.FillRectangle(cross, R(0.21f, 0.45f, 0.3f, 0.12f));
                    }
                    g.FillRectangle(dark, R(0.74f, 0.44f, 0.1f, 0.1f));
                    break;
                }
                case AchievementIcon.BowlingPins:
                {
                    foreach (var (cx, cy) in new[] { (0.3f, 0.14f), (0.7f, 0.14f), (0.5f, 0.3f) })
                    {
                        g.FillEllipse(brush, R(cx - 0.07f, cy, 0.14f, 0.14f));
                        g.FillEllipse(brush, R(cx - 0.11f, cy + 0.16f, 0.22f, 0.36f));
                        using var band = new Pen(locked ? DarkDetail : Color.FromArgb(255, 235, 60, 60), s * 0.035f);
                        g.DrawLine(band, P(cx - 0.06f, cy + 0.15f), P(cx + 0.06f, cy + 0.15f));
                    }
                    g.FillEllipse(dark, R(0.36f, 0.66f, 0.3f, 0.3f));
                    g.FillEllipse(brush, R(0.38f, 0.68f, 0.26f, 0.26f));
                    g.FillEllipse(dark, R(0.45f, 0.73f, 0.04f, 0.04f));
                    g.FillEllipse(dark, R(0.52f, 0.73f, 0.04f, 0.04f));
                    break;
                }
                case AchievementIcon.Medal:
                {
                    using (var ribbon = new SolidBrush(locked ? color : Color.FromArgb(255, 64, 156, 255)))
                    {
                        g.FillPolygon(ribbon, new[] { P(0.26f, 0.0f), P(0.44f, 0.0f), P(0.56f, 0.34f), P(0.4f, 0.4f) });
                        g.FillPolygon(ribbon, new[] { P(0.74f, 0.0f), P(0.56f, 0.0f), P(0.44f, 0.34f), P(0.6f, 0.4f) });
                    }
                    g.FillEllipse(brush, R(0.2f, 0.32f, 0.6f, 0.6f));
                    DrawGlyphText(g, "100K", R(0.2f, 0.32f, 0.6f, 0.6f), s * 0.17f, DarkDetail);
                    break;
                }
                case AchievementIcon.Trophy:
                {
                    using var gold = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 204, 64));
                    using var handle = new Pen(locked ? color : Color.FromArgb(255, 255, 204, 64), s * 0.07f);
                    g.DrawArc(handle, R(0.08f, 0.1f, 0.3f, 0.34f), 90f, 180f);
                    g.DrawArc(handle, R(0.62f, 0.1f, 0.3f, 0.34f), 270f, 180f);
                    g.FillPolygon(gold, new[] { P(0.2f, 0.06f), P(0.8f, 0.06f), P(0.72f, 0.42f), P(0.58f, 0.56f), P(0.42f, 0.56f), P(0.28f, 0.42f) });
                    g.FillRectangle(gold, R(0.44f, 0.54f, 0.12f, 0.2f));
                    g.FillRectangle(gold, R(0.26f, 0.74f, 0.48f, 0.12f));
                    DrawGlyphText(g, "250K", R(0.2f, 0.08f, 0.6f, 0.36f), s * 0.14f, DarkDetail);
                    break;
                }
                case AchievementIcon.ComboMax:
                {
                    DrawGlyphText(g, "x10", R(-0.1f, 0.08f, 1.2f, 0.84f), s * 0.42f, locked ? color : Color.FromArgb(255, 255, 214, 64));
                    break;
                }
                case AchievementIcon.Crown:
                {
                    using var gold = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 204, 64));
                    g.FillPolygon(gold, new[] { P(0.08f, 0.76f), P(0.08f, 0.26f), P(0.3f, 0.5f), P(0.5f, 0.14f), P(0.7f, 0.5f), P(0.92f, 0.26f), P(0.92f, 0.76f) });
                    g.FillRectangle(gold, R(0.08f, 0.8f, 0.84f, 0.1f));
                    g.FillEllipse(dark, R(0.44f, 0.56f, 0.12f, 0.12f));
                    break;
                }
                case AchievementIcon.BonusStack:
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float x = 0.06f + (i % 3) * 0.3f + (i >= 3 ? 0.15f : 0f);
                        float y = i >= 3 ? 0.56f : 0.22f;
                        using var box = DrawingHelpers.RoundedRect(R(x, y, 0.26f, 0.22f), s * 0.04f, clampToRect: true);
                        using var fill = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 214, 64));
                        g.FillPath(fill, box);
                    }
                    break;
                }
                case AchievementIcon.PitSign:
                {
                    using (var sign = DrawingHelpers.RoundedRect(R(0.12f, 0.08f, 0.76f, 0.84f), s * 0.12f, clampToRect: true))
                    using (var blue = new SolidBrush(locked ? color : Color.FromArgb(255, 40, 110, 230)))
                        g.FillPath(blue, sign);
                    DrawGlyphText(g, "P", R(0.12f, 0.08f, 0.76f, 0.84f), s * 0.62f, locked ? DarkDetail : Color.White);
                    break;
                }
                case AchievementIcon.Pileup:
                {
                    using var burst = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 150, 40));
                    var pts = new PointF[16];
                    for (int i = 0; i < 16; i++)
                    {
                        double a = i * Math.PI / 8.0;
                        float spike = i % 2 == 0 ? 0.46f : 0.26f;
                        pts[i] = P(0.5f + (float)Math.Cos(a) * spike, 0.5f + (float)Math.Sin(a) * spike);
                    }
                    g.FillPolygon(burst, pts);
                    DrawGlyphText(g, "3", R(0.2f, 0.2f, 0.6f, 0.6f), s * 0.36f, DarkDetail);
                    break;
                }
                case AchievementIcon.Warning:
                {
                    using var amber = new SolidBrush(locked ? color : Color.FromArgb(255, 255, 196, 40));
                    g.FillPolygon(amber, new[] { P(0.5f, 0.04f), P(0.96f, 0.9f), P(0.04f, 0.9f) });
                    g.FillRectangle(dark, R(0.45f, 0.32f, 0.1f, 0.32f));
                    g.FillEllipse(dark, R(0.45f, 0.7f, 0.1f, 0.1f));
                    break;
                }
                case AchievementIcon.WrongWay:
                {
                    using (var red = new SolidBrush(locked ? color : Color.FromArgb(255, 220, 40, 40)))
                        g.FillEllipse(red, R(0.04f, 0.04f, 0.92f, 0.92f));
                    g.FillRectangle(locked ? dark : Brushes.White, R(0.18f, 0.42f, 0.64f, 0.16f));
                    break;
                }
                case AchievementIcon.Question:
                {
                    Font font = GlyphFont(s * 0.82f);
                    var st = g.Save();
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString("?", font, brush, new RectangleF(ox, oy + s * 0.04f, s, s), CenterFormat);
                    g.Restore(st);
                    break;
                }
            }
        }

        private static void DrawGlyphText(Graphics g, string text, RectangleF area, float sizePx, Color color)
        {
            Font font = GlyphFont(sizePx);
            using var brush = new SolidBrush(color);
            var st = g.Save();
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(text, font, brush, area, CenterNoWrapFormat);
            g.Restore(st);
        }

        public static void DrawMethodGlyph(Graphics g, RectangleF r, InputMethod method, Color color)
        {
            float s = Math.Min(r.Width, r.Height);
            float ox = r.X + (r.Width - s) / 2f, oy = r.Y + (r.Height - s) / 2f;
            PointF P(float x, float y) => new(ox + x * s, oy + y * s);
            RectangleF R(float x, float y, float w, float h) => new(ox + x * s, oy + y * s, w * s, h * s);

            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, Math.Max(1.2f, s * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            switch (method)
            {
                case InputMethod.Wheel:
                    g.DrawEllipse(pen, R(0.08f, 0.08f, 0.84f, 0.84f));
                    g.FillEllipse(brush, R(0.38f, 0.38f, 0.24f, 0.24f));
                    g.DrawLine(pen, P(0.12f, 0.5f), P(0.4f, 0.5f));
                    g.DrawLine(pen, P(0.6f, 0.5f), P(0.88f, 0.5f));
                    g.DrawLine(pen, P(0.5f, 0.6f), P(0.5f, 0.88f));
                    break;
                case InputMethod.Pad:
                    using (var body = DrawingHelpers.RoundedRect(R(0.06f, 0.26f, 0.88f, 0.36f), s * 0.14f, clampToRect: true))
                        g.FillPath(brush, body);
                    g.FillEllipse(brush, R(0.06f, 0.4f, 0.3f, 0.42f));
                    g.FillEllipse(brush, R(0.64f, 0.4f, 0.3f, 0.42f));
                    using (var hole = new SolidBrush(DarkDetail))
                    {
                        g.FillRectangle(hole, R(0.2f, 0.4f, 0.14f, 0.05f));
                        g.FillRectangle(hole, R(0.245f, 0.355f, 0.05f, 0.14f));
                        g.FillEllipse(hole, R(0.66f, 0.34f, 0.08f, 0.08f));
                        g.FillEllipse(hole, R(0.74f, 0.42f, 0.08f, 0.08f));
                    }
                    break;
                default:
                    using (var outline = new Pen(color, Math.Max(1.2f, s * 0.08f)))
                    using (var frame = DrawingHelpers.RoundedRect(R(0.04f, 0.24f, 0.92f, 0.52f), s * 0.08f, clampToRect: true))
                        g.DrawPath(outline, frame);
                    for (int row = 0; row < 2; row++)
                        for (int col = 0; col < 5; col++)
                            g.FillRectangle(brush, R(0.13f + col * 0.155f, 0.34f + row * 0.13f, 0.09f, 0.08f));
                    g.FillRectangle(brush, R(0.28f, 0.6f, 0.44f, 0.07f));
                    break;
            }
            g.Restore(state);
        }
    }
}
