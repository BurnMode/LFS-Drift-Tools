using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace LFSDriftTools
{
    public static class PadCloak
    {
        private static readonly string StateFile = Path.Combine(
            System.Windows.Forms.Application.StartupPath, "Configs", "hidden_pad.txt");

        public static bool IsHiding { get; private set; }

        private static string? _hiddenInstanceId;
        private static HashSet<string> _knownPhysicalInstanceIds = new();

        public static void CaptureBaseline()
        {
            _knownPhysicalInstanceIds = EnumerateXInputInstanceIds();
        }

        public static void RestoreFromCrash()
        {
            if (!File.Exists(StateFile)) return;

            string instanceId = "";
            try { instanceId = File.ReadAllText(StateFile).Trim(); } catch { }

            try
            {
                if (!string.IsNullOrEmpty(instanceId))
                {
                    lock (DeviceSync.Lock)
                    {
                        var svc = new HidHideControlService();
                        if (svc.IsInstalled)
                            svc.RemoveBlockedInstanceId(instanceId);
                    }
                }
                DeleteStateFile();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("HidHide restore after crash", ex);
            }
        }

        private static void DeleteStateFile()
        {
            try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch { }
        }

        public static bool TryHide(out string message)
        {
            message = "";
            if (IsHiding) return true;

            try
            {
                var svc = new HidHideControlService();
                if (!svc.IsInstalled)
                {
                    message = Localization.T("controllerassist.hidhide.notinstalled");
                    return false;
                }

                string? instanceId = FindSolePhysicalInstanceId();
                if (instanceId == null)
                {
                    message = Localization.T("controllerassist.hidhide.ambiguous");
                    return false;
                }

                string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
                lock (DeviceSync.Lock)
                {
                    svc.AddApplicationPath(exePath, false);
                    svc.AddBlockedInstanceId(instanceId);
                    svc.IsActive = true;
                }

                try { File.WriteAllText(StateFile, instanceId); }
                catch (Exception ex) { ErrorLog.Write("HidHide state save", ex); }

                _hiddenInstanceId = instanceId;
                IsHiding = true;
                return true;
            }
            catch (Exception ex)
            {
                message = string.Format(Localization.T("controllerassist.hidhide.error"), ex.Message);
                return false;
            }
        }

        public static void Unhide()
        {
            if (!IsHiding) return;

            try
            {
                lock (DeviceSync.Lock)
                {
                    var svc = new HidHideControlService();
                    if (svc.IsInstalled && _hiddenInstanceId != null)
                        svc.RemoveBlockedInstanceId(_hiddenInstanceId);
                }
                DeleteStateFile();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("HidHide unhide", ex);
            }
            finally
            {
                _hiddenInstanceId = null;
                IsHiding = false;
            }
        }

        private static string? FindSolePhysicalInstanceId()
        {
            var stillPresent = EnumerateXInputInstanceIds()
                .Where(id => _knownPhysicalInstanceIds.Contains(id))
                .ToList();

            return stillPresent.Count == 1 ? stillPresent[0] : null;
        }

        private static HashSet<string> EnumerateXInputInstanceIds()
        {
            var set = new HashSet<string>();
            lock (DeviceSync.Lock)
            {
                for (int i = 0; i < 8; i++)
                {
                    bool ok = Devcon.FindByInterfaceGuid(DeviceInterfaceIds.XUsbDevice, out _, out string instanceId, i, true);
                    if (!ok) break;
                    set.Add(instanceId);
                }
            }
            return set;
        }
    }
}
