using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Reflection;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        private bool _caMasterUserWanted;
        private bool _caHardwareWasAvailable;

        private void ApplyControllerAssistMasterState(bool on, bool userInitiated = true)
        {
            _caMasterUserWanted = on;

            if (on)
            {
                if (!_assistLoop.IsRunning)
                {
                    PadCloak.CaptureBaseline();
                    _diBaselineGuids = new HashSet<Guid>(_wheelInput.GetAvailableDevices().Select(d => d.Guid));
                }

                if (!_assistLoop.Start())
                {
                    _caMasterSwitch.SetCheckedSilent(false);
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

            if (_caHidePadSwitch != null)
                _caHidePadSwitch.Enabled = _caHidePadSwitch.Enabled && _hidHideInstalledCache;
        }

        private bool _vigemInstalledCache;
        private bool _hidHideInstalledCache;

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
                if (_lfsNoticeDontShow || _lfsNoticeShownThisRun)
                {
                    StopLfsNoticeTimer();
                    return;
                }

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
            if (_lfsNoticeDontShow) return;
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
            dontShow.CheckedChanged += (s, e) =>
            {
                if (dontShow.Checked == _lfsNoticeDontShow) return;
                _lfsNoticeDontShow = dontShow.Checked;
                SaveSettings();
            };

            var okBtn = MakeButton(card, Localization.T("common.ok"), 220, height - 54, 140, 30, ApplePalette.Blue);
            okBtn.Click += (s, e) => popup.Close();

            popup.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_lfsNoticeForm, popup)) _lfsNoticeForm = null;
                popup.Dispose();
            };

            _lfsNoticeForm = popup;
            popup.Show();
        }

        private void SelectAssistPreset(AssistPreset preset)
        {
            _assistEngine.Settings = AssistSettings.ForPreset(preset);
            RefreshControllerAssistPresetButtons();
            SaveSettings();
        }

        private void RefreshControllerAssistPresetButtons()
        {
            if (_caPresetOffBtn == null) return;

            var active = _assistEngine.Settings.Preset;
            SetButtonColor(_caPresetOffBtn, active == AssistPreset.Off ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetRaceBtn, active == AssistPreset.Race ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetStreetBtn, active == AssistPreset.Street ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_caPresetDriftBtn, active == AssistPreset.Drift ? ApplePalette.Green : ApplePalette.Blue);
        }

        private string _lastAutoPromptSignature = "";

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

        private void UpdateAssistCharacterLabel()
        {
            if (_caCharacterValueLabel == null) return;

            int value = (int)Math.Round(_assistEngine.AssistCharacterPercent);
            if (value < 0)
            {
                _caCharacterValueLabel.Text = string.Format(Localization.T("controllerassist.character.stability.value"), -value);
                _caCharacterValueLabel.ForeColor = ApplePalette.Blue;
            }
            else if (value > 0)
            {
                _caCharacterValueLabel.Text = string.Format(Localization.T("controllerassist.character.drift.value"), value);
                _caCharacterValueLabel.ForeColor = ApplePalette.Orange;
            }
            else
            {
                _caCharacterValueLabel.Text = Localization.T("controllerassist.character.neutral");
                _caCharacterValueLabel.ForeColor = ApplePalette.Text;
            }
        }

        private sealed record TuningSlider(string LabelKey, string HelpKey, double Min, double Max, double TickScale,
            Func<double> Get, Action<double> Set, string Suffix, int Decimals);

        private static List<(string SectionKey, TuningSlider[] Sliders)> GetTuningSections(AssistSettings s)
        {
            var sections = new List<(string SectionKey, TuningSlider[] Sliders)>
            {
                ("controllerassist.tuning.section.input", new[]
                {
                    new TuningSlider("controllerassist.tuning.deadzone", "controllerassist.tuning.deadzone.help",
                        0, 20, 10, () => s.DeadzonePercent, v => s.DeadzonePercent = v, "%", 1),
                    new TuningSlider("controllerassist.tuning.responsecurve", "controllerassist.tuning.responsecurve.help",
                        0.2, 3.0, 100, () => s.ResponseCurve, v => s.ResponseCurve = v, "", 2),
                    new TuningSlider("controllerassist.tuning.speedref", "controllerassist.tuning.speedref.help",
                        20, 250, 1, () => s.SpeedSensitivityRefKmh, v => s.SpeedSensitivityRefKmh = v, " km/h", 0),
                    new TuningSlider("controllerassist.tuning.minhighspeed", "controllerassist.tuning.minhighspeed.help",
                        0, 100, 1, () => s.MinHighSpeedSteerFraction * 100, v => s.MinHighSpeedSteerFraction = v / 100.0, "%", 0),
                }),
            };

            if (s.Mode == SteeringAssistMode.TargetAngle)
            {
                sections.Add(("controllerassist.tuning.section.anglehold", new[]
                {
                    new TuningSlider("controllerassist.tuning.slipactivation", "controllerassist.tuning.slipactivation.drift.help",
                        0, 30, 10, () => s.SlipActivationDeg, v => s.SlipActivationDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.slipfull", "controllerassist.tuning.slipfull.drift.help",
                        0, 45, 10, () => s.SlipFullDeg, v => s.SlipFullDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.assiststrength", "controllerassist.tuning.assiststrength.drift.help",
                        0, 100, 1, () => s.AssistStrength * 100, v => s.AssistStrength = v / 100.0, "%", 0),
                    new TuningSlider("controllerassist.tuning.targetanglemax", "controllerassist.tuning.targetanglemax.help",
                        10, 135, 10, () => s.TargetAngleMaxDeg, v => s.TargetAngleMaxDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.targetanglecurve", "controllerassist.tuning.targetanglecurve.help",
                        0.2, 3.0, 100, () => s.TargetAngleCurve, v => s.TargetAngleCurve = v, "", 2),
                    new TuningSlider("controllerassist.tuning.targetanglegain", "controllerassist.tuning.targetanglegain.help",
                        0.5, 5, 100, () => s.TargetAngleGain, v => s.TargetAngleGain = v, "", 2),
                    new TuningSlider("controllerassist.tuning.targetanglesmoothing", "controllerassist.tuning.targetanglesmoothing.help",
                        0, 300, 1, () => s.TargetAngleSmoothingMs, v => s.TargetAngleSmoothingMs = v, " ms", 0),
                    new TuningSlider("controllerassist.tuning.correctionrate", "controllerassist.tuning.correctionrate.help",
                        0, 1500, 1, () => s.TargetAngleCorrectionRatePerSec, v => s.TargetAngleCorrectionRatePerSec = v, " %/s", 0),
                    new TuningSlider("controllerassist.tuning.handoverrelease", "controllerassist.tuning.handoverrelease.help",
                        0, 500, 1, () => s.TargetAngleHandoverReleaseMs, v => s.TargetAngleHandoverReleaseMs = v, " ms", 0),
                    new TuningSlider("controllerassist.tuning.stickrate", "controllerassist.tuning.stickrate.help",
                        0, 1000, 1, () => s.TargetAngleStickRateDegPerSec, v => s.TargetAngleStickRateDegPerSec = v, " °/s", 0),
                    new TuningSlider("controllerassist.tuning.softsaturation", "controllerassist.tuning.softsaturation.help",
                        0, 100, 1, () => s.TargetAngleSoftSaturation, v => s.TargetAngleSoftSaturation = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.spincatch", "controllerassist.tuning.spincatch.help",
                        0, 100, 1, () => s.TargetAngleSpinCatch, v => s.TargetAngleSpinCatch = v, "%", 0),
                }));
            }
            else
            {
                sections.Add(("controllerassist.tuning.section.countersteer", new[]
                {
                    new TuningSlider("controllerassist.tuning.slipactivation", "controllerassist.tuning.slipactivation.help",
                        0, 30, 10, () => s.SlipActivationDeg, v => s.SlipActivationDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.slipfull", "controllerassist.tuning.slipfull.help",
                        0, 45, 10, () => s.SlipFullDeg, v => s.SlipFullDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.countersteergain", "controllerassist.tuning.countersteergain.help",
                        0, 5, 100, () => s.CountersteerGain, v => s.CountersteerGain = v, "", 2),
                    new TuningSlider("controllerassist.tuning.assiststrength", "controllerassist.tuning.assiststrength.help",
                        0, 100, 1, () => s.AssistStrength * 100, v => s.AssistStrength = v / 100.0, "%", 0),
                    new TuningSlider("controllerassist.tuning.steerrateatspeed", "controllerassist.tuning.steerrateatspeed.help",
                        0, 1500, 1, () => s.SteerRateAtSpeedPercentPerSec, v => s.SteerRateAtSpeedPercentPerSec = v, " %/s", 0),
                    new TuningSlider("controllerassist.tuning.frontgriplimit", "controllerassist.tuning.frontgriplimit.help",
                        0, 15, 10, () => s.FrontGripLimitDeg, v => s.FrontGripLimitDeg = v, "°", 1),
                    new TuningSlider("controllerassist.tuning.brakesteer", "controllerassist.tuning.brakesteer.help",
                        0, 80, 1, () => s.BrakeSteerReductionPercent, v => s.BrakeSteerReductionPercent = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.straightstability", "controllerassist.tuning.straightstability.help",
                        0, 100, 1, () => s.StraightStabilityPercent, v => s.StraightStabilityPercent = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.spincatch", "controllerassist.tuning.spincatch.race.help",
                        0, 100, 1, () => s.TargetAngleSpinCatch, v => s.TargetAngleSpinCatch = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.allowedslip", "controllerassist.tuning.allowedslip.help",
                        0, 30, 10, () => s.SpinCatchAllowedSlipDeg, v => s.SpinCatchAllowedSlipDeg = v, "°", 1),
                }));
                sections.Add(("controllerassist.tuning.section.throttle", new[]
                {
                    new TuningSlider("controllerassist.tuning.throttlespincut", "controllerassist.tuning.throttlespincut.help",
                        0, 100, 1, () => s.ThrottleSpinCutPercent, v => s.ThrottleSpinCutPercent = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.throttleramp", "controllerassist.tuning.throttleramp.help",
                        0, 100, 1, () => s.ThrottleRampPercent, v => s.ThrottleRampPercent = v, "%", 0),
                    new TuningSlider("controllerassist.tuning.tractioncontrol", "controllerassist.tuning.tractioncontrol.help",
                        0, 100, 1, () => s.TractionControlPercent, v => s.TractionControlPercent = v, "%", 0),
                }));
            }

            sections.Add(("controllerassist.tuning.section.output", new[]
            {
                new TuningSlider("controllerassist.tuning.minactivationspeed", "controllerassist.tuning.minactivationspeed.help",
                    0, 20, 10, () => s.MinActivationSpeedKmh, v => s.MinActivationSpeedKmh = v, " km/h", 1),
                new TuningSlider("controllerassist.tuning.smoothing", "controllerassist.tuning.smoothing.help",
                    0, 200, 1, () => s.SmoothingMs, v => s.SmoothingMs = v, " ms", 0),
            }));

            return sections;
        }

        private static readonly (string LabelKey, string HelpKey)[] ControllerAssistRumbleHelpItems =
        {
            ("controllerassist.rumble.strength", "controllerassist.rumble.strength.help"),
            ("controllerassist.rumble.loadweight", "controllerassist.rumble.loadweight.help"),
            ("controllerassist.rumble.roadweight", "controllerassist.rumble.roadweight.help"),
            ("controllerassist.rumble.impactweight", "controllerassist.rumble.impactweight.help"),
            ("controllerassist.rumble.surfaceweight", "controllerassist.rumble.surfaceweight.help"),
            ("controllerassist.rumble.shiftweight", "controllerassist.rumble.shiftweight.help"),
            ("controllerassist.rumble.jerksensitivity", "controllerassist.rumble.jerksensitivity.help"),
            ("controllerassist.rumble.loadslipactivation", "controllerassist.rumble.loadslipactivation.help"),
            ("controllerassist.rumble.loadslipfull", "controllerassist.rumble.loadslipfull.help"),
            ("controllerassist.rumble.roadgain", "controllerassist.rumble.roadgain.help"),
            ("controllerassist.rumble.impactgain", "controllerassist.rumble.impactgain.help"),
            ("controllerassist.rumble.responsecurve", "controllerassist.rumble.responsecurve.help"),
            ("controllerassist.rumble.directionalbias", "controllerassist.rumble.directionalbias.help"),
        };

        private void ShowSliderHelpPopup(Form ownerForm, string titleKey, (string LabelKey, string HelpKey)[] items)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 460, 600, out _);

            MakeLabel(card, Localization.T(titleKey), 20, 15, 380, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            var text = new RichTextBox
            {
                Location = new Point(20, 50),
                Size = new Size(420, 470),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,
                Font = UiFonts.Get("Segoe UI", 9.5f),
                WordWrap = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            Font boldFont = UiFonts.Get("Segoe UI Semibold", 9.5f);
            foreach (var item in items)
            {
                if (text.TextLength > 0) text.AppendText(Environment.NewLine + Environment.NewLine);
                text.SelectionStart = text.TextLength;
                text.SelectionFont = boldFont;
                text.AppendText(Localization.T(item.LabelKey));
                text.SelectionStart = text.TextLength;
                text.SelectionFont = text.Font;
                text.AppendText(Environment.NewLine + Localization.T(item.HelpKey));
            }
            text.SelectionStart = 0;
            card.Controls.Add(text);

            var okBtn = MakeButton(card, Localization.T("common.ok"), 20, 536, 420, 32, ApplePalette.Blue);
            okBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            okBtn.Click += (s, e) => popup.Close();

            ShowModalPopup(popup, ownerForm);
        }

        private void ShowControllerAssistTuningMenu(Form? ownerForm = null)
        {
            var s = _assistEngine.Settings;
            var sections = GetTuningSections(s);
            var helpItems = sections
                .SelectMany(section => section.Sliders)
                .Select(t => (t.LabelKey, t.HelpKey))
                .ToArray();

            const int popupW = 600, colW = 270, colGap = 20, rowH = 50, sectionH = 26, logRowH = 46;
            int rows = sections.Sum(section => (section.Sliders.Length + 1) / 2);
            int popupH = 55 + sections.Count * sectionH + rows * rowH + logRowH + 15;

            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, popupW, popupH, out _,
                onHelp: () => ShowSliderHelpPopup(popup, "controllerassist.tuning.help.title", helpItems));

            MakeLabel(card, Localization.T("controllerassist.tuning.title"), 20, 15, 400, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            int y = 55;
            foreach (var (sectionKey, sliders) in sections)
            {
                MakeLabel(card, Localization.T(sectionKey), 20, y, popupW - 40, 18,
                    ApplePalette.Secondary, UiFonts.Get("Segoe UI Semibold", 9f));
                y += sectionH;

                for (int i = 0; i < sliders.Length; i++)
                {
                    int x = i % 2 == 0 ? 20 : 20 + colW + colGap;
                    AddTuningSlider(card, s, sliders[i], x, y, colW);
                    if (i % 2 == 1 || i == sliders.Length - 1)
                        y += rowH;
                }
            }

            var logBtn = MakeButton(card, "", 20, y + 6, colW, 30, ApplePalette.Blue);
            void RefreshLogButton()
            {
                logBtn.Text = Localization.T(_assistRecorder.IsRecording ? "controllerassist.log.stop" : "controllerassist.log.start");
                SetButtonColor(logBtn, _assistRecorder.IsRecording ? ApplePalette.Green : ApplePalette.Blue);
            }
            RefreshLogButton();
            logBtn.Click += (sd, e) =>
            {
                if (_assistRecorder.IsRecording) StopAssistLog();
                else StartAssistLog();
                RefreshLogButton();
            };

            var folderBtn = MakeButton(card, Localization.T("controllerassist.log.folder"), 20 + colW + colGap, y + 6, colW, 30, ApplePalette.Blue);
            folderBtn.Click += (sd, e) => OpenAssistLogFolder();

            ShowModalPopup(popup, ownerForm);
        }

        private readonly AssistRecorder _assistRecorder = new();

        private static string AssistLogDir => Path.Combine(AppContext.BaseDirectory, "AssistLogs");

        private void StartAssistLog()
        {
            try
            {
                string path = _assistRecorder.Start(AssistLogDir, BuildAssistLogHeader());
                AppendStatusMessage(string.Format(Localization.T("controllerassist.log.started"), path), StatusSuccessColor);
            }
            catch (Exception ex)
            {
                AppendStatusMessage(ex.Message, StatusErrorColor);
            }
        }

        private void StopAssistLog()
        {
            var result = _assistRecorder.Stop();
            if (result is { } r)
                AppendStatusMessage(string.Format(Localization.T("controllerassist.log.saved"), r.Segments, r.SummaryPath), StatusSuccessColor);
        }

        private void OpenAssistLogFolder()
        {
            try
            {
                Directory.CreateDirectory(AssistLogDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AssistLogDir}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendStatusMessage(ex.Message, StatusErrorColor);
            }
        }

        private string BuildAssistLogHeader()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            string settingsJson = JsonSerializer.Serialize(_assistEngine.Settings, options);
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            return $"App version: {version}\nCar: {_currentCarName}\nAssist character: {_assistEngine.AssistCharacterPercent:F0}\nSteering assist on: {_assistEngine.MasterEnabled}\nAssist settings:\n{settingsJson}";
        }

        private void AddTuningSlider(Control card, AssistSettings s, TuningSlider t, int x, int y, int width)
        {
            MakeLabel(card, Localization.T(t.LabelKey), x, y, width, 18, ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));

            double initial = t.Get();
            var slider = new MacSlider
            {
                Location = new Point(x, y + 20),
                Size = new Size(width - 70, 22),
                Minimum = (int)Math.Round(t.Min * t.TickScale),
                Maximum = (int)Math.Round(t.Max * t.TickScale),
                Value = (int)Math.Round(initial * t.TickScale),
            };
            var valLabel = MakeLabel(card, FormatSliderValue(initial, t.Decimals, t.Suffix), x + width - 65, y + 20, 65, 22,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));

            slider.ValueChanged += (sd, e) =>
            {
                double v = slider.Value / t.TickScale;
                t.Set(v);
                valLabel.Text = FormatSliderValue(v, t.Decimals, t.Suffix);
                s.Preset = AssistPreset.Custom;
                RefreshControllerAssistPresetButtons();
                SaveSettings();
            };
            card.Controls.Add(slider);
        }

        private void ShowControllerAssistRumbleMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 420, 760, out _,
                onHelp: () => ShowSliderHelpPopup(popup, "controllerassist.rumble.help.title", ControllerAssistRumbleHelpItems));

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

            ShowModalPopup(popup, ownerForm);
        }
    }
}
