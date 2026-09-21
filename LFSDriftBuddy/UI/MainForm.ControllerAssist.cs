using LFSDriftTools.InSim;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static LFSDriftTools.DriftEngine;
using static LFSDriftTools.IndicatorManager;
using System.Reflection;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        // What the user last chose for the master switch, independent of whether it's currently
        // being held off by missing hardware. The switch itself is forced off when the pad or
        // OutSim2 goes away; this is what makes it come back on once both are back.
        private bool _caMasterUserWanted;
        private bool _caHardwareWasAvailable;

        private void ApplyControllerAssistMasterState(bool on, bool userInitiated = true)
        {
            _caMasterUserWanted = on;

            if (on)
            {
                // Belt-and-braces: the periodic tick (OnOutGaugeFreshnessPollTick) only runs
                // once the message loop is pumping, which auto-start-on-launch (LoadSettings,
                // still inside the constructor) predates entirely, leaving the baseline at its
                // empty initial value the very first time. Capturing synchronously here, right
                // before the virtual pad exists, guarantees a fresh baseline regardless of when
                // assist starts.
                if (!_assistLoop.IsRunning)
                {
                    PadCloak.CaptureBaseline();
                    _diBaselineGuids = new HashSet<Guid>(_wheelInput.GetAvailableDevices().Select(d => d.Guid));
                }

                if (!_assistLoop.Start())
                {
                    _caMasterSwitch.SetCheckedSilent(false);
                    // A failed manual attempt is the user's answer for now; a failed automatic
                    // resume keeps the wish so the next hardware comeback tries again.
                    if (userInitiated) _caMasterUserWanted = false;
                    SyncControllerAssistAvailability();
                    return;
                }
                if (_caHidePadSwitch != null && _caHidePadSwitch.Checked)
                    ApplyPadCloakState(true);

                ScheduleLfsPadNotice(LfsPadNotice.Emulated, null);
            }
            else
            {
                string physicalPadName = _wheelInput.DeviceName;
                _assistLoop.Stop();
                ApplyPadCloakState(false);

                ScheduleLfsPadNotice(LfsPadNotice.Physical, physicalPadName);
            }

            _overlay.ShowControllerAssistNotification(
                Localization.T(on ? "controllerassist.notification.on" : "controllerassist.notification.off"));

            SaveSettings();
            SyncControllerAssistAvailability();
        }

        private void UpdateControllerAssistStatusLabels()
        {
            if (_caPadStatusLabel == null) return;

            bool padConnected = _assistLoop.PadConnected;
            _caPadStatusLabel.Text = Localization.T(padConnected ? "controllerassist.pad.connected" : "controllerassist.pad.disconnected");
            _caPadStatusLabel.ForeColor = padConnected ? ApplePalette.StatusOk : ApplePalette.StatusIdle;

            // Driver presence itself is checked far less often (RefreshDriverStatusLabels,
            // startup + after an install attempt) since it opens a handle to each driver;
            // this just re-applies the cached result every tick alongside the other locks.
            if (_caHidePadSwitch != null)
                _caHidePadSwitch.Enabled = _caHidePadSwitch.Enabled && _hidHideInstalledCache;
        }

        private bool _vigemInstalledCache;
        private bool _hidHideInstalledCache;

        // Static driver-installed facts, unlike hardware connection state which changes
        // constantly: only re-checked at startup and right after an install attempt, since
        // each check opens a handle to the corresponding driver.
        private void RefreshDriverStatusLabels()
        {
            if (_caVirtualPadStatusLabel == null) return;

            _vigemInstalledCache = DriverManager.IsViGEmBusInstalled();
            _caVirtualPadStatusLabel.Text = "ViGEmBus: " + Localization.T(_vigemInstalledCache ? "drivers.status.installed" : "drivers.status.notinstalled");
            _caVirtualPadStatusLabel.ForeColor = _vigemInstalledCache ? ApplePalette.StatusOk : ApplePalette.StatusIdle;
            _caVigemInstallBtn.Enabled = !_vigemInstalledCache;

            _hidHideInstalledCache = DriverManager.IsHidHideInstalled();
            _caHidHideStatusLabel.Text = "HidHide: " + Localization.T(_hidHideInstalledCache ? "drivers.status.installed" : "drivers.status.notinstalled");
            _caHidHideStatusLabel.ForeColor = _hidHideInstalledCache ? ApplePalette.StatusOk : ApplePalette.StatusIdle;
            _caHidHideInstallBtn.Enabled = !_hidHideInstalledCache;
        }

        private async void InstallDriverAsync(DriverKind kind, Button triggerBtn)
        {
            triggerBtn.Enabled = false;
            triggerBtn.Text = Localization.T("drivers.status.installing");

            var result = await DriverManager.InstallAsync(kind);

            RefreshDriverStatusLabels();
            _caVigemInstallBtn.Text = Localization.T("drivers.button.install");
            _caHidHideInstallBtn.Text = Localization.T("drivers.button.install");

            AppendStatusMessage(result.Message, result.Success ? StatusSuccessColor : StatusErrorColor);

            // The hide-pad wish may have been sitting there unfulfilled only because HidHide
            // wasn't installed yet; now that it is, honor it without a manual re-toggle.
            if (result.Success && kind == DriverKind.HidHide &&
                _caMasterSwitch.Checked && _caHidePadSwitch.Checked && !PadCloak.IsHiding)
                ApplyPadCloakState(true);
        }

        private void ApplyPadCloakState(bool hide)
        {
            if (hide)
            {
                if (PadCloak.TryHide(out string message))
                    AppendStatusMessage(Localization.T("controllerassist.hidhide.hidden"));
                else
                    AppendStatusMessage(message, StatusErrorColor);
            }
            else
            {
                bool wasHiding = PadCloak.IsHiding;
                PadCloak.Unhide();
                if (wasHiding)
                    AppendStatusMessage(Localization.T("controllerassist.hidhide.restored"));
            }
        }

        private enum LfsPadNotice { None, Emulated, Physical }

        private LfsPadNotice _lastLfsPadNotice = LfsPadNotice.None;
        private HashSet<Guid> _diBaselineGuids = new();
        private System.Windows.Forms.Timer? _lfsNoticeTimer;
        private Form? _lfsNoticeForm;
        private bool _lfsNoticeShownThisRun;
        private bool _lfsNoticeDontShow;

        private const string DefaultEmulatedPadName = "Controller (XBOX 360 For Windows)";

        private static bool IsLfsRunning()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("LFS");
                bool running = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                return running;
            }
            catch
            {
                return false;
            }
        }

        private string? FindEmulatedPadName()
        {
            return _wheelInput.GetAvailableDevices()
                .Where(d => SteeringWheelInput.IsXInputPad(d.Name) && !_diBaselineGuids.Contains(d.Guid))
                .Select(d => d.Name)
                .FirstOrDefault();
        }

        private void ScheduleLfsPadNotice(LfsPadNotice kind, string? knownPadName)
        {
            StopLfsNoticeTimer();

            if (_lfsNoticeShownThisRun || _lfsNoticeDontShow) return;

            if (!IsLfsRunning())
            {
                _lastLfsPadNotice = LfsPadNotice.None;
                return;
            }

            if (_lastLfsPadNotice == kind) return;
            _lastLfsPadNotice = kind;

            int nameTries = 0;
            _lfsNoticeTimer = new System.Windows.Forms.Timer { Interval = LfsNoticePollIntervalMs };
            _lfsNoticeTimer.Tick += (s, e) =>
            {
                if (!IsLfsRunning())
                {
                    StopLfsNoticeTimer();
                    _lastLfsPadNotice = LfsPadNotice.None;
                    return;
                }

                if (!OutSimIsReceiving()) return;

                string padName;
                bool nameIsGuess = false;

                if (kind == LfsPadNotice.Physical)
                {
                    padName = string.IsNullOrWhiteSpace(knownPadName) ? DefaultEmulatedPadName : knownPadName;
                }
                else
                {
                    nameTries++;
                    string? found = FindEmulatedPadName();
                    if (found == null && nameTries < 8) return;
                    padName = found ?? DefaultEmulatedPadName;
                    nameIsGuess = found == null;
                }

                StopLfsNoticeTimer();
                ShowLfsPadNotice(kind, padName, nameIsGuess);
            };
            _lfsNoticeTimer.Start();
        }

        private void StopLfsNoticeTimer()
        {
            _lfsNoticeTimer?.Stop();
            _lfsNoticeTimer?.Dispose();
            _lfsNoticeTimer = null;
        }

        private bool OutSimIsReceiving()
        {
            var sample = _outSim.LastSample;
            return _outSim.IsRunning && sample != null && (DateTime.UtcNow - sample.ReceivedAtUtc).TotalSeconds < 2.0;
        }

        private class NoActivateForm : Form
        {
            protected override bool ShowWithoutActivation => true;
        }

        private void ShowLfsPadNotice(LfsPadNotice kind, string padName, bool nameIsGuess)
        {
            _lfsNoticeShownThisRun = true;
            _lfsNoticeForm?.Close();

            bool emulated = kind == LfsPadNotice.Emulated;
            bool showHideTip = emulated && _caHidePadSwitch != null && !_caHidePadSwitch.Checked;

            var popup = new NoActivateForm { StartPosition = FormStartPosition.CenterScreen, TopMost = true };
            int height = 440 + (showHideTip ? 50 : 0);
            var card = BuildPopupChrome(popup, 580, height, out _);

            MakeLabel(card, Localization.T(emulated ? "lfsnotice.title.emulated" : "lfsnotice.title.physical"),
                24, 18, 500, 32, ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 15f));

            MakeLabel(card, Localization.T(emulated ? "lfsnotice.emulated.message" : "lfsnotice.physical.message"),
                24, 62, 532, 70, ApplePalette.Text, UiFonts.Get("Segoe UI", 12f), ContentAlignment.TopLeft);

            MakeLabel(card, Localization.T("lfsnotice.steps"),
                24, 138, 532, 70, ApplePalette.Text, UiFonts.Get("Segoe UI", 12f), ContentAlignment.TopLeft);

            var nameBox = new Panel { Location = new Point(24, 216), Size = new Size(532, 52), BackColor = Color.Transparent };
            nameBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, nameBox.Width - 1, nameBox.Height - 1);
                using GraphicsPath path = DrawingHelpers.RoundedPath(rect, 10);
                using Pen border = new Pen(ApplePalette.Green, 1.6f);
                e.Graphics.DrawPath(border, path);
            };
            card.Controls.Add(nameBox);
            MakeLabel(nameBox, padName, 8, 6, 516, 40, ApplePalette.TextGreen,
                UiFonts.Get("Segoe UI Semibold", 15f), ContentAlignment.MiddleCenter);

            int y = 276;
            if (nameIsGuess)
            {
                MakeLabel(card, Localization.T("lfsnotice.padname.guess"), 24, y, 532, 22,
                    ApplePalette.Secondary, UiFonts.Get("Segoe UI", 10f), ContentAlignment.MiddleCenter);
                y += 26;
            }

            if (showHideTip)
            {
                MakeLabel(card, Localization.T("lfsnotice.hidenote"), 24, y, 532, 44,
                    ApplePalette.Secondary, UiFonts.Get("Segoe UI", 10.5f), ContentAlignment.TopLeft);
            }

            var dontShow = new MacCheckBox { Text = Localization.T("lfsnotice.dontshow"), Location = new Point(24, height - 94), Size = new Size(532, 26), Checked = _lfsNoticeDontShow };
            card.Controls.Add(dontShow);

            var okBtn = MakeButton(card, Localization.T("common.ok"), 220, height - 54, 140, 30, ApplePalette.Blue);
            okBtn.Click += (s, e) => popup.Close();

            popup.FormClosed += (s, e) =>
            {
                if (dontShow.Checked != _lfsNoticeDontShow)
                {
                    _lfsNoticeDontShow = dontShow.Checked;
                    SaveSettings();
                }
                if (ReferenceEquals(_lfsNoticeForm, popup)) _lfsNoticeForm = null;
                popup.Dispose();
            };

            _lfsNoticeForm = popup;
            popup.Show();
        }

        private void RefreshControllerAssistPresetButtons()
        {
            if (_caPresetOffBtn == null) return;

            var active = _assistEngine.Settings.Preset;
            SetButtonColor(_caPresetOffBtn, active == AssistPreset.Off ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetLightBtn, active == AssistPreset.Light ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetMediumBtn, active == AssistPreset.Medium ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetStrongBtn, active == AssistPreset.Strong ? ApplePalette.Green : ApplePalette.Blue);
        }

        private string _lastAutoPromptSignature = "";

        // ViGEmBus keeps listing its virtual pad for a moment after it's been disconnected, so
        // "is the virtual pad connected right now" alone would let that leftover entry be picked
        // as a real controller in the gap right after the physical pad drops out.
        private DateTime _virtualPadLastActiveUtc = DateTime.MinValue;
        private const double VirtualPadLingerSeconds = 4.0;

        private bool VirtualPadMayBePresent =>
            _virtualPad.IsAvailable || (DateTime.UtcNow - _virtualPadLastActiveUtc).TotalSeconds < VirtualPadLingerSeconds;

        private void TryAutoSelectAssistController()
        {
            if (_wheelInput == null || _wheelInput.IsConnected || _wheelInput.IsXInputDevice) return;

            if (VirtualPadMayBePresent) return;

            var devices = _deviceListCache;
            var candidates = devices
                .Where(d => SteeringWheelInput.IsXInputPad(d.Name) || _wheelInput.IsRecognizedWheel(d.Guid))
                .ToList();

            if (candidates.Count == 0)
            {
                _lastAutoPromptSignature = "";
                return;
            }

            if (candidates.Count == 1)
            {
                var device = candidates[0];
                JoystickOffset axis = SteeringWheelInput.IsXInputPad(device.Name)
                    ? JoystickOffset.X
                    : _wheelInput.GetDefaultAxisFor(device.Name);

                if (!_wheelInput.ConnectToDevice(device.Guid, axis)) return;

                _savedWheelGuid = device.Guid;
                _savedWheelAxis = axis;
                SyncControllerAssistAvailability();
                SaveSettings();
                AppendStatusMessage(string.Format(Localization.T("controllerassist.autoselected"), _wheelInput.DeviceName));
                _lastAutoPromptSignature = "";
                return;
            }

            string signature = string.Join(",", candidates.Select(d => d.Guid).OrderBy(g => g));
            if (signature == _lastAutoPromptSignature) return;
            _lastAutoPromptSignature = signature;

            ShowWheelSetupDialog(devices);
        }

        private string? _lastInputControllerTilesSignature = null;

        private Font? _inputTilesEmptyFont;

        private void RefreshInputControllerTiles()
        {
            if (_inputControllerTilesPanel == null) return;

            var devices = GetCachedDeviceList()
                .Where(d => !(SteeringWheelInput.IsXInputPad(d.Name) && VirtualPadMayBePresent && d.Guid != _wheelInput.DeviceGuid))
                .ToList();

            string activeGuid = (_wheelInput.IsConnected || _wheelInput.IsXInputDevice) ? _wheelInput.DeviceGuid.ToString() : "";
            string signature = activeGuid + "|" + string.Join(",", devices.Select(d => $"{d.Guid}:{d.Name}"));
            if (signature == _lastInputControllerTilesSignature) return;
            _lastInputControllerTilesSignature = signature;

            foreach (Control old in _inputControllerTilesPanel.Controls.Cast<Control>().ToList())
                old.Dispose();

            if (devices.Count == 0)
            {
                _inputTilesEmptyFont ??= UiFonts.Get("Segoe UI", 9f);
                MakeLabel(_inputControllerTilesPanel, Localization.T("inputcontroller.none"), 0, 0, _inputControllerTilesPanel.Width, 30,
                    ApplePalette.Secondary, _inputTilesEmptyFont, ContentAlignment.MiddleLeft);
                return;
            }

            const int tileW = 120, tileH = 100, tileGap = 10;
            int x = 0;
            foreach (var d in devices)
            {
                bool isActive = d.Guid == _wheelInput.DeviceGuid && (_wheelInput.IsConnected || _wheelInput.IsXInputDevice);
                var tile = MakeButton(_inputControllerTilesPanel, d.Name, x, 0, tileW, tileH,
                    isActive ? ApplePalette.Green : ApplePalette.Blue);

                var guid = d.Guid;
                var name = d.Name;
                tile.Click += (s, e) =>
                {
                    JoystickOffset axis = SteeringWheelInput.IsXInputPad(name)
                        ? (guid == _savedWheelGuid ? _savedWheelAxis : JoystickOffset.X)
                        : _wheelInput.GetDefaultAxisFor(name);

                    if (!_wheelInput.ConnectToDevice(guid, axis)) return;

                    _savedWheelGuid = guid;
                    _savedWheelAxis = axis;
                    SyncControllerAssistAvailability();
                    SaveSettings();
                    AppendStatusMessage(string.Format(Localization.T("wheelconfig.configured"), _wheelInput.DeviceName, _wheelInput.SteeringAxis));
                };

                x += tileW + tileGap;
            }
        }

        private void SyncControllerAssistAvailability()
        {
            RefreshInputControllerTiles();

            if (_caMasterSwitch == null) return;

            bool hasController = _wheelInput.IsXInputDevice;
            bool hasOutSim = _outSim.IsRunning;
            bool hardwareAvailable = hasController && hasOutSim;

            _caLockedLabel.Visible = true;
            if (!hasController)
            {
                _caLockedLabel.Text = Localization.T("controllerassist.locked");
                _caLockedLabel.ForeColor = ApplePalette.StatusWarn;
            }
            else if (!hasOutSim)
            {
                _caLockedLabel.Text = Localization.T("controllerassist.locked.outsim");
                _caLockedLabel.ForeColor = ApplePalette.StatusWarn;
            }
            else
            {
                _caLockedLabel.Text = string.Format(Localization.T("controllerassist.connected"), _wheelInput.DeviceName);
                _caLockedLabel.ForeColor = ApplePalette.StatusOk;
            }
            _caMasterSwitch.Enabled = hardwareAvailable;

            if (!hardwareAvailable && _caMasterSwitch.Checked)
            {
                _caMasterSwitch.SetCheckedSilent(false);
                _assistLoop.Stop();
                ApplyPadCloakState(false);
                _lastLfsPadNotice = LfsPadNotice.None;
                SaveSettings();
            }

            bool hardwareJustReturned = hardwareAvailable && !_caHardwareWasAvailable;
            _caHardwareWasAvailable = hardwareAvailable;
            if (hardwareJustReturned && _caMasterUserWanted && !_caMasterSwitch.Checked)
            {
                _caMasterSwitch.SetCheckedSilent(true);
                ApplyControllerAssistMasterState(true, userInitiated: false);
            }

            bool optionsEnabled = hardwareAvailable && _caMasterSwitch.Checked;
            foreach (var c in _caControls) c.Enabled = optionsEnabled;

            UpdateControllerAssistStatusLabels();
        }

        private static string FormatSliderValue(double v, int decimals, string suffix) => v.ToString("F" + decimals) + suffix;

        private void ShowControllerAssistTuningMenu()
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 420, 700, out _);

            MakeLabel(card, Localization.T("controllerassist.tuning.title"), 20, 15, 320, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            var s = _assistEngine.Settings;
            int y = 55;

            void AddSlider(string labelKey, double minDisplay, double maxDisplay, double tickScale,
                double initialDisplay, Action<double> onChangeDisplay, string suffix = "", int decimals = 0)
            {
                MakeLabel(card, Localization.T(labelKey), 20, y, 220, 20, ApplePalette.Text);
                var slider = new MacSlider
                {
                    Location = new Point(20, y + 20),
                    Size = new Size(300, 22),
                    Minimum = (int)Math.Round(minDisplay * tickScale),
                    Maximum = (int)Math.Round(maxDisplay * tickScale),
                    Value = (int)Math.Round(initialDisplay * tickScale),
                };
                var valLabel = MakeLabel(card, FormatSliderValue(initialDisplay, decimals, suffix), 322, y + 20, 70, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));
                slider.ValueChanged += (sd, e) =>
                {
                    double v = slider.Value / tickScale;
                    onChangeDisplay(v);
                    valLabel.Text = FormatSliderValue(v, decimals, suffix);
                    s.Preset = AssistPreset.Custom;
                    RefreshControllerAssistPresetButtons();
                    SaveSettings();
                };
                card.Controls.Add(slider);
                y += 52;
            }

            AddSlider("controllerassist.tuning.deadzone", 0, 20, 10, s.DeadzonePercent, v => s.DeadzonePercent = v, "%", 1);
            AddSlider("controllerassist.tuning.responsecurve", 0.2, 3.0, 100, s.ResponseCurve, v => s.ResponseCurve = v, "", 2);
            AddSlider("controllerassist.tuning.speedref", 20, 250, 1, s.SpeedSensitivityRefKmh, v => s.SpeedSensitivityRefKmh = v, " km/h", 0);
            AddSlider("controllerassist.tuning.minhighspeed", 0, 100, 1, s.MinHighSpeedSteerFraction * 100, v => s.MinHighSpeedSteerFraction = v / 100.0, "%", 0);
            AddSlider("controllerassist.tuning.slipactivation", 0, 30, 10, s.SlipActivationDeg, v => s.SlipActivationDeg = v, "°", 1);
            AddSlider("controllerassist.tuning.slipfull", 0, 45, 10, s.SlipFullDeg, v => s.SlipFullDeg = v, "°", 1);
            AddSlider("controllerassist.tuning.countersteergain", 0, 5, 100, s.CountersteerGain, v => s.CountersteerGain = v, "", 2);
            AddSlider("controllerassist.tuning.assiststrength", 0, 100, 1, s.AssistStrength * 100, v => s.AssistStrength = v / 100.0, "%", 0);
            AddSlider("controllerassist.tuning.minactivationspeed", 0, 20, 10, s.MinActivationSpeedKmh, v => s.MinActivationSpeedKmh = v, " km/h", 1);
            AddSlider("controllerassist.tuning.maxsteerrate", 1, 40, 10, s.MaxSteerRatePerSec, v => s.MaxSteerRatePerSec = v, "", 1);
            AddSlider("controllerassist.tuning.smoothing", 0, 200, 1, s.SmoothingMs, v => s.SmoothingMs = v, " ms", 0);
            AddSlider("controllerassist.tuning.selfcenter", 0, 100, 1, s.SelfCenterStrength * 100, v => s.SelfCenterStrength = v / 100.0, "%", 0);

            ShowModalPopup(popup);
        }

        private void ShowControllerAssistRumbleMenu()
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 420, 760, out _);

            MakeLabel(card, Localization.T("controllerassist.rumble.title"), 20, 15, 320, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            var r = _assistEngine.Rumble;
            int y = 55;

            void AddSlider(string labelKey, double minDisplay, double maxDisplay, double tickScale,
                double initialDisplay, Action<double> onChangeDisplay, string suffix = "", int decimals = 0)
            {
                MakeLabel(card, Localization.T(labelKey), 20, y, 220, 20, ApplePalette.Text);
                var slider = new MacSlider
                {
                    Location = new Point(20, y + 20),
                    Size = new Size(300, 22),
                    Minimum = (int)Math.Round(minDisplay * tickScale),
                    Maximum = (int)Math.Round(maxDisplay * tickScale),
                    Value = (int)Math.Round(initialDisplay * tickScale),
                };
                var valLabel = MakeLabel(card, FormatSliderValue(initialDisplay, decimals, suffix), 322, y + 20, 70, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));
                slider.ValueChanged += (sd, e) =>
                {
                    double v = slider.Value / tickScale;
                    onChangeDisplay(v);
                    valLabel.Text = FormatSliderValue(v, decimals, suffix);
                    SaveSettings();
                };
                card.Controls.Add(slider);
                y += 52;
            }

            AddSlider("controllerassist.rumble.strength", 0, 100, 1, r.RumbleStrength * 100, v => r.RumbleStrength = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.loadweight", 0, 100, 1, r.RumbleLoadWeight * 100, v => r.RumbleLoadWeight = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.roadweight", 0, 100, 1, r.RumbleRoadWeight * 100, v => r.RumbleRoadWeight = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.impactweight", 0, 100, 1, r.RumbleImpactWeight * 100, v => r.RumbleImpactWeight = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.surfaceweight", 0, 100, 1, r.RumbleSurfaceChangeWeight * 100, v => r.RumbleSurfaceChangeWeight = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.shiftweight", 0, 100, 1, r.RumbleShiftWeight * 100, v => r.RumbleShiftWeight = v / 100.0, "%", 0);
            AddSlider("controllerassist.rumble.jerksensitivity", 0.5, 20, 10, r.RumbleJerkSensitivity, v => r.RumbleJerkSensitivity = v, " g/s", 1);
            AddSlider("controllerassist.rumble.loadslipactivation", 0, 30, 10, r.RumbleLoadSlipActivationDeg, v => r.RumbleLoadSlipActivationDeg = v, "°", 1);
            AddSlider("controllerassist.rumble.loadslipfull", 0, 30, 10, r.RumbleLoadSlipFullDeg, v => r.RumbleLoadSlipFullDeg = v, "°", 1);
            AddSlider("controllerassist.rumble.roadgain", 0.02, 3, 100, r.RumbleRoadGain, v => r.RumbleRoadGain = v, "", 2);
            AddSlider("controllerassist.rumble.impactgain", 0.02, 3, 100, r.RumbleImpactGain, v => r.RumbleImpactGain = v, "", 2);
            AddSlider("controllerassist.rumble.responsecurve", 0.2, 3, 100, r.RumbleResponseCurve, v => r.RumbleResponseCurve = v, "", 2);
            AddSlider("controllerassist.rumble.directionalbias", 0, 100, 1, r.RumbleDirectionalBias * 100, v => r.RumbleDirectionalBias = v / 100.0, "%", 0);

            ShowModalPopup(popup);
        }
    }
}
