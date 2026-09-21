using System.Collections.Generic;
using System.Drawing;

namespace LFSDriftTools
{
    public static class UiFonts
    {
        private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> Cache = new();

        public static Font Get(string family, float size, FontStyle style = FontStyle.Regular)
        {
            lock (Cache)
            {
                var key = (family, size, style);
                if (!Cache.TryGetValue(key, out var font))
                {
                    font = new Font(family, size, style);
                    Cache[key] = font;
                }
                return font;
            }
        }
    }

    public static class UiColors
    {
        public static readonly Color Muted = Color.FromArgb(140, 140, 170);
        public static readonly Color LabelDefault = Color.FromArgb(60, 60, 60);
    }
}