using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LFSDriftTools
{
    public static class LogRetention
    {
        public const int MaxRecordings = 30;
        public const long MaxBytes = 200L * 1024 * 1024;
        private const string SummarySuffix = "_summary";

        public static void PruneBeforeNewRecording(string directory, params string[] patterns)
        {
            try
            {
                if (!Directory.Exists(directory)) return;

                var dir = new DirectoryInfo(directory);
                var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (string pattern in patterns)
                    foreach (var file in dir.GetFiles(pattern))
                        files[file.FullName] = file;

                var recordings = files.Values
                    .GroupBy(f => RecordingKey(f.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => (Files: g.ToList(), Newest: g.Max(f => f.LastWriteTimeUtc), Bytes: g.Sum(f => f.Length)))
                    .OrderByDescending(r => r.Newest)
                    .ToList();

                int kept = 0;
                long keptBytes = 0;
                foreach (var recording in recordings)
                {
                    if (kept < MaxRecordings - 1 && keptBytes + recording.Bytes <= MaxBytes)
                    {
                        kept++;
                        keptBytes += recording.Bytes;
                        continue;
                    }

                    foreach (var file in recording.Files)
                    {
                        try { file.Delete(); }
                        catch (Exception ex) { ErrorLog.Write("Log retention", ex); }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Log retention", ex);
            }
        }

        private static string RecordingKey(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            return name.EndsWith(SummarySuffix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - SummarySuffix.Length)
                : name;
        }
    }
}
