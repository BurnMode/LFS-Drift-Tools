using System.IO;
using System.Text.Json;

namespace LFSDriftTools
{
    public static class ConfigVersions
    {
        public const int Current = 1;
        public const string PropertyName = "ConfigVersion";

        public static bool IsOutdated(string path)
        {
            if (!File.Exists(path)) return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                return !(doc.RootElement.TryGetProperty(PropertyName, out JsonElement version) &&
                    version.ValueKind == JsonValueKind.Number &&
                    version.TryGetInt32(out int value) &&
                    value >= Current);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string ArchivePath(string path, string stamp) => path + ".old_" + stamp;
    }
}
