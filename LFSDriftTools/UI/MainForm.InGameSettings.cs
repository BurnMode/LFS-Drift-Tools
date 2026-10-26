using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        private const int OvWidth = 660;
        private const int OvHeaderHeight = 46;
        private const int OvSectionGap = 8;
        private const int OvBodyGap = 4;
        private const int OvPad = 18;
        private const int OvColumnGap = 24;
        private const int OvColumnWidth = (OvWidth - OvPad * 2 - OvColumnGap) / 2;
        private const int OvRowGap = 10;
        private const int OvLiveIntervalMs = 500;

        private sealed class OvSection
        {
            public RoundedPanel Header = null!;
            public Label Chevron = null!;
            public Label Summary = null!;
            public RoundedPanel Body = null!;
            public Func<(string Text, Color Color)>? GetSummary;
        }

        private sealed class OvColumns
        {
            private readonly int[] _y = { OvPad, OvPad };

            public static int X(int column) => OvPad + column * (OvColumnWidth + OvColumnGap);

            public int Take(int column, int height)
            {
                int y = _y[column];
                _y[column] += height + OvRowGap;
                return y;
            }

            public int Height => Math.Max(_y[0], _y[1]) - OvRowGap + OvPad;
        }

        private static readonly InputBinding DefaultMenuHotkey = InputBinding.FromKey(Keys.D, Keys.Control);

        private GlobalHotkey _settingsOverlayHotkey = null!;
        private InputBinding _menuHotkeyBinding = InputBinding.None;
        private Button? _menuHotkeyBtn;
        private InGameSettingsOverlay _inGameSettingsOverlay;
        private bool _ovRefreshing;
        private readonly List<OvSection> _ovSections = new();
        private int _ovExpandedIndex;
        private Label _ovHintLabel = null!;
        private System.Windows.Forms.Timer? _ovLiveTimer;

        private MacToggleSwitch _ovInSimConnected = null!;
        private MacToggleSwitch _ovOutGaugeConnected = null!;
        private Label _ovInSimStatusLabel = null!;
        private Label _ovOutGaugeStatusLabel = null!;
        private MacToggleSwitch _ovDarkMode = null!;
        private Button _ovLangBtn = null!;

        private MacToggleSwitch _ovRevEnabled = null!;
        private MacToggleSwitch _ovRevUnsupported = null!;
        private MacToggleSwitch _ovRevAutoCalibrate = null!;
        private MinimalNumericUpDown _ovRevLimitNumeric = null!;
        private MinimalNumericUpDown _ovRevCutMSNumeric = null!;
        private Button _ovRevCalibrateBtn = null!;
        private Button _ovRevBindBtn = null!;

        private MacToggleSwitch _ovHudEnabled = null!;
        private MacToggleSwitch _ovHudStyleForza = null!;
        private MacToggleSwitch _ovHudStyleBtn = null!;
        private MacToggleSwitch _ovRpmHud = null!;
        private MacToggleSwitch _ovAchievementSound = null!;

        private MacToggleSwitch _ovSpeedoEnabled = null!;
        private Label _ovSpeedoPresetLabel = null!;
        private MacToggleSwitch _ovSpeedUnit = null!;
        private MacToggleSwitch _ovAirSpeed = null!;
        private MacToggleSwitch _ovPlainDigits = null!;
        private MacToggleSwitch _ovInstantSpeed = null!;
        private MinimalNumericUpDown _ovSpeedoOffsetX = null!;
        private MinimalNumericUpDown _ovSpeedoOffsetY = null!;
        private MinimalNumericUpDown _ovSpeedoScale = null!;

        private MacToggleSwitch _ovIndicatorsEnabled = null!;
        private MacToggleSwitch _ovIndicatorAutoCancel = null!;
        private MacToggleSwitch _ovIndicatorSounds = null!;
        private MacSlider _ovIndicatorVolumeSlider = null!;
        private Label _ovIndicatorVolumeValueLabel = null!;
        private MinimalNumericUpDown _ovIndicatorArmThresholdNumeric = null!;
        private MinimalNumericUpDown _ovIndicatorCenterThresholdNumeric = null!;

        private MacToggleSwitch _ovAssistEnabled = null!;
        private Label _ovAssistStatusLabel = null!;
        private MacToggleSwitch _ovAssistSteering = null!;
        private Button _ovAssistPresetOff = null!;
        private Button _ovAssistPresetRace = null!;
        private Button _ovAssistPresetStreet = null!;
        private Button _ovAssistPresetDrift = null!;
        private MacSlider _ovAssistCharacter = null!;
        private Label _ovAssistCharacterValue = null!;
        private MacToggleSwitch _ovAssistVibration = null!;
        private MacSlider _ovAssistVibrationStrength = null!;
        private Label _ovAssistVibrationValue = null!;
        private MacToggleSwitch _ovAssistHidePad = null!;
        private MacToggleSwitch _ovAssistOverlayBar = null!;
        private Button _ovAssistTuningBtn = null!;
        private Button _ovAssistRumbleBtn = null!;

        private MinimalNumericUpDown _ovMinDriftSpeedNumeric = null!;
        private MinimalNumericUpDown _ovMaxBurnoutSpeedNumeric = null!;
        private MacToggleSwitch _ovAdvancedOutGauge = null!;
        private MacToggleSwitch _ovCarCollisionDetect = null!;
        private MacToggleSwitch _ovObjectCollisionDetect = null!;

        private void ApplyMenuHotkey(InputBinding binding)
        {
            if (binding.Kind == InputKind.None || (binding.Kind == InputKind.Keyboard && binding.Key == Keys.None))
                binding = DefaultMenuHotkey;

            if (_menuHotkeyBinding.Kind == InputKind.Keyboard)
                _settingsOverlayHotkey.RemoveBinding(_menuHotkeyBinding.Key);
            else if (_menuHotkeyBinding.Kind == InputKind.WheelButton)
                _wheelInput.RemoveBinding(_menuHotkeyBinding.WheelButton);

            _menuHotkeyBinding = binding;

            if (binding.Kind == InputKind.Keyboard)
            {
                Keys modifiers = binding.Modifiers;
                _settingsOverlayHotkey.SetBinding(binding.Key, () =>
                {
                    if ((ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt)) != modifiers) return;
                    PostToUi(ToggleInGameSettingsOverlay);
                });
            }
            else
            {
                _wheelInput.SetBinding(binding.WheelButton, () => PostToUi(ToggleInGameSettingsOverlay));
            }

            if (_menuHotkeyBtn != null)
                _menuHotkeyBtn.Text = _menuHotkeyBinding.ToString();
        }

        private void BindMenuHotkey()
        {
            BindKey("header.menuhotkey", b =>
            {
                ApplyMenuHotkey(b);
                if (b.Kind == InputKind.None)
                    AppendStatusMessage(string.Format(Localization.T("status.menuhotkey.default"), DefaultMenuHotkey));
                SaveSettings();
            }, allowModifiers: true);
        }

        private void ToggleInGameSettingsOverlay()
        {
            if (_inGameSettingsOverlay.Visible)
            {
                _inGameSettingsOverlay.RestoreAndHide();
                return;
            }

            IntPtr lfsHwnd = LfsWindow.Find();
            if (lfsHwnd == IntPtr.Zero || GetForegroundWindow() != lfsHwnd) return;

            ApplyOverlayTheme();
            _inGameSettingsOverlay.ShowOverWindow(lfsHwnd, ReflowInGameSettingsOverlay, RefreshInGameSettingsOverlayValues);
            _ovLiveTimer?.Start();
        }

        private void ApplyOverlayTheme()
        {
            ApplyThemeRecursive(_inGameSettingsOverlay.ContentHost);
            foreach (Label label in _cardBackedLabels)
            {
                if (label.Parent is RoundedPanel parent)
                    label.BackColor = parent.BackColor;
            }
        }

        private void ReflowInGameSettingsOverlay(int availableWidth)
        {
            var host = _inGameSettingsOverlay.ContentHost;
            host.SuspendLayout();

            int y = 0;
            for (int i = 0; i < _ovSections.Count; i++)
            {
                var section = _ovSections[i];
                bool expanded = i == _ovExpandedIndex;
                section.Header.Location = new Point(0, y);
                section.Chevron.Text = expanded ? "▾" : "▸";
                y += OvHeaderHeight;

                section.Body.Visible = expanded;
                if (expanded)
                {
                    section.Body.Location = new Point(0, y + OvBodyGap);
                    y += OvBodyGap + section.Body.Height;
                }
                y += OvSectionGap;
            }

            _ovHintLabel.Location = new Point(0, y + 4);
            _ovHintLabel.Size = new Size(OvWidth, 20);
            host.Size = new Size(OvWidth, y + 24);
            host.ResumeLayout();
        }

        private void ToggleOvSection(int index)
        {
            _ovExpandedIndex = _ovExpandedIndex == index ? -1 : index;
            ReflowInGameSettingsOverlay(OvWidth);
            _inGameSettingsOverlay.CenterContent();
        }

        private RoundedPanel AddOvSection(Panel host, string titleKey, Func<(string Text, Color Color)>? summary)
        {
            int index = _ovSections.Count;

            var header = new RoundedPanel
            {
                Size = new Size(OvWidth, OvHeaderHeight),
                BackColor = ApplePalette.Card,
                Radius = 12,
                Cursor = Cursors.Hand,
            };
            host.Controls.Add(header);

            var chevron = MakeLabel(header, "▸", 12, 0, 22, OvHeaderHeight,
                ApplePalette.Title, UiFonts.Get("Segoe UI Symbol", 10f), ContentAlignment.MiddleCenter);
            chevron.Tag = "theme:title";
            var title = MakeLabel(header, "", 38, 0, 330, OvHeaderHeight,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11.5f), ContentAlignment.MiddleLeft, locKey: titleKey);
            title.Tag = "theme:title";
            var summaryLabel = MakeLabel(header, "", OvWidth - 16 - 270, 0, 270, OvHeaderHeight,
                UiColors.Muted, UiFonts.Get("Segoe UI", 9f), ContentAlignment.MiddleRight);
            summaryLabel.Tag = "status:live";

            var body = new RoundedPanel
            {
                Size = new Size(OvWidth, 100),
                BackColor = ApplePalette.Card,
                Radius = 12,
                Visible = false,
            };
            host.Controls.Add(body);

            _ovSections.Add(new OvSection { Header = header, Chevron = chevron, Summary = summaryLabel, Body = body, GetSummary = summary });

            void Toggle(object? s, EventArgs e) => ToggleOvSection(index);
            header.Click += Toggle;
            chevron.Click += Toggle;
            title.Click += Toggle;
            summaryLabel.Click += Toggle;

            return body;
        }

        private MacToggleSwitch OvToggle(Control body, OvColumns cols, int column, string labelKey, Action<bool> onChanged)
        {
            int x = OvColumns.X(column);
            int y = cols.Take(column, 24);
            MakeLabel(body, "", x, y, OvColumnWidth - 52, 24,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9.5f), ContentAlignment.MiddleLeft, locKey: labelKey);
            var toggle = new MacToggleSwitch { Location = new Point(x + OvColumnWidth - 42, y + 2), Size = new Size(40, 20) };
            toggle.CheckedChanged += (s, e) =>
            {
                if (_ovRefreshing) return;
                onChanged(toggle.Checked);
                RefreshInGameSettingsOverlayValues();
            };
            body.Controls.Add(toggle);
            return toggle;
        }

        private MinimalNumericUpDown OvNumeric(Control body, OvColumns cols, int column, string labelKey,
            decimal min, decimal max, decimal increment, Action<decimal> onChanged, int decimals = 0)
        {
            int x = OvColumns.X(column);
            int y = cols.Take(column, 50);
            MakeLabel(body, "", x, y, OvColumnWidth, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft, locKey: labelKey);
            var numeric = new MinimalNumericUpDown
            {
                Location = new Point(x, y + 20),
                Size = new Size(OvColumnWidth, 30),
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = decimals,
            };
            numeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                onChanged(v);
                UpdateOvLive();
            };
            body.Controls.Add(numeric);
            return numeric;
        }

        private MacSlider OvSlider(Control body, OvColumns cols, int column, string labelKey, int min, int max,
            out Label valueLabel, Action<int> onChanged)
        {
            int x = OvColumns.X(column);
            int y = cols.Take(column, 44);
            MakeLabel(body, "", x, y, OvColumnWidth - 120, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft, locKey: labelKey);
            valueLabel = MakeLabel(body, "", x + OvColumnWidth - 120, y, 120, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI Semibold", 8.5f), ContentAlignment.MiddleRight);
            var slider = new MacSlider
            {
                Location = new Point(x, y + 20),
                Size = new Size(OvColumnWidth, 22),
                Minimum = min,
                Maximum = max,
            };
            slider.ValueChanged += (s, e) =>
            {
                if (_ovRefreshing) return;
                onChanged(slider.Value);
                UpdateOvLive();
            };
            body.Controls.Add(slider);
            return slider;
        }

        private Button OvButton(Control body, OvColumns cols, int column, string textKey, Action onClick)
        {
            int y = cols.Take(column, 30);
            var button = MakeButton(body, "", OvColumns.X(column), y, OvColumnWidth, 30, ApplePalette.Blue, locKey: textKey);
            button.Click += (s, e) => onClick();
            return button;
        }

        private (Button Left, Button Right) OvButtonPair(Control body, OvColumns cols, int column,
            string leftKey, Action leftClick, string rightKey, Action rightClick)
        {
            int x = OvColumns.X(column);
            int y = cols.Take(column, 30);
            int half = (OvColumnWidth - 8) / 2;
            var left = MakeButton(body, "", x, y, half, 30, ApplePalette.Blue, locKey: leftKey);
            left.Click += (s, e) => leftClick();
            var right = MakeButton(body, "", x + half + 8, y, half, 30, ApplePalette.Blue, locKey: rightKey);
            right.Click += (s, e) => rightClick();
            return (left, right);
        }

        private Label OvStatusLine(Control body, OvColumns cols, int column, int height = 18)
        {
            int y = cols.Take(column, height);
            var label = MakeLabel(body, "", OvColumns.X(column), y, OvColumnWidth, height,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8f), ContentAlignment.TopLeft);
            label.Tag = "status:live";
            return label;
        }

        private (string Text, Color Color) OvOnOff(bool on, string? detail = null)
        {
            string state = Localization.T(on ? "ingamesettings.state.on" : "ingamesettings.state.off");
            return (string.IsNullOrEmpty(detail) ? state : state + "  ·  " + detail, on ? ApplePalette.Green : UiColors.Muted);
        }

        private void BuildInGameSettingsOverlayContent(Panel host)
        {
            BuildOvRevLimiterSection(host);
            BuildOvHudSection(host);
            BuildOvSpeedometerSection(host);
            BuildOvIndicatorsSection(host);
            BuildOvAssistSection(host);
            BuildOvScoreSection(host);
            BuildOvConnectionsSection(host);

            _ovHintLabel = MakeLabel(host, "", 0, 0, OvWidth, 20, UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f),
                ContentAlignment.MiddleCenter);

            _ovLiveTimer = new System.Windows.Forms.Timer { Interval = OvLiveIntervalMs };
            _ovLiveTimer.Tick += (s, e) =>
            {
                if (!_inGameSettingsOverlay.Visible)
                {
                    _ovLiveTimer.Stop();
                    return;
                }
                UpdateOvLive();
            };

            ReflowInGameSettingsOverlay(OvWidth);
        }

        private void BuildOvRevLimiterSection(Panel host)
        {
            var body = AddOvSection(host, "rev.title",
                () => OvOnOff(_revEnableSwitch.Checked, $"{_revLimiterNumeric.Value:0} RPM"));
            var cols = new OvColumns();

            _ovRevEnabled = OvToggle(body, cols, 0, "ingamesettings.enabled", v =>
            {
                if (_revEnableSwitch.Enabled) _revEnableSwitch.Checked = v;
            });
            _ovRevUnsupported = OvToggle(body, cols, 0, "rev.unsupported.label", v => _revUnsupportedSwitch.Checked = v);
            _ovRevAutoCalibrate = OvToggle(body, cols, 0, "ingamesettings.autocalibrate", v => _revAutoCalibrateNewCarCheck.Checked = v);
            _ovRevCalibrateBtn = OvButton(body, cols, 0, "rev.calibrate", async () =>
            {
                _inGameSettingsOverlay.RestoreAndHide();
                await RPMLimitterCalibrate();
            });

            _ovRevLimitNumeric = OvNumeric(body, cols, 1, "rev.limit", RevLimitMinRpm, RevLimitMaxRpm, 100,
                v => _revLimiterNumeric.Value = v);
            _ovRevCutMSNumeric = OvNumeric(body, cols, 1, "rev.cutms", RevCutMinMs, RevCutMaxMs, 5,
                v => _revCutMS.Value = v);
            _ovRevBindBtn = OvButton(body, cols, 1, "rev.bindfunctions", () => OpenRevLimiterBindings(_inGameSettingsOverlay));

            body.Height = cols.Height;
        }

        private void BuildOvHudSection(Panel host)
        {
            var body = AddOvSection(host, "ingamesettings.section.hud", () =>
            {
                string style = _showOverlayCheck.Checked ? Localization.T("hud.showoverlay")
                    : _showHudCheck.Checked ? Localization.T("ingamesettings.isbtnhud") : "";
                return OvOnOff(_hudMasterEnabledCheck.Checked, style);
            });
            var cols = new OvColumns();

            _ovHudEnabled = OvToggle(body, cols, 0, "ingamesettings.enabled", v => _hudMasterEnabledCheck.Checked = v);
            _ovHudStyleForza = OvToggle(body, cols, 0, "hud.showoverlay", v => _showOverlayCheck.Checked = v);
            _ovHudStyleBtn = OvToggle(body, cols, 0, "ingamesettings.isbtnhud", v => _showHudCheck.Checked = v);
            _ovRpmHud = OvToggle(body, cols, 0, "ingamesettings.rpmhud", v => _showRPMHudCheck.Checked = v);

            _ovAchievementSound = OvToggle(body, cols, 1, "ach.sound", v =>
            {
                _achievementSoundEnabled = v;
                SaveSettings();
            });
            OvButton(body, cols, 1, "ingamesettings.hudcolors", () => ShowScoreColorsMenu(_inGameSettingsOverlay));
            OvButton(body, cols, 1, "ach.title", () => ShowAchievementsMenu(_inGameSettingsOverlay));

            body.Height = cols.Height;
        }

        private void BuildOvSpeedometerSection(Panel host)
        {
            var body = AddOvSection(host, "speedometer.title", () => OvOnOff(_speedoTachoEnabledCheck.Checked,
                string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1)));
            var cols = new OvColumns();

            _ovSpeedoEnabled = OvToggle(body, cols, 0, "ingamesettings.enabled", v => _speedoTachoEnabledCheck.Checked = v);

            int x = OvColumns.X(0);
            int y = cols.Take(0, 30);
            var prevBtn = MakeButton(body, "‹", x, y, 44, 30, ApplePalette.Blue);
            prevBtn.Click += (s, e) =>
            {
                SwitchSpeedoPreset(NextSpeedoPresetIndex(-1));
                RefreshInGameSettingsOverlayValues();
            };
            _ovSpeedoPresetLabel = MakeLabel(body, "", x + 50, y, OvColumnWidth - 100, 30,
                ApplePalette.Text, UiFonts.Get("Segoe UI Semibold", 9.5f), ContentAlignment.MiddleCenter);
            var nextBtn = MakeButton(body, "›", x + OvColumnWidth - 44, y, 44, 30, ApplePalette.Blue);
            nextBtn.Click += (s, e) =>
            {
                SwitchSpeedoPreset(NextSpeedoPresetIndex(+1));
                RefreshInGameSettingsOverlayValues();
            };

            _ovSpeedUnit = OvToggle(body, cols, 0, "speedometer.usemph", v => _speedUnitToggle.Checked = v);
            _ovAirSpeed = OvToggle(body, cols, 0, "speedometer.elements.airspeed", v =>
            {
                _overlay.SpeedoTachoAirSpeed = v;
                SaveSettings();
            });
            _ovPlainDigits = OvToggle(body, cols, 0, "ingamesettings.plaindigits", v =>
            {
                _overlay.SpeedoTachoPlainDigits = v;
                SaveSettings();
            });
            _ovInstantSpeed = OvToggle(body, cols, 0, "ingamesettings.instantspeed", v =>
            {
                _overlay.SpeedoTachoInstantSpeed = v;
                SaveSettings();
            });

            _ovSpeedoOffsetX = OvNumeric(body, cols, 1, "speedometer.offsetx",
                _speedoTachoOffsetXNumeric.Minimum, _speedoTachoOffsetXNumeric.Maximum, _speedoTachoOffsetXNumeric.Increment,
                v => _speedoTachoOffsetXNumeric.Value = v);
            _ovSpeedoOffsetY = OvNumeric(body, cols, 1, "speedometer.offsety",
                _speedoTachoOffsetYNumeric.Minimum, _speedoTachoOffsetYNumeric.Maximum, _speedoTachoOffsetYNumeric.Increment,
                v => _speedoTachoOffsetYNumeric.Value = v);
            _ovSpeedoScale = OvNumeric(body, cols, 1, "speedometer.scale",
                _speedoTachoScaleNumeric.Minimum, _speedoTachoScaleNumeric.Maximum, _speedoTachoScaleNumeric.Increment,
                v => _speedoTachoScaleNumeric.Value = v, decimals: 2);
            OvButtonPair(body, cols, 1,
                "speedometer.elements.button", () => ShowSpeedoElementsMenu(_inGameSettingsOverlay),
                "hud.colors", () => ShowHudColorsMenu(_inGameSettingsOverlay));
            OvButton(body, cols, 1, "speedometer.background.button", () => ShowSpeedoBackgroundMenu(_inGameSettingsOverlay));

            body.Height = cols.Height;
        }

        private void BuildOvIndicatorsSection(Panel host)
        {
            var body = AddOvSection(host, "indicators.title", () => OvOnOff(_indicatorsMasterEnabledCheck.Checked));
            var cols = new OvColumns();

            _ovIndicatorsEnabled = OvToggle(body, cols, 0, "ingamesettings.enabled", v => _indicatorsMasterEnabledCheck.Checked = v);
            _ovIndicatorAutoCancel = OvToggle(body, cols, 0, "ingamesettings.autocancel", v => _indicatorAutoCancelCheck.Checked = v);
            _ovIndicatorSounds = OvToggle(body, cols, 0, "ingamesettings.sounds", v => _indicatorSoundsCheck.Checked = v);
            _ovIndicatorVolumeSlider = OvSlider(body, cols, 0, "indicators.volume", 0, 100, out _ovIndicatorVolumeValueLabel,
                v => _indicatorVolumeSlider.Value = v);

            _ovIndicatorArmThresholdNumeric = OvNumeric(body, cols, 1, "indicators.armthreshold", 1, 100, 1,
                v => _indicatorArmThresholdNumeric.Value = v);
            _ovIndicatorCenterThresholdNumeric = OvNumeric(body, cols, 1, "indicators.centerthreshold", 1, 90, 1,
                v => _indicatorCenterThresholdNumeric.Value = v);

            body.Height = cols.Height;
        }

        private void BuildOvAssistSection(Panel host)
        {
            var body = AddOvSection(host, "controllerassist.title", () =>
            {
                string preset = Localization.T(_assistEngine.Settings.Preset switch
                {
                    AssistPreset.Race => "controllerassist.preset.race",
                    AssistPreset.Street => "controllerassist.preset.street",
                    AssistPreset.Drift => "controllerassist.preset.drift",
                    _ => "controllerassist.preset.off",
                });
                return OvOnOff(_caMasterSwitch.Checked, preset);
            });
            var cols = new OvColumns();

            _ovAssistEnabled = OvToggle(body, cols, 0, "ingamesettings.enabled", v =>
            {
                if (_caMasterSwitch.Enabled) _caMasterSwitch.Checked = v;
            });
            _ovAssistStatusLabel = OvStatusLine(body, cols, 0, 34);
            _ovAssistSteering = OvToggle(body, cols, 0, "controllerassist.steeringassist", v => _caSteeringAssistSwitch.Checked = v);

            int x = OvColumns.X(0);
            int labelY = cols.Take(0, 16);
            MakeLabel(body, "", x, labelY, OvColumnWidth, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft, locKey: "controllerassist.preset");
            int y = cols.Take(0, 30);
            int presetW = (OvColumnWidth - 3 * 6) / 4;
            Button PresetButton(int slot, string key, AssistPreset preset)
            {
                var button = MakeButton(body, "", x + slot * (presetW + 6), y, presetW, 30, ApplePalette.Blue, locKey: key);
                button.Font = UiFonts.Get("Segoe UI Semibold", 9f);
                button.Click += (s, e) =>
                {
                    SelectAssistPreset(preset);
                    RefreshInGameSettingsOverlayValues();
                };
                return button;
            }
            _ovAssistPresetOff = PresetButton(0, "controllerassist.preset.off", AssistPreset.Off);
            _ovAssistPresetRace = PresetButton(1, "controllerassist.preset.race", AssistPreset.Race);
            _ovAssistPresetStreet = PresetButton(2, "controllerassist.preset.street", AssistPreset.Street);
            _ovAssistPresetDrift = PresetButton(3, "controllerassist.preset.drift", AssistPreset.Drift);

            _ovAssistCharacter = OvSlider(body, cols, 0, "controllerassist.character", -100, 100, out _ovAssistCharacterValue,
                v => _caCharacterSlider.Value = v);
            _ovAssistCharacter.CenterValue = 0;
            _ovAssistCharacter.DoubleClick += (s, e) => _ovAssistCharacter.Value = 0;
            _ovAssistCharacterValue.Tag = "status:live";

            _ovAssistVibration = OvToggle(body, cols, 1, "controllerassist.vibration", v => _caVibrationSwitch.Checked = v);
            _ovAssistVibrationStrength = OvSlider(body, cols, 1, "controllerassist.vibrationstrength", 0, 100, out _ovAssistVibrationValue,
                v => _caVibrationStrengthSlider.Value = v);
            _ovAssistHidePad = OvToggle(body, cols, 1, "ingamesettings.hidepad", v => _caHidePadSwitch.Checked = v);
            _ovAssistOverlayBar = OvToggle(body, cols, 1, "controllerassist.overlaybar", v => _caOverlayBarSwitch.Checked = v);
            _ovAssistTuningBtn = OvButton(body, cols, 1, "controllerassist.tuning.button", () => ShowControllerAssistTuningMenu(_inGameSettingsOverlay));
            _ovAssistRumbleBtn = OvButton(body, cols, 1, "controllerassist.rumble.button", () => ShowControllerAssistRumbleMenu(_inGameSettingsOverlay));

            body.Height = cols.Height;
        }

        private void BuildOvScoreSection(Panel host)
        {
            var body = AddOvSection(host, "score.title", null);
            var cols = new OvColumns();

            _ovMinDriftSpeedNumeric = OvNumeric(body, cols, 0, "score.mindriftspeed", 0, 100, 1, v => _minDriftSpeedNumeric.Value = v);
            _ovMaxBurnoutSpeedNumeric = OvNumeric(body, cols, 0, "score.maxburnoutspeed", 0, 150, 1, v => _maxBurnoutSpeedNumeric.Value = v);
            OvButtonPair(body, cols, 0,
                "score.levels.button", () => ShowAngleLevelsMenu(_inGameSettingsOverlay),
                "score.levels.speed_button", () => ShowSpeedLevelsMenu(_inGameSettingsOverlay));

            _ovAdvancedOutGauge = OvToggle(body, cols, 1, "ingamesettings.advancedoutgauge", v => _advancedOutGaugeCheck.Checked = v);
            _ovCarCollisionDetect = OvToggle(body, cols, 1, "ingamesettings.cars", v => _collisionDetectCheck.Checked = v);
            _ovObjectCollisionDetect = OvToggle(body, cols, 1, "ingamesettings.objects", v => _objectCollisionDetectCheck.Checked = v);
            OvButton(body, cols, 1, "derby.levels.button", () => ShowHitLevelsMenu(_inGameSettingsOverlay));

            body.Height = cols.Height;
        }

        private void BuildOvConnectionsSection(Panel host)
        {
            var body = AddOvSection(host, "ingamesettings.connections",
                () => (_connectionStateLabel.Text, _connectionStateLabel.ForeColor));
            var cols = new OvColumns();

            _ovInSimConnected = OvToggle(body, cols, 0, "ingamesettings.insim", v => _connectionSwitch.Checked = v);
            _ovInSimStatusLabel = OvStatusLine(body, cols, 0);
            _ovOutGaugeConnected = OvToggle(body, cols, 0, "ingamesettings.outgauge", v => _outGaugeConnectionSwitch.Checked = v);
            _ovOutGaugeStatusLabel = OvStatusLine(body, cols, 0);

            _ovDarkMode = OvToggle(body, cols, 1, "header.theme", v =>
            {
                _themeSwitch.Checked = v;
                ApplyOverlayTheme();
            });
            int x = OvColumns.X(1);
            int labelY = cols.Take(1, 16);
            MakeLabel(body, "", x, labelY, OvColumnWidth, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft, locKey: "header.language");
            int y = cols.Take(1, 30);
            _ovLangBtn = MakeButton(body, Localization.LanguageDisplayName(Localization.CurrentLanguage), x, y, OvColumnWidth, 30, ApplePalette.Blue);
            _ovLangBtn.Click += (s, e) => OpenLanguagePicker(_ovLangBtn, _inGameSettingsOverlay);

            body.Height = cols.Height;
        }

        private void RefreshInGameSettingsOverlayValues()
        {
            _ovRefreshing = true;

            _ovInSimConnected.Enabled = _connectionSwitch.Enabled;
            _ovInSimConnected.SetCheckedSilent(_connectionSwitch.Checked);
            _ovOutGaugeConnected.SetCheckedSilent(_outGaugeConnectionSwitch.Checked);
            _ovDarkMode.SetCheckedSilent(_themeSwitch.Checked);

            _ovRevEnabled.Enabled = _revEnableSwitch.Enabled;
            _ovRevEnabled.SetCheckedSilent(_revEnableSwitch.Checked);
            _ovRevUnsupported.Enabled = _revUnsupportedSwitch.Enabled;
            _ovRevUnsupported.SetCheckedSilent(_revUnsupportedSwitch.Checked);
            _ovRevAutoCalibrate.Enabled = _revAutoCalibrateNewCarCheck.Enabled;
            _ovRevAutoCalibrate.SetCheckedSilent(_revAutoCalibrateNewCarCheck.Checked);
            _ovRevLimitNumeric.Enabled = _revLimiterNumeric.Enabled;
            _ovRevLimitNumeric.Value = _revLimiterNumeric.Value;
            _ovRevCutMSNumeric.Enabled = _revCutMS.Enabled;
            _ovRevCutMSNumeric.Value = _revCutMS.Value;
            _ovRevCalibrateBtn.Enabled = _calibrateBtn1.Enabled;
            _ovRevBindBtn.Enabled = revBindingsBtn.Enabled;

            _ovHudEnabled.SetCheckedSilent(_hudMasterEnabledCheck.Checked);
            _ovHudStyleForza.Enabled = _showOverlayCheck.Enabled;
            _ovHudStyleForza.SetCheckedSilent(_showOverlayCheck.Checked);
            _ovHudStyleBtn.Enabled = _showHudCheck.Enabled;
            _ovHudStyleBtn.SetCheckedSilent(_showHudCheck.Checked);
            _ovRpmHud.Enabled = _showRPMHudCheck.Enabled;
            _ovRpmHud.SetCheckedSilent(_showRPMHudCheck.Checked);
            _ovAchievementSound.SetCheckedSilent(_achievementSoundEnabled);

            _ovSpeedoEnabled.Enabled = _speedoTachoEnabledCheck.Enabled;
            _ovSpeedoEnabled.SetCheckedSilent(_speedoTachoEnabledCheck.Checked);
            _ovSpeedUnit.SetCheckedSilent(_speedUnitToggle.Checked);
            _ovAirSpeed.SetCheckedSilent(_overlay.SpeedoTachoAirSpeed);
            _ovPlainDigits.SetCheckedSilent(_overlay.SpeedoTachoPlainDigits);
            _ovInstantSpeed.SetCheckedSilent(_overlay.SpeedoTachoInstantSpeed);
            _ovSpeedoOffsetX.Value = _speedoTachoOffsetXNumeric.Value;
            _ovSpeedoOffsetY.Value = _speedoTachoOffsetYNumeric.Value;
            _ovSpeedoScale.Value = _speedoTachoScaleNumeric.Value;

            _ovIndicatorsEnabled.SetCheckedSilent(_indicatorsMasterEnabledCheck.Checked);
            _ovIndicatorAutoCancel.Enabled = _indicatorAutoCancelCheck.Enabled;
            _ovIndicatorAutoCancel.SetCheckedSilent(_indicatorAutoCancelCheck.Checked);
            _ovIndicatorSounds.Enabled = _indicatorSoundsCheck.Enabled;
            _ovIndicatorSounds.SetCheckedSilent(_indicatorSoundsCheck.Checked);
            _ovIndicatorVolumeSlider.Enabled = _indicatorVolumeSlider.Enabled;
            _ovIndicatorVolumeSlider.SetValueSilent(_indicatorVolumeSlider.Value);
            _ovIndicatorArmThresholdNumeric.Enabled = _indicatorArmThresholdNumeric.Enabled;
            _ovIndicatorArmThresholdNumeric.Value = _indicatorArmThresholdNumeric.Value;
            _ovIndicatorCenterThresholdNumeric.Enabled = _indicatorCenterThresholdNumeric.Enabled;
            _ovIndicatorCenterThresholdNumeric.Value = _indicatorCenterThresholdNumeric.Value;

            _ovAssistEnabled.Enabled = _caMasterSwitch.Enabled;
            _ovAssistEnabled.SetCheckedSilent(_caMasterSwitch.Checked);
            _ovAssistSteering.Enabled = _caSteeringAssistSwitch.Enabled;
            _ovAssistSteering.SetCheckedSilent(_caSteeringAssistSwitch.Checked);
            _ovAssistCharacter.Enabled = _caCharacterSlider.Enabled;
            _ovAssistCharacter.SetValueSilent(_caCharacterSlider.Value);
            _ovAssistVibration.Enabled = _caVibrationSwitch.Enabled;
            _ovAssistVibration.SetCheckedSilent(_caVibrationSwitch.Checked);
            _ovAssistVibrationStrength.Enabled = _caVibrationStrengthSlider.Enabled;
            _ovAssistVibrationStrength.SetValueSilent(_caVibrationStrengthSlider.Value);
            _ovAssistHidePad.Enabled = _caHidePadSwitch.Enabled;
            _ovAssistHidePad.SetCheckedSilent(_caHidePadSwitch.Checked);
            _ovAssistOverlayBar.Enabled = _caOverlayBarSwitch.Enabled;
            _ovAssistOverlayBar.SetCheckedSilent(_caOverlayBarSwitch.Checked);
            foreach (var button in new[] { _ovAssistPresetOff, _ovAssistPresetRace, _ovAssistPresetStreet, _ovAssistPresetDrift })
                button.Enabled = _caPresetOffBtn.Enabled;
            _ovAssistTuningBtn.Enabled = _caPresetOffBtn.Enabled;
            _ovAssistRumbleBtn.Enabled = _caVibrationSwitch.Enabled;

            _ovMinDriftSpeedNumeric.Value = _minDriftSpeedNumeric.Value;
            _ovMaxBurnoutSpeedNumeric.Value = _maxBurnoutSpeedNumeric.Value;
            _ovAdvancedOutGauge.Enabled = _advancedOutGaugeCheck.Enabled;
            _ovAdvancedOutGauge.SetCheckedSilent(_advancedOutGaugeCheck.Checked);
            _ovCarCollisionDetect.SetCheckedSilent(_collisionDetectCheck.Checked);
            _ovObjectCollisionDetect.SetCheckedSilent(_objectCollisionDetectCheck.Checked);

            _ovRefreshing = false;
            UpdateOvLive();
        }

        private void UpdateOvLive()
        {
            foreach (var section in _ovSections)
            {
                if (section.GetSummary == null) continue;
                var (text, color) = section.GetSummary();
                section.Summary.Text = text;
                section.Summary.ForeColor = color;
            }

            _ovInSimStatusLabel.Text = _connectionStateLabel.Text;
            _ovInSimStatusLabel.ForeColor = _connectionStateLabel.ForeColor;
            _ovOutGaugeStatusLabel.Text = _outGaugeStateLabel.Text;
            _ovOutGaugeStatusLabel.ForeColor = _outGaugeStateLabel.ForeColor;
            _ovLangBtn.Text = Localization.LanguageDisplayName(Localization.CurrentLanguage);
            _ovHintLabel.Text = string.Format(Localization.T("ingamesettings.hint"), _menuHotkeyBinding);

            _ovSpeedoPresetLabel.Text = string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1);
            _ovIndicatorVolumeValueLabel.Text = $"{_indicatorVolumeSlider.Value}%";

            bool locked = !_caMasterSwitch.Enabled;
            _ovAssistStatusLabel.Text = locked ? _caLockedLabel.Text : _caPadStatusLabel.Text;
            _ovAssistStatusLabel.ForeColor = locked ? ApplePalette.StatusWarn : UiColors.Muted;
            _ovAssistCharacterValue.Text = _caCharacterValueLabel.Text;
            _ovAssistCharacterValue.ForeColor = _caCharacterValueLabel.ForeColor;
            _ovAssistVibrationValue.Text = $"{_caVibrationStrengthSlider.Value}%";

            var active = _assistEngine.Settings.Preset;
            SetButtonColor(_ovAssistPresetOff, active == AssistPreset.Off ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_ovAssistPresetRace, active == AssistPreset.Race ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_ovAssistPresetStreet, active == AssistPreset.Street ? ApplePalette.Green : ApplePalette.Blue);
            SetButtonColor(_ovAssistPresetDrift, active == AssistPreset.Drift ? ApplePalette.Green : ApplePalette.Blue);
        }
    }
}
