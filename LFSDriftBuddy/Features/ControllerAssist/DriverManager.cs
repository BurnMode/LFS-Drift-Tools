using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;

namespace LFSDriftTools
{
    public enum DriverKind { ViGEmBus, HidHide }

    public class DriverInstallResult
    {
        public bool Success;
        public string Message = "";
    }

    public static class DriverManager
    {
        public static string ViGEmBusInstallerPath =>
            Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Drivers", "ViGEmBusSetup.exe");

        public static string HidHideInstallerPath =>
            Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Drivers", "HidHideSetup.exe");

        public static bool IsViGEmBusInstalled()
        {
            try
            {
                using var client = new ViGEmClient();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsHidHideInstalled()
        {
            try
            {
                return new HidHideControlService().IsInstalled;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsInstalled(DriverKind kind) =>
            kind == DriverKind.ViGEmBus ? IsViGEmBusInstalled() : IsHidHideInstalled();

        private static string InstallerPath(DriverKind kind) =>
            kind == DriverKind.ViGEmBus ? ViGEmBusInstallerPath : HidHideInstallerPath;

        public static async Task<DriverInstallResult> InstallAsync(DriverKind kind)
        {
            string path = InstallerPath(kind);
            if (!File.Exists(path))
                return new DriverInstallResult { Success = false, Message = Localization.T("drivers.installer_missing") };

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "/quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas",
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new DriverInstallResult { Success = false, Message = Localization.T("drivers.install_failed_exception") };

                await proc.WaitForExitAsync();

                bool ok = IsInstalled(kind);
                return new DriverInstallResult
                {
                    Success = ok,
                    Message = ok ? Localization.T("drivers.install_success") : Localization.T("drivers.install_failed_verify")
                };
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new DriverInstallResult { Success = false, Message = Localization.T("drivers.install_cancelled") };
            }
            catch (Exception ex)
            {
                return new DriverInstallResult { Success = false, Message = string.Format(Localization.T("drivers.install_failed_exception_detail"), ex.Message) };
            }
        }
    }
}
