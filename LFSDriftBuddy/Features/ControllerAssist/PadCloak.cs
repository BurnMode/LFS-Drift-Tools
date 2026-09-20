using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace LFSDriftTools
{
    // Hides the physical XInput pad from every other application (games included) via HidHide
    // while Controller Assist is emulating a virtual Xbox 360 pad in its place, so LFS only
    // ever sees the corrected virtual one.
    //
    // ViGEmBus's virtual pad can't be told apart from a real one by hardware ID or VID/PID: it
    // deliberately reports itself as a stock Microsoft Xbox 360 controller (VID_045E&PID_028E)
    // for maximum game compatibility, and its PnP instance ID doesn't mention ViGEmBus/Nefarius
    // anywhere either. So instead of trying to recognize it from the outside, MainForm keeps
    // calling CaptureBaseline() on every tick where the virtual pad is known NOT to be connected
    // yet; whichever XUSB devices were already present at that point are "physical" by
    // construction, and only that known set is ever considered for hiding.
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

        // Called once at startup, before anything else touches HidHide: restores visibility for
        // whatever this app hid in a previous run that never reached a clean shutdown (crash,
        // task-killed, power loss).
        public static void RestoreFromCrash()
        {
            if (!File.Exists(StateFile)) return;

            string instanceId = "";
            try { instanceId = File.ReadAllText(StateFile).Trim(); } catch { }
            try { File.Delete(StateFile); } catch { }

            if (string.IsNullOrEmpty(instanceId)) return;

            try
            {
                var svc = new HidHideControlService();
                if (svc.IsInstalled)
                    svc.RemoveBlockedInstanceId(instanceId);
            }
            catch { }
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
                svc.AddApplicationPath(exePath, false);
                svc.AddBlockedInstanceId(instanceId);
                svc.IsActive = true;

                try { File.WriteAllText(StateFile, instanceId); } catch { }

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
                var svc = new HidHideControlService();
                if (svc.IsInstalled && _hiddenInstanceId != null)
                    svc.RemoveBlockedInstanceId(_hiddenInstanceId);
            }
            catch { }
            finally
            {
                _hiddenInstanceId = null;
                IsHiding = false;
                try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch { }
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
            for (int i = 0; i < 8; i++)
            {
                bool ok = Devcon.FindByInterfaceGuid(DeviceInterfaceIds.XUsbDevice, out _, out string instanceId, i, true);
                if (!ok) break;
                set.Add(instanceId);
            }
            return set;
        }
    }
}
