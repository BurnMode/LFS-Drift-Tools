using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LFSDriftTools
{
    public enum FolderLocationRisk { None, ProgramFiles, OneDrive, Temp, Zip }

    public static class SystemProbe
    {
        public static bool TryWrite(string directory, out string error)
        {
            error = "";
            string probe = Path.Combine(directory, "write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(probe, "test");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
                return false;
            }
        }

        public static FolderLocationRisk ClassifyLocation(string directory)
        {
            string full = NormalizeDirectory(directory);

            if (IsUnder(full, Path.GetTempPath()))
                return full.Contains(".zip", StringComparison.OrdinalIgnoreCase) ? FolderLocationRisk.Zip : FolderLocationRisk.Temp;

            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root) && IsUnder(full, root))
                    return FolderLocationRisk.ProgramFiles;
            }

            foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string? root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(root) && IsUnder(full, root))
                    return FolderLocationRisk.OneDrive;
            }

            return FolderLocationRisk.None;
        }

        public static bool IsInUserLibraryFolder(string directory)
        {
            string full = NormalizeDirectory(directory);
            foreach (var folder in new[]
            {
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
            })
            {
                string root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root) && IsUnder(full, root))
                    return true;
            }
            return false;
        }

        private static string NormalizeDirectory(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        private static bool IsUnder(string fullDirectory, string root) =>
            fullDirectory.StartsWith(NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase);

        public static bool IsCurrentProcessElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool? IsWindowProcessElevated(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
                    return null;
                try
                {
                    if (!GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _))
                        return null;
                    return elevated != 0;
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static string? FindUdpPortOwner(int port)
        {
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, false, AfInet, UdpTableOwnerPid, 0);
            if (size <= 0) return null;

            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedUdpTable(table, ref size, false, AfInet, UdpTableOwnerPid, 0) != 0)
                    return null;

                int count = Marshal.ReadInt32(table);
                const int rowSize = 12;
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = table + 4 + i * rowSize;
                    int rawPort = Marshal.ReadInt32(row, 4);
                    int localPort = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                    if (localPort != port) continue;

                    int pid = Marshal.ReadInt32(row, 8);
                    if (pid == Environment.ProcessId) return null;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        return process.ProcessName + ".exe";
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        public static List<string> FindRunningPrograms(IReadOnlyList<(string ProcessName, string DisplayName)> known)
        {
            var found = new List<string>();
            Process[] processes = Process.GetProcesses();
            try
            {
                foreach (var (processName, displayName) in known)
                {
                    if (found.Contains(displayName)) continue;
                    if (processes.Any(p => string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
                        found.Add(displayName);
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
            return found;
        }

        public static int CountErrorLogEntries(string path, DateTime since, out string lastSource)
        {
            lastSource = "";
            if (!File.Exists(path)) return 0;

            int count = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length < 23 || line[0] != '[' || line[20] != ']') continue;
                if (!DateTime.TryParseExact(line.Substring(1, 19), "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stamp)) continue;
                if (stamp < since) continue;
                int colon = line.IndexOf(": ", 22, StringComparison.Ordinal);
                if (colon < 0) continue;
                if (line.IndexOf("previous error repeated", colon, StringComparison.Ordinal) == colon + 2) continue;
                count++;
                lastSource = line.Substring(22, colon - 22);
            }
            return count;
        }

        public static List<string>? QueryAntivirusProducts()
        {
            try
            {
                Type? locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
                if (locatorType == null) return null;
                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic service = locator.ConnectServer(".", @"root\SecurityCenter2");
                dynamic items = service.ExecQuery("SELECT displayName FROM AntiVirusProduct");
                var names = new List<string>();
                foreach (dynamic item in items)
                {
                    string? name = item.displayName;
                    if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                        names.Add(name);
                }
                return names;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Antivirus query", ex);
                return null;
            }
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenElevation = 20;
        private const int AfInet = 2;
        private const int UdpTableOwnerPid = 1;

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int ipVersion, int tableClass, uint reserved);
    }
}
