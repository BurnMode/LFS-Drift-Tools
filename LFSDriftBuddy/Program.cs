using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftTools
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => LogCrash("Task", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogCrash("UI", e.Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private const long CrashLogMaxBytes = 1024 * 1024;
        private const double CrashLogRepeatWindowSec = 10.0;

        private static readonly object CrashLogLock = new();
        private static string? _lastCrashKey;
        private static DateTime _lastCrashWrittenUtc = DateTime.MinValue;
        private static int _suppressedCrashCount;

        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                lock (CrashLogLock)
                {
                    string key = $"{source}|{ex?.GetType().FullName}|{ex?.Message}|{ex?.StackTrace}";
                    DateTime now = DateTime.UtcNow;

                    if (key == _lastCrashKey && (now - _lastCrashWrittenUtc).TotalSeconds < CrashLogRepeatWindowSec)
                    {
                        _suppressedCrashCount++;
                        return;
                    }

                    string path = Path.Combine(AppContext.BaseDirectory, "crash.log");

                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > CrashLogMaxBytes)
                        File.Move(path, path + ".old", overwrite: true);

                    string text = "";
                    if (_suppressedCrashCount > 0)
                        text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] previous error repeated {_suppressedCrashCount} more times\r\n\r\n";

                    text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\r\n\r\n";
                    File.AppendAllText(path, text);

                    _lastCrashKey = key;
                    _lastCrashWrittenUtc = now;
                    _suppressedCrashCount = 0;
                }
            }
            catch
            {
            }
        }
    }
}
