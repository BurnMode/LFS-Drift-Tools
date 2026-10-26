using System;
using System.Collections.Generic;
using System.IO;

namespace LFSDriftTools
{
    public static class ErrorLog
    {
        private const long MaxBytes = 1024 * 1024;
        private const double RepeatWindowSec = 10.0;
        private const int MaxTrackedErrors = 256;

        private sealed class Entry
        {
            public DateTime LastWrittenUtc;
            public int Suppressed;
        }

        private static readonly object Sync = new();
        private static readonly Dictionary<string, Entry> Recent = new();

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "crash.log");

        public static void Write(string source, Exception? ex)
        {
            try
            {
                lock (Sync)
                {
                    string key = $"{source}|{ex?.GetType().FullName}|{ex?.Message}|{ex?.StackTrace}";
                    DateTime now = DateTime.UtcNow;

                    if (Recent.TryGetValue(key, out Entry? entry))
                    {
                        if ((now - entry.LastWrittenUtc).TotalSeconds < RepeatWindowSec)
                        {
                            entry.Suppressed++;
                            return;
                        }
                    }
                    else
                    {
                        if (Recent.Count >= MaxTrackedErrors)
                            Recent.Clear();
                        entry = new Entry();
                        Recent[key] = entry;
                    }

                    string path = FilePath;
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(path, path + ".old", overwrite: true);

                    string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string text = "";
                    if (entry.Suppressed > 0)
                        text += $"[{stamp}] {source}: previous error repeated {entry.Suppressed} more times\r\n\r\n";
                    text += $"[{stamp}] {source}: {ex}\r\n\r\n";
                    File.AppendAllText(path, text);

                    entry.LastWrittenUtc = now;
                    entry.Suppressed = 0;
                }
            }
            catch
            {
            }
        }
    }
}
