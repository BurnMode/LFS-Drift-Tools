using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftTools
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ErrorLog.Write("AppDomain", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => ErrorLog.Write("Task", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ErrorLog.Write("UI", e.Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            PerfProbe.Initialize();
            PerfProbe.RegisterThread(PerfThread.Ui);
            PerfProbe.InstallMessageProbe();
            WheelProbe.Initialize();
            Application.Run(new MainForm());
            PerfProbe.Shutdown();
            WheelProbe.Shutdown();
        }
    }
}
