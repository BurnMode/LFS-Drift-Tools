using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        public class WheelSetupForm : Form
        {
            private readonly MainForm _owner;
            private readonly SteeringWheelInput _wheelInput;
            private readonly List<(Guid Guid, string Name)> _devices;

            public Guid SelectedDeviceGuid { get; private set; } = Guid.Empty;
            public JoystickOffset SelectedAxis { get; private set; } = JoystickOffset.X;

            private static readonly JoystickOffset[] AxisChoices = new[]
            {
                JoystickOffset.X, JoystickOffset.Y, JoystickOffset.Z,
                JoystickOffset.RotationX, JoystickOffset.RotationY, JoystickOffset.RotationZ
            };

            private Label[]? _axisBars;
            private Label? _calibHint;
            private bool _calibrating = false;
            private readonly Dictionary<JoystickOffset, int> _axisMin = new();
            private readonly Dictionary<JoystickOffset, int> _axisMax = new();
            private System.Windows.Forms.Timer? _calibTimer;

            private readonly Panel _content;

            public WheelSetupForm(MainForm owner, SteeringWheelInput wheelInput, List<(Guid Guid, string Name)> devices)
            {
                _owner = owner;
                _wheelInput = wheelInput;
                _devices = devices;

                Text = Localization.T("wheelconfig.title");
                StartPosition = FormStartPosition.CenterParent;

                var card = _owner.BuildPopupChrome(this, 380, 460, out _);

                _owner.MakeLabel(card, Localization.T("wheelconfig.title"), 20, 15, 250, 24,
                    ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

                _content = new Panel
                {
                    Location = new Point(20, 50),
                    Size = new Size(Width - 40, Height - 50 - 20),
                    BackColor = Color.Transparent,
                };
                card.Controls.Add(_content);

                ShowDeviceList();
            }

            private void ShowDeviceList()
            {
                _content.Controls.Clear();

                var title = new Label
                {
                    Text = Localization.T("wheelconfig.nodetectauto"),
                    Location = new Point(0, 0),
                    Size = new Size(_content.Width, 40),
                    ForeColor = ApplePalette.Text,
                    Font = UiFonts.Get("Segoe UI", 9.5f),
                };
                _content.Controls.Add(title);

                int y = 46;

                if (_devices.Count == 0)
                {
                    _content.Controls.Add(new Label
                    {
                        Text = Localization.T("wheelconfig.nodetect"),
                        Location = new Point(0, y),
                        Size = new Size(_content.Width, 40),
                        ForeColor = ApplePalette.Secondary
                    });
                    y += 46;
                }

                foreach (var d in _devices)
                {
                    bool isXInput = SteeringWheelInput.IsXInputPad(d.Name);
                    if (isXInput && _owner.VirtualPadMayBePresent && d.Guid != _wheelInput.DeviceGuid)
                        continue;

                    string label = isXInput
                        ? $"{d.Name}   {Localization.T("controllerassist.badge")}"
                        : d.Name;
                    var btn = _owner.MakeButton(_content, label, 0, y, _content.Width, 78);

                    var guid = d.Guid;
                    btn.Click += (s, e) =>
                    {
                        SelectedDeviceGuid = guid;
                        ShowAxisCalibration();
                    };

                    y += 84;
                }

                var cancelBtn = _owner.MakeButton(_content, Localization.T("wheelconfig.abort"),
                    0, _content.Height - 34, _content.Width, 30);
                cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            }

            private const int CalibrationSeconds = 10;

            private void ShowAxisCalibration()
            {
                _content.Controls.Clear();

                _wheelInput.ConnectToDevice(SelectedDeviceGuid, JoystickOffset.X);

                var title = new Label
                {
                    Text = Localization.T("wheelconfig.calibrateinfo"),
                    Location = new Point(0, 0),
                    Size = new Size(_content.Width, 40),
                    ForeColor = ApplePalette.Text,
                    Font = UiFonts.Get("Segoe UI", 9.5f),
                };
                _content.Controls.Add(title);

                _axisBars = new Label[AxisChoices.Length];
                int y = 44;
                foreach (var axis in AxisChoices)
                {
                    int idx = Array.IndexOf(AxisChoices, axis);
                    var lbl = new Label
                    {
                        Text = $"{axis}: 0",
                        Location = new Point(0, y),
                        Size = new Size(_content.Width, 18),
                        ForeColor = ApplePalette.Secondary,
                        Font = new Font("Consolas", 9f)
                    };
                    _content.Controls.Add(lbl);
                    _axisBars[idx] = lbl;
                    y += 20;
                }

                _calibHint = new Label
                {
                    Text = string.Format(Localization.T("wheelconfig.detection"), CalibrationSeconds),
                    Location = new Point(0, y + 6),
                    Size = new Size(_content.Width, 18),
                    ForeColor = ApplePalette.Secondary
                };
                _content.Controls.Add(_calibHint);

                var manualLabel = new Label
                {
                    Text = Localization.T("wheelconfig.manualset"),
                    Location = new Point(0, y + 28),
                    Size = new Size(_content.Width, 16),
                    ForeColor = ApplePalette.Secondary
                };
                _content.Controls.Add(manualLabel);

                int mx = 0, my = y + 46;
                foreach (var axis in AxisChoices)
                {
                    var b = _owner.MakeButton(_content, axis.ToString(), mx, my, 105, 30);

                    var ax = axis;
                    b.Click += (s, e) => Confirm(ax);

                    mx += 112;
                    if (mx > 220) { mx = 0; my += 34; }
                }

                var backBtn = _owner.MakeButton(_content, Localization.T("wheelconfig.otherdevice"),
                    0, _content.Height - 34, _content.Width, 30);
                backBtn.Click += (s, e) => { StopCalibration(); ShowDeviceList(); };

                _axisMin.Clear();
                _axisMax.Clear();
                _calibrating = true;
                _wheelInput.RawAxesChanged += OnRawAxes;

                int secondsLeft = CalibrationSeconds;
                _calibTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _calibTimer.Tick += (s, e) =>
                {
                    secondsLeft--;
                    if (secondsLeft > 0)
                    {
                        if (_calibHint != null)
                            _calibHint.Text = string.Format(Localization.T("wheelconfig.detection"), secondsLeft);
                        return;
                    }

                    _calibTimer.Stop();
                    _calibrating = false;
                    _wheelInput.RawAxesChanged -= OnRawAxes;

                    JoystickOffset best = JoystickOffset.X;
                    int bestRange = -1;
                    foreach (var axis in AxisChoices)
                    {
                        int range = (_axisMax.TryGetValue(axis, out var mx2) ? mx2 : 0)
                                  - (_axisMin.TryGetValue(axis, out var mn) ? mn : 0);
                        if (range > bestRange) { bestRange = range; best = axis; }
                    }

                    if (bestRange > AxisCalibrationMinRange)
                        Confirm(best);
                    else if (_calibHint != null)
                        _calibHint.Text = Localization.T("wheelconfig.nomotion");
                };
                _calibTimer.Start();

                FormClosed += (s, e) => StopCalibration();
            }

            private void OnRawAxes(Dictionary<JoystickOffset, int> values)
            {
                if (InvokeRequired) { BeginInvoke((Action)(() => OnRawAxes(values))); return; }
                if (!_calibrating || _axisBars == null) return;

                foreach (var axis in AxisChoices)
                {
                    if (!values.TryGetValue(axis, out var v)) continue;

                    int idx = Array.IndexOf(AxisChoices, axis);
                    if (idx >= 0 && idx < _axisBars.Length)
                        _axisBars[idx].Text = $"{axis}: {v}";

                    if (!_axisMin.ContainsKey(axis) || v < _axisMin[axis]) _axisMin[axis] = v;
                    if (!_axisMax.ContainsKey(axis) || v > _axisMax[axis]) _axisMax[axis] = v;
                }
            }

            private void StopCalibration()
            {
                _calibrating = false;
                _calibTimer?.Stop();
                _calibTimer?.Dispose();
                _calibTimer = null;
                _wheelInput.RawAxesChanged -= OnRawAxes;
            }

            private void Confirm(JoystickOffset axis)
            {
                StopCalibration();
                SelectedAxis = axis;
                _wheelInput.SetSteeringAxis(axis);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        public class KeyBindingForm : Form
        {
            public InputBinding Result { get; private set; } = InputBinding.None;
            private readonly Label _instructionLabel;
            private readonly MainForm _owner;
            private readonly SteeringWheelInput? _wheelInput;
            private readonly bool _allowModifiers;
            private readonly string _keyName;

            public KeyBindingForm(MainForm owner, string keyName, SteeringWheelInput? wheelInput = null, bool allowModifiers = false)
            {
                _owner = owner;
                _wheelInput = wheelInput;
                _allowModifiers = allowModifiers;
                _keyName = keyName;

                Text = Localization.T("keybind.dialog.title");
                StartPosition = FormStartPosition.CenterParent;
                KeyPreview = true;

                var card = _owner.BuildPopupChrome(this, 360, 180, out _);

                _owner.MakeLabel(card, Localization.T("keybind.dialog.title"), 20, 15, 260, 24,
                    ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

                _instructionLabel = _owner.MakeLabel(card, string.Format(Localization.T("keybind.dialog.press"), keyName),
                    20, 55, Width - 40, 80, ApplePalette.Text, UiFonts.Get("Segoe UI", 11f), ContentAlignment.MiddleCenter);

                if (_wheelInput != null)
                    _wheelInput.AnyButtonPressed += WheelInput_AnyButtonPressed;

                FormClosed += (s, e) =>
                {
                    if (_wheelInput != null)
                        _wheelInput.AnyButtonPressed -= WheelInput_AnyButtonPressed;
                };
            }

            private const int FeedbackDelayMs = 300;
            private bool _closing;

            private void CloseAfterFeedback()
            {
                _closing = true;
                var timer = new System.Windows.Forms.Timer { Interval = FeedbackDelayMs };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    DialogResult = DialogResult.OK;
                    Close();
                };
                timer.Start();
            }

            private void WheelInput_AnyButtonPressed(int buttonIndex)
            {
                if (_closing) return;

                Result = InputBinding.FromWheelButton(buttonIndex);

                _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.bound"), $"Wheel Btn {buttonIndex}");
                _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                CloseAfterFeedback();
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                bool keyDown = msg.Msg == 0x0100 || (_allowModifiers && msg.Msg == 0x0104);

                if (keyDown && _closing)
                    return true;

                if (keyDown)
                {
                    Keys baseKey = keyData & Keys.KeyCode;

                    if (_allowModifiers && baseKey is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
                    {
                        _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.press"), _keyName)
                            + "\n" + InputBinding.ModifierPrefix(keyData & Keys.Modifiers) + "…";
                        return true;
                    }

                    if (_allowModifiers && baseKey != Keys.Escape && baseKey != Keys.Delete)
                    {
                        Result = InputBinding.FromKey(baseKey, keyData & Keys.Modifiers);
                        _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.bound"), Result.ToString());
                        _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                        CloseAfterFeedback();
                        return true;
                    }

                    if (baseKey == Keys.Escape)
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                        return true;
                    }

                    if (baseKey == Keys.Delete)
                    {
                        Result = InputBinding.None;
                        _instructionLabel.Text = Localization.T("keybind.dialog.unbound");
                        _instructionLabel.ForeColor = Color.FromArgb(230, 150, 80);

                        CloseAfterFeedback();
                        return true;
                    }

                    Result = InputBinding.FromKey(baseKey);
                    _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.bound"), baseKey);
                    _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                    CloseAfterFeedback();
                    return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        public class RevLimiterCalibrationPromptForm : Form
        {
            private readonly MainForm _owner;
            private readonly Label _messageLabel;
            private readonly Label _liveRpmLabel;
            private readonly Button _calibrateButton;
            private readonly Func<Task> _startCalibration;

            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WS_EX_TOOLWINDOW = 0x00000080;

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    return cp;
                }
            }

            public RevLimiterCalibrationPromptForm(MainForm owner, string carName, Func<Task> startCalibration, bool enterHintAvailable, bool firstTimeForCar = true)
            {
                _owner = owner;
                _startCalibration = startCalibration;

                Text = Localization.T("rev.calibration_prompt.title");
                StartPosition = FormStartPosition.CenterScreen;
                TopMost = true;

                var card = _owner.BuildPopupChrome(this, 440, 250, out _);

                _owner.MakeLabel(card, Localization.T("rev.calibration_prompt.title"), 20, 15, 300, 24,
                    ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

                string bodyTemplate = firstTimeForCar
                    ? Localization.T("rev.calibration_prompt.body")
                    : Localization.T("rev.calibration_prompt.manual_body");
                string bodyText = string.Format(bodyTemplate, carName)
                    + (enterHintAvailable ? Localization.T("rev.calibration_prompt.enter_hint") : "");

                _messageLabel = _owner.MakeLabel(card, bodyText, 20, 55, Width - 40, 74,
                    ApplePalette.Text, UiFonts.Get("Segoe UI", 9.5f), ContentAlignment.TopLeft);

                _liveRpmLabel = _owner.MakeLabel(card, "", 20, 55 + 74, Width - 40, 34,
                    ApplePalette.Blue, UiFonts.Get("Segoe UI Semibold", 20f, FontStyle.Bold), ContentAlignment.MiddleCenter);
                _liveRpmLabel.Visible = false;

                _calibrateButton = _owner.MakeButton(card, Localization.T("rev.calibrate"),
                    20, Height - 56, Width - 40, 30);
                _calibrateButton.Click += async (s, e) =>
                {
                    _calibrateButton.Enabled = false;
                    await _startCalibration();
                };

                AcceptButton = _calibrateButton;
            }

            public void ShowCalibratingState()
            {
                _messageLabel.Text = Localization.T("rev.calibration_prompt.inprogress");
                _messageLabel.TextAlign = ContentAlignment.MiddleCenter;
                _liveRpmLabel.Visible = true;
                _liveRpmLabel.Text = "0 RPM";
                _calibrateButton.Enabled = false;
            }

            public void UpdateLiveRpm(int rpm)
            {
                if (!_liveRpmLabel.Visible) return;
                _liveRpmLabel.Text = rpm.ToString("N0") + " RPM";
            }

            public void ShowDoneState(int finalRpm)
            {
                _messageLabel.Text = Localization.T("rev.calibration_prompt.done");
                _messageLabel.TextAlign = ContentAlignment.MiddleCenter;

                _liveRpmLabel.Text = finalRpm.ToString("N0") + " RPM";
                _liveRpmLabel.Visible = true;
                _calibrateButton.Visible = false;
            }
        }

        private readonly (Color color, string code)[] InSimPalette =
        {
        (Color.Black,      "^0"),
        (Color.Red,        "^1"),
        (Color.LimeGreen,  "^2"),
        (Color.Yellow,     "^3"),
        (Color.Blue,       "^4"),
        (Color.Magenta,    "^5"),
        (Color.Cyan,       "^6"),
        (Color.White,      "^7"),
        (Color.Gray,       "^8"),
        (Color.LightBlue,  "^9"),
    };

        private void ShowHudColorsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            bool isType2 = _overlay.SpeedoTachoType == SpeedoPresetData.TachoType2;
            var card = BuildPopupChrome(popup, 300, isType2 ? 430 : 310, out _);

            MakeLabel(card, Localization.T("hud.colors"), 20, 15, 200, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("hud.colors.subtitle"), 20, 40, 250, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f));

            int y = 66;
            void AddRow(string labelKey, Func<Color> getColor, Action<Color> setColor)
            {
                string label = Localization.T(labelKey);
                MakeLabel(card, label, 20, y + 6, 150, 20, ApplePalette.Text);

                var swatch = MakeButton(card, "", 195, y, 85, 30, getColor());
                swatch.Click += (s, e) => OpenCustomColorPicker(swatch, getColor(), label, setColor, includeAlpha: true, ownerForm: ownerForm);
                card.Controls.Add(swatch);

                y += 40;
            }

            AddRow("hud.color.redline", () => _speedoTachoRedlineColor, c => { _speedoTachoRedlineColor = c; _overlay.RedlineColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.text", () => _speedoTachoTextColor, c => { _speedoTachoTextColor = c; _overlay.SpeedoTachoTextColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.indicator", () => _speedoTachoIndicatorColor, c => { _speedoTachoIndicatorColor = c; _overlay.SpeedoTachoIndicatorColor = c; RefreshSpeedoPreview(); });
            if (isType2)
                AddRow("hud.color.mid", () => _speedoTachoMidColor, c => { _speedoTachoMidColor = c; _overlay.SpeedoTachoMidColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.ticks", () => _speedoTachoTickColor, c => { _speedoTachoTickColor = c; _overlay.SpeedoTachoTickColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.background", () => _speedoTachoBackgroundColor, c => { _speedoTachoBackgroundColor = c; _overlay.SpeedoTachoBackgroundColor = c; RefreshSpeedoPreview(); });
            if (isType2)
            {
                AddRow("hud.color.fuel", () => _speedoTachoFuelColor, c => { _speedoTachoFuelColor = c; _overlay.SpeedoTachoFuelColor = c; RefreshSpeedoPreview(); });
                AddRow("hud.color.turbo", () => _speedoTachoTurboColor, c => { _speedoTachoTurboColor = c; _overlay.SpeedoTachoTurboColor = c; RefreshSpeedoPreview(); });
            }

            ShowModalPopup(popup, ownerForm);
        }

        private void ShowScoreColorsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 380, 400, out _);

            MakeLabel(card, Localization.T("hud.scorecolors"), 20, 15, 300, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("hud.scorecolors.subtitle"), 20, 40, 320, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f));

            int y = 66;
            Button AddColorRow(string label, Color initialColor, int slot)
            {
                MakeLabel(card, label, 20, y + 6, 260, 20, ApplePalette.Text);

                var swatch = MakeButton(card, "", 290, y, 70, 30, initialColor);
                swatch.Click += (s, e) => HandleColorButtonClick(swatch, slot, ownerForm);

                y += 40;
                return swatch;
            }

            bool useOverlayColors = _showOverlayCheck != null && _showOverlayCheck.Checked
                                  && (_showHudCheck == null || !_showHudCheck.Checked);

            _colorBtnIdle = AddColorRow(Localization.T("hud.scorecolor.idle"),
                useOverlayColors ? OverlayColorIdle : InSimCodeToColor(InSimColorIdle), 0);
            _colorBtn1 = AddColorRow(Localization.T("hud.scorecolor.default"),
                useOverlayColors ? OverlayColor1 : InSimCodeToColor(InSimColor1), 1);
            _colorBtn2 = AddColorRow(string.Format(Localization.T("hud.colorlevel"), 1, Localization.T("drift.label.angle_good")),
                useOverlayColors ? OverlayColor2 : InSimCodeToColor(InSimColor2), 2);
            _colorBtn3 = AddColorRow(string.Format(Localization.T("hud.colorlevel"), 2, Localization.T("drift.label.angle_high")),
                useOverlayColors ? OverlayColor3 : InSimCodeToColor(InSimColor3), 3);
            _colorBtn4 = AddColorRow(string.Format(Localization.T("hud.colorlevel"), 3, Localization.T("drift.label.angle_extreme")),
                useOverlayColors ? OverlayColor4 : InSimCodeToColor(InSimColor4), 4);
            _colorBtn5 = AddColorRow(string.Format(Localization.T("hud.colorlevel"), 4, Localization.T("drift.label.angle_ultraextreme")),
                useOverlayColors ? OverlayColor5 : InSimCodeToColor(InSimColor5), 5);
            _colorBtn6 = AddColorRow(Localization.T("hud.scorecolor.backward"),
                useOverlayColors ? OverlayColor6 : InSimCodeToColor(InSimColor6), 6);

            try { ShowModalPopup(popup, ownerForm); }
            finally { _colorBtnIdle = _colorBtn1 = _colorBtn2 = _colorBtn3 = _colorBtn4 = _colorBtn5 = _colorBtn6 = null; }
        }

        private void ShowAngleLevelsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 340, 356, out _);

            MakeLabel(card, Localization.T("score.levels.title"), 20, 15, 290, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("score.levels.angle_subtitle"), 20, 40, 300, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.angle_header"), 20, y, 290, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI Semibold", 9f));
            y += 26;

            double[] angleValues = _drift.AngleLevelThresholds.ToArray();
            void AddAngleRow(string label, int index)
            {
                MakeLabel(card, label, 20, y + 5, 190, 20, ApplePalette.Text);
                MakeNumericUpDown(card, 220, y, 90, 26, 0, 150, (decimal)angleValues[index], 1,
                    v =>
                    {
                        angleValues[index] = (double)v;
                        _drift.SetAngleLevelThresholds(angleValues);
                        SaveSettings();
                    });
                y += 36;
            }

            AddAngleRow(Localization.T("score.levels.angle_base"), 0);
            AddAngleRow(Localization.T("drift.label.angle_good"), 1);
            AddAngleRow(Localization.T("drift.label.angle_high"), 2);
            AddAngleRow(Localization.T("drift.label.angle_extreme"), 3);
            AddAngleRow(Localization.T("drift.label.angle_ultraextreme"), 4);

            MakeLabel(card, Localization.T("score.levels.angle_backward"), 20, y + 5, 190, 20, ApplePalette.Text);
            MakeNumericUpDown(card, 220, y, 90, 26, 0, 180, (decimal)_drift.BackwardDriftAngleThreshold, 1,
                v =>
                {
                    _drift.SetBackwardDriftAngleThreshold((double)v);
                    SaveSettings();
                });
            y += 36;

            ShowModalPopup(popup, ownerForm);
        }

        private void ShowSpeedLevelsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 340, 320, out _);

            MakeLabel(card, Localization.T("score.levels.speed_title"), 20, 15, 290, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("score.levels.speed_subtitle"), 20, 40, 300, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.speed_header"), 20, y, 290, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI Semibold", 9f));
            y += 26;

            double[] speedValues = _drift.SpeedLevelThresholds.ToArray();
            void AddSpeedRow(string label, int index)
            {
                MakeLabel(card, label, 20, y + 5, 190, 20, ApplePalette.Text);
                MakeNumericUpDown(card, 220, y, 90, 26, 0, 400, (decimal)speedValues[index], 1,
                    v =>
                    {
                        speedValues[index] = (double)v;
                        _drift.SetSpeedLevelThresholds(speedValues);
                        SaveSettings();
                    });
                y += 36;
            }

            AddSpeedRow(Localization.T("score.levels.speed_base"), 0);
            AddSpeedRow(string.Format(Localization.T("score.levels.speed_level"), 2), 1);
            AddSpeedRow(string.Format(Localization.T("score.levels.speed_level"), 3), 2);
            AddSpeedRow(string.Format(Localization.T("score.levels.speed_level"), 4), 3);
            AddSpeedRow(string.Format(Localization.T("score.levels.speed_level"), 5), 4);

            ShowModalPopup(popup, ownerForm);
        }

        private void ShowHitLevelsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 340, 320, out _);

            MakeLabel(card, Localization.T("derby.levels.title"), 20, 15, 290, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("derby.levels.subtitle"), 20, 40, 300, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.speed_header"), 20, y, 290, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI Semibold", 9f));
            y += 26;

            double[] hitValues = _collisions.HitLevelThresholds.ToArray();
            void AddHitRow(string label, int index)
            {
                MakeLabel(card, label, 20, y + 5, 190, 20, ApplePalette.Text);
                MakeNumericUpDown(card, 220, y, 90, 26, 0, 400, (decimal)hitValues[index], 1,
                    v =>
                    {
                        hitValues[index] = (double)v;
                        _collisions.SetHitLevelThresholds(hitValues);
                        SaveSettings();
                    });
                y += 36;
            }

            AddHitRow(Localization.T("derby.levels.tier1"), 0);
            AddHitRow(Localization.T("derby.levels.tier2"), 1);
            AddHitRow(Localization.T("derby.levels.tier3"), 2);
            AddHitRow(Localization.T("derby.levels.tier4"), 3);
            AddHitRow(Localization.T("derby.levels.tier5"), 4);

            ShowModalPopup(popup, ownerForm);
        }

        private void ShowSpeedoElementsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            bool isType1 = _overlay.SpeedoTachoType != SpeedoPresetData.TachoType2;
            int rowCount = isType1 ? 9 : 6;
            int popupHeight = 84 + rowCount * 40 + (isType1 ? 60 : 0) + 26;
            var card = BuildPopupChrome(popup, 340, popupHeight, out _);

            MakeLabel(card, Localization.T("speedometer.elements.title"), 20, 15, 290, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("speedometer.elements.subtitle"), 20, 40, 300, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 84;
            void AddToggleRow(string labelKey, bool initial, Action<bool> onChange)
            {
                MakeLabel(card, Localization.T(labelKey), 20, y + 4, 220, 22, ApplePalette.Text);
                var toggle = new MacToggleSwitch { Location = new Point(280, y), Size = new Size(40, 20), Checked = initial };
                toggle.CheckedChanged += (s, e) =>
                {
                    onChange(toggle.Checked);
                    SaveSettings();
                };
                card.Controls.Add(toggle);
                y += 40;
            }

            if (isType1) AddToggleRow("speedometer.elements.rpmdigits", _overlay.SpeedoTachoShowRpmDigits, v =>
            {
                _overlay.SpeedoTachoShowRpmDigits = v;
                _speedoTachoPreview.ShowRpmDigits = v;
                _speedoTachoPreview.Invalidate();
            });
            AddToggleRow("speedometer.elements.unit", _overlay.SpeedoTachoShowUnit, v =>
            {
                _overlay.SpeedoTachoShowUnit = v;
                _speedoTachoPreview.ShowUnit = v;
                _speedoTachoPreview.Invalidate();
            });
            if (isType1) AddToggleRow("speedometer.elements.minorticks", _overlay.SpeedoTachoShowMinorTicks, v =>
            {
                _overlay.SpeedoTachoShowMinorTicks = v;
                _speedoTachoPreview.ShowMinorTicks = v;
                _speedoTachoPreview.Invalidate();
            });
            AddToggleRow("speedometer.elements.majorticks", _overlay.SpeedoTachoShowMajorTicks, v =>
            {
                _overlay.SpeedoTachoShowMajorTicks = v;
                _speedoTachoPreview.ShowMajorTicks = v;
                _speedoTachoPreview.Invalidate();
            });
            AddToggleRow("speedometer.elements.plaindigits", _overlay.SpeedoTachoPlainDigits, v =>
            {
                _overlay.SpeedoTachoPlainDigits = v;
                _speedoTachoPreview.PlainDigits = v;
                _speedoTachoPreview.Invalidate();
            });
            AddToggleRow("speedometer.elements.instantspeed", _overlay.SpeedoTachoInstantSpeed, v =>
            {
                _overlay.SpeedoTachoInstantSpeed = v;
            });
            AddToggleRow("speedometer.elements.airspeed", _overlay.SpeedoTachoAirSpeed, v =>
            {
                _overlay.SpeedoTachoAirSpeed = v;
            });
            AddToggleRow("speedometer.elements.telltales", _overlay.SpeedoTachoShowTelltales, v =>
            {
                _overlay.SpeedoTachoShowTelltales = v;
                _speedoTachoPreview.ShowTelltales = v;
                _speedoTachoPreview.Invalidate();
            });
            if (isType1) AddToggleRow("speedometer.elements.centerunit", _overlay.SpeedoTachoCenterUnit, v =>
            {
                _overlay.SpeedoTachoCenterUnit = v;
                _speedoTachoPreview.CenterUnit = v;
                _speedoTachoPreview.Invalidate();
            });

            if (isType1)
            {
                MakeLabel(card, Localization.T("speedometer.elements.redlinethickness"), 20, y, 250, 20, ApplePalette.Text);
                y += 20;
                _redlineThicknessSlider = new MacSlider
                {
                    Location = new Point(20, y),
                    Size = new Size(300, 22),
                    Minimum = 10,
                    Maximum = 145,
                    Value = (int)Math.Round(_redlineThickness * 10),
                };
                _redlineThicknessSlider.ValueChanged += (s, e) =>
                {
                    _redlineThickness = _redlineThicknessSlider!.Value / 10f;
                    _overlay.RedlineThickness = _redlineThickness;
                    _speedoTachoPreview.RedlineThickness = _redlineThickness;
                    _speedoTachoPreview.Invalidate();
                    SaveSettings();
                };
                card.Controls.Add(_redlineThicknessSlider);
                y += 40;
            }

            try { ShowModalPopup(popup, ownerForm); }
            finally { _redlineThicknessSlider = null; }
        }
    }
}
