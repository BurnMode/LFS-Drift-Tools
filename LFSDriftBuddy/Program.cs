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

        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\r\n\r\n");
            }
            catch
            {
            }
        }
    }
}
