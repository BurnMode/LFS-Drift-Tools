using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public static class FontLibrary
    {
        public const string ActiveFontFile = "active.otf";
        private const string FallbackFamilyName = "Segoe UI";

        private static readonly object Sync = new();
        private static readonly Dictionary<string, FontFamily?> Families = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PrivateFontCollection> Collections = new();

        public static FontFamily? Load(string fileName)
        {
            lock (Sync)
            {
                if (Families.TryGetValue(fileName, out FontFamily? cached))
                    return cached;

                FontFamily? family = null;
                try
                {
                    string path = Path.Combine(Application.StartupPath, "Data", "Fonts", fileName);
                    if (File.Exists(path))
                    {
                        var collection = new PrivateFontCollection();
                        collection.AddFontFile(path);
                        if (collection.Families.Length > 0)
                        {
                            Collections.Add(collection);
                            family = collection.Families[0];
                        }
                        else
                        {
                            collection.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Font load " + fileName, ex);
                }

                Families[fileName] = family;
                return family;
            }
        }

        public static FontFamily ActiveOrFallback => Load(ActiveFontFile) ?? new FontFamily(FallbackFamilyName);

        public static Font CreateActiveFont(float size, FontStyle style)
        {
            FontFamily? active = Load(ActiveFontFile);
            return active != null ? new Font(active, size, style) : new Font(FallbackFamilyName, size, style);
        }
    }
}
