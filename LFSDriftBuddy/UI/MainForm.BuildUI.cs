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
        private void BuildUI()
        {

            var version = Assembly
                      .GetExecutingAssembly()
                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                      ?.InformationalVersion;

            string appVersion = $"v.{version?.Split('+')[0] ?? "Unknown"}.alpha";

            SuspendLayout();

            Text = "Live For Speed - Drift Tools";

            StartPosition = FormStartPosition.CenterScreen;

            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;

            Size = new Size(890, 670 + TitleBarHeight);

            MinimumSize = new Size(890, 670 + TitleBarHeight);
            MaximumSize = new Size(890, 670 + TitleBarHeight);

            DoubleBuffered = true;

            BackColor = Color.Black;

            Font = UiFonts.Get("Segoe UI", 10f);

            _mainScrollPanel = new NoScrollBarPanel
            {
                Location = new Point(1, 1),
                Size = new Size(ClientSize.Width - 2 - MinimalScrollbar.TrackWidth, ClientSize.Height - 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = BackColor,
            };
            Controls.Add(_mainScrollPanel);

            _mainScrollbar = new MinimalScrollbar();
            _mainScrollbar.AttachTo(_mainScrollPanel);
            Controls.Add(_mainScrollbar);

            ComputeGridLayout();

            _mainScrollPanel.ContentHeight = _gridContentHeight;
            _mainScrollbar.SyncToTarget();

            _wheelInput = new SteeringWheelInput(
                this.Handle

            );

            _assistLoop = new AssistLoop(_wheelInput, _controllerAssistState, _assistEngine, _virtualPad);
            _assistLoop.Log += msg => PostToUi(() => AppendStatusMessage(msg, StatusErrorColor));

            headerPanel = CreateCard(
                "",
                GridSlot.Header,
                420,
                170, locKey: "header.title");

            Color disconnectedColor = ApplePalette.StatusIdle;
            _connectionStateLabel = MakeLabel(headerPanel, "", 16, 8, 388, 18, disconnectedColor, UiFonts.Get("Segoe UI", 8f), ContentAlignment.TopRight, locKey: "status.disconnected");
            _connectionStateLabel.Tag = "status:live";
            _connectionStateLabel.AutoEllipsis = false;
            _outGaugeStateLabel = MakeLabel(headerPanel, "", 16, 27, 388, 30, disconnectedColor, UiFonts.Get("Segoe UI", 8f), ContentAlignment.TopRight, locKey: "status.outgauge.disconnected");
            _outGaugeStateLabel.Tag = "status:live";
            _outGaugeStateLabel.AutoEllipsis = false;

            MakeLabel(headerPanel, "Dark Mode", 24, 60, 155, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.theme");
            MakeLabel(headerPanel, "Language", 24, 95, 155, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.language");
            MakeLabel(headerPanel, "App Version", 24, 130, 155, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.appversion");

            MakeLabel(headerPanel, appVersion, 200, 130, 200, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleRight);

            _langBtn = MakeButton(headerPanel, Localization.LanguageDisplayName(Localization.CurrentLanguage),
            300, 95, 100, 30);
            _langBtn.Click += (s, e) => OpenLanguagePicker(_langBtn);

            _themeSwitch = new MacToggleSwitch
            {
                Location = new Point(355, 65),
                Size = new Size(40, 20),
                Checked = false
            };

            _themeSwitch.CheckedChanged += (s, e) =>
            {
                ApplyTheme(_themeSwitch.Checked);
                SaveSettings();
            };

            headerPanel.Controls.Add(_themeSwitch);

            connectionPanel = CreateCard(
                "Connection",
                GridSlot.ConnectionInSim,
                420,
                295, locKey: "connection.title");

            var connectionLabel = MakeLabel(connectionPanel, "STATUS", 300, 14, 55, 22,
            UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _connectionSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = false
            };

            _connectionSwitch.CheckedChanged += (s, e) =>
            {
                if (_connectionSwitch.Checked)
                {

                    _connectionSwitch.Enabled = false;

                    string host = _hostBox.Text.Trim();
                    int port = (int)_portBox.Value;
                    string admin = _adminBox.Text;
                    Task.Run(() => _insim.Connect(host, port, admin));
                }
                else
                {

                    _connectionSwitch.Enabled = false;

                    Task.Run(() =>
                    {
                        _insim.DeleteAllButtons();
                        PostToUi(() => _hud.InvalidateCache());
                        System.Threading.Thread.Sleep(HudClearSettleMs);
                        _insim.Disconnect();

                        PostToUi(() =>
                        {
                            _connectionSwitch.Enabled = true;
                        });
                    });
                }
            };

            connectionPanel.Controls.Add(_connectionSwitch);

            MakeLabel(connectionPanel, "Host", 24, 60, 155, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.host");
            MakeLabel(connectionPanel, "Port", 24, 95, 155, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.port");
            MakeLabel(connectionPanel, "Admin password", 24, 130, 220, 30,
            UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.adminpass");

            _hostBox = MakeTextBox(connectionPanel, "127.0.0.1", 250, 60, 150, 30);

            _portBox = MakeNumericUpDown(
                connectionPanel,
                250,
                95,
                150,
                30,
                1,
                65535,
                29999);

            _adminBox = MakeTextBox(connectionPanel, "", 250, 130, 150, 30);

            var connSeparator2 = new Panel { Location = new Point(24, 178), Size = new Size(372, 1), BackColor = ApplePalette.Border, Tag = "theme:border" };
            connectionPanel.Controls.Add(connSeparator2);

            MakeLabel(connectionPanel, Localization.T("general.lfsfolder"), 24, 188, 372, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "general.lfsfolder");

            _lfsFolderStatusLabel = MakeLabel(connectionPanel, "", 24, 214, 372, 20,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);
            UpdateLfsFolderStatusLabel();

            var lfsBrowseBtn = MakeButton(connectionPanel, Localization.T("general.browse"), 24, 244, 176, 30, locKey: "general.browse");
            lfsBrowseBtn.Click += (s, e) => BrowseLfsFolder();
            _localizedControls.Add((lfsBrowseBtn, "general.browse"));

            var lfsConfigureBtn = MakeButton(connectionPanel, Localization.T("general.configure"), 220, 244, 176, 30, ApplePalette.Blue, locKey: "general.configure");
            lfsConfigureBtn.Click += (s, e) => ConfigureLfsCfg();
            _localizedControls.Add((lfsConfigureBtn, "general.configure"));

            var inputControllerPanel = CreateCard(
                "Input Device",
                GridSlot.InputController,
                420,
                170, locKey: "inputcontroller.title");

            var inputControllerConfigureBtn = MakeButton(inputControllerPanel, Localization.T("controllerassist.setup.button"), 420 - 10 - 170, 10, 170, 30, locKey: "controllerassist.setup.button");
            inputControllerConfigureBtn.Click += (s, e) => ShowWheelSetupDialog(_wheelInput.GetAvailableDevices());

            _inputControllerTilesPanel = new Panel
            {
                Location = new Point(16, 54),
                Size = new Size(420 - 32, 100),
                BackColor = Color.Transparent,
            };
            inputControllerPanel.Controls.Add(_inputControllerTilesPanel);

            var outGaugeConnectionPanel = CreateCard(
                "OutGauge Connection",
                GridSlot.ConnectionOutGauge,
                420,
                115, locKey: "connection.outgauge.title");

            MakeLabel(outGaugeConnectionPanel, "STATUS", 300, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _outGaugeConnectionSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = false
            };
            _outGaugeConnectionSwitch.CheckedChanged += (s, e) =>
            {
                ApplyOutGaugeConnectionState(_outGaugeConnectionSwitch.Checked);
                SaveSettings();
            };
            outGaugeConnectionPanel.Controls.Add(_outGaugeConnectionSwitch);

            MakeLabel(outGaugeConnectionPanel, "Port", 24, 60, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.port");
            _outGaugePortBox = MakeNumericUpDown(
                outGaugeConnectionPanel,
                250,
                60,
                150,
                30,
                1,
                65535,
                _outGauge.UdpPort);

            _outGaugeStatusLabel = MakeLabel(outGaugeConnectionPanel, "", 24, 92, 372, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);
            UpdateOutGaugeStatusLabel();


            var outSimConnectionPanel = CreateCard(
                "OutSim2 Connection",
                GridSlot.ConnectionOutSim,
                420,
                170, locKey: "connection.outsim.title");

            MakeLabel(outSimConnectionPanel, "STATUS", 300, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _outSimConnectionSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = false
            };
            _outSimConnectionSwitch.CheckedChanged += (s, e) =>
            {
                ApplyOutSimConnectionState(_outSimConnectionSwitch.Checked);
                SaveSettings();
            };
            outSimConnectionPanel.Controls.Add(_outSimConnectionSwitch);

            MakeLabel(outSimConnectionPanel, Localization.T("connection.port"), 24, 60, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.port");
            _outSimPortBox = MakeNumericUpDown(
                outSimConnectionPanel, 250, 60, 150, 30, 1, 65535, _outSim.UdpPort);

            MakeLabel(outSimConnectionPanel, Localization.T("connection.outsim.opts"), 24, 95, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.outsim.opts");
            _outSimOptsBox = MakeTextBox(outSimConnectionPanel, "bf", 250, 95, 150, 30, HorizontalAlignment.Left);
            _outSimOptsBox.Leave += (s, e) => SaveSettings();

            _outSimStatusLabel = MakeLabel(outSimConnectionPanel, "", 24, 132, 372, 20,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);
            UpdateOutSimStatusLabel();

            speedometerPanel = CreateCard(
                "Speedometer",
                GridSlot.Speedometer,
                420,
                450, locKey: "speedometer.title");

            MakeLabel(speedometerPanel, "STATUS", 300, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _speedoTachoEnabledCheck = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = true
            };
            _speedoTachoEnabledCheck.CheckedChanged += (s, e) =>
            {
                _overlay.SpeedoTachoEnabled = _speedoTachoEnabledCheck.Checked;
                _speedoTachoIntent.Capture(_speedoTachoEnabledCheck.Checked);
                UpdateOverlayAttachment();
                SaveSettings();
            };
            speedometerPanel.Controls.Add(_speedoTachoEnabledCheck);
            _localizedControls.Add((_speedoTachoEnabledCheck, "speedometer.showhud"));

            MakeLabel(speedometerPanel, Localization.T("speedometer.preview"), 24, 48, 85, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.MiddleLeft, locKey: "speedometer.preview");
            _speedoTachoPreview = new SpeedoTachoPreviewControl
            {
                Location = new Point(110, 60),
                Size = new Size(200, 160)
            };
            speedometerPanel.Controls.Add(_speedoTachoPreview);

            _speedoPresetLabel = MakeLabel(speedometerPanel, "", 296, 48, 100, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.MiddleRight);

            var speedoPresetPrevBtn = MakeButton(speedometerPanel, "‹", 70, 126, 28, 28, ApplePalette.Blue);
            speedoPresetPrevBtn.Click += (s, e) => SwitchSpeedoPreset(_activeSpeedoPresetIndex == 0 ? 1 : 0);

            var speedoPresetNextBtn = MakeButton(speedometerPanel, "›", 316, 126, 28, 28, ApplePalette.Blue);
            speedoPresetNextBtn.Click += (s, e) => SwitchSpeedoPreset(_activeSpeedoPresetIndex == 0 ? 1 : 0);

            var speedoPresetResetBtn = MakeButton(speedometerPanel, Localization.T("speedometer.preset.reset"), 320, 165, 85, 60, Color.FromArgb(200, 20, 20));
            speedoPresetResetBtn.Click += (s, e) =>
            {
                var defaults = _activeSpeedoPresetIndex == 0 ? new SpeedoPresetData() : SpeedoPresetData.Default2();
                _speedoPresets[_activeSpeedoPresetIndex] = defaults;
                ApplySpeedoPreset(defaults);
                SaveSettings();
            };
            _localizedControls.Add((speedoPresetResetBtn, "speedometer.preset.reset"));

            UpdateSpeedoPresetLabel();

            MakeLabel(speedometerPanel, "Offset X:", 24, 268, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.offsetx");
            _speedoTachoOffsetXNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 268, 150, 30, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetX = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Offset Y:", 24, 303, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.offsety");
            _speedoTachoOffsetYNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 303, 150, 30, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetY = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Scale:", 24, 338, 155, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.scale");
            _speedoTachoScaleNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 338, 150, 30, 0.3m, 2.5m, 1.0m, 0.05m,
                v => { _overlay.SpeedoTachoScale = (float)v; SaveSettings(); });
            _speedoTachoScaleNumeric.DecimalPlaces = 2;

            MakeLabel(speedometerPanel, Localization.T("speedometer.usemph"), 24, 378, 300, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.usemph");
            _speedUnitToggle = new MacToggleSwitch
            {
                Location = new Point(355, 383),
                Size = new Size(40, 20),
                Checked = false
            };
            speedometerPanel.Controls.Add(_speedUnitToggle);

            MakeLabel(speedometerPanel, Localization.T("speedometer.usemph.subtitle"), 24, 410, 380, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "speedometer.usemph.subtitle");

            _speedUnitToggle.CheckedChanged += (s, e) =>
            {
                _useMph = _speedUnitToggle.Checked;
                _overlay.SpeedoTachoUseMph = _useMph;
                UpdateSpeedUnitLabels();
                SaveSettings();
            };
            UpdateSpeedUnitLabels();

            var hudColorsBtn = MakeButton(speedometerPanel, Localization.T("hud.colors"), 24, 230, 124, 30, ApplePalette.Blue);
            hudColorsBtn.Click += (s, e) => ShowHudColorsMenu();
            _localizedControls.Add((hudColorsBtn, "hud.colors"));

            var speedoBackgroundBtn = MakeButton(speedometerPanel, Localization.T("speedometer.background.button"), 152, 230, 124, 30, ApplePalette.Blue);
            speedoBackgroundBtn.Click += (s, e) => ShowSpeedoBackgroundMenu();
            _localizedControls.Add((speedoBackgroundBtn, "speedometer.background.button"));

            var speedoElementsBtn = MakeButton(speedometerPanel, Localization.T("speedometer.elements.button"), 280, 230, 124, 30, ApplePalette.Blue);
            speedoElementsBtn.Click += (s, e) => ShowSpeedoElementsMenu();
            _localizedControls.Add((speedoElementsBtn, "speedometer.elements.button"));

            var marginSide = 20;

            scorePanel = CreateCard(
                "Drift Score",
                GridSlot.Score,
                420,
                586, locKey: "score.title");

            _resetBtn = MakeButton(scorePanel, "RESET SCORE", 230 + marginSide, 10, 160, 30, Color.FromArgb(200, 20, 20), locKey: "score.reset");

            _resetBtn.Enabled = false;

            _resetBtn.Click += (s, e) => { _drift.ResetTotal(); UpdateScoreLabels(); };

            _driverInfoLabel = MakeLabel(scorePanel, "---", 24, 53, 380, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f));

            MakeLabel(scorePanel, Localization.T("hud.advanced_outgauge"), 24, 225, 300, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.advanced_outgauge");

            _advancedOutGaugeCheck = new MacToggleSwitch
            {
                Location = new Point(355, 230),
                Size = new Size(40, 20),
                Checked = true,
                Enabled = false
            };
            _advancedOutGaugeCheck.CheckedChanged += (s, e) =>
            {
                _drift.SetAdvancedOutGaugeEnabled(_advancedOutGaugeCheck.Enabled && _advancedOutGaugeCheck.Checked);

                _advancedOutGaugeIntent.Capture(_advancedOutGaugeCheck.Checked);
                SaveSettings();
            };
            scorePanel.Controls.Add(_advancedOutGaugeCheck);

            MakeLabel(scorePanel, Localization.T("hud.advanced_outgauge.subtitle"), 24, 255, 380, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.advanced_outgauge.subtitle");

            var driftLevelsBtn = MakeButton(scorePanel, "DRIFT LEVELS", 24, 190, 181, 30, ApplePalette.Blue, locKey: "score.levels.button");
            driftLevelsBtn.Click += (s, e) => ShowAngleLevelsMenu();

            var speedLevelsBtn = MakeButton(scorePanel, "SPEED LEVELS", 215, 190, 181, 30, ApplePalette.Blue, locKey: "score.levels.speed_button");
            speedLevelsBtn.Click += (s, e) => ShowSpeedLevelsMenu();

            const int scoreColW = 94;
            Label AddStatTile(string labelKey, int col, int row, Color valueColor, string initialValue = "0")
            {
                int x = 24 + col * scoreColW;
                int y = row == 0 ? 76 : 136;
                MakeLabel(scorePanel, "", x, y, scoreColW - 4, 14, ApplePalette.Text,
                    UiFonts.Get("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: labelKey);
                return MakeLabel(scorePanel, initialValue, x, y + 20, scoreColW - 4, 22,
                    valueColor, UiFonts.Get("Segoe UI", 14f, FontStyle.Bold));
            }

            _scoreValueLabel = AddStatTile("score.total", 0, 0, Color.FromArgb(255, 200, 50));
            _runValueLabel = AddStatTile("score.run", 1, 0, Color.FromArgb(80, 220, 120));
            _comboValueLabel = AddStatTile("score.combo", 2, 0, Color.FromArgb(255, 120, 40), "x1");
            _angleValueLabel = AddStatTile("score.angle", 3, 0, ApplePalette.Text, "0°");

            _bestRunValueLabel = AddStatTile("score.bestrun", 0, 1, Color.FromArgb(255, 200, 50));
            _bestDriftValueLabel = AddStatTile("score.bestdrift", 1, 1, Color.FromArgb(60, 180, 255), "0.0s");
            _bestDeepDriftValueLabel = AddStatTile("score.bestdeepdrift", 2, 1, Color.FromArgb(255, 80, 80), "0.0s");

            MakeLabel(scorePanel, Localization.T("score.mindriftspeed"), 24, 296, 220, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.mindriftspeed");
            _minDriftSpeedNumeric = MakeNumericUpDown(
                scorePanel, 270, 292, 90, 28, 0, 100, (decimal)_drift.MinDriftSpeedKmh, 1,
                v => { _drift.SetMinDriftSpeedKmh((double)v); SaveSettings(); });
            MakeLabel(scorePanel, "km/h", 366, 296, 60, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));
            MakeLabel(scorePanel, Localization.T("score.mindriftspeed.subtitle"), 24, 322, 372, 28,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.mindriftspeed.subtitle");

            MakeLabel(scorePanel, Localization.T("score.maxburnoutspeed"), 24, 358, 220, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.maxburnoutspeed");
            _maxBurnoutSpeedNumeric = MakeNumericUpDown(
                scorePanel, 270, 344, 90, 28, 0, 150, (decimal)_drift.MaxBurnoutSpeedKmh, 1,
                v => { _drift.SetMaxBurnoutSpeedKmh((double)v); SaveSettings(); });
            MakeLabel(scorePanel, "km/h", 366, 348, 60, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 9f));
            MakeLabel(scorePanel, Localization.T("score.maxburnoutspeed.subtitle"), 24, 374, 372, 28,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.maxburnoutspeed.subtitle");

            MakeLabel(scorePanel, Localization.T("score.collisiondetect"), 24, 400, 260, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.collisiondetect");
            _collisionDetectCheck = new MacToggleSwitch
            {
                Location = new Point(355, 405),
                Size = new Size(40, 20),
                Checked = true
            };
            _collisionDetectCheck.CheckedChanged += (s, e) =>
            {
                _collisions.Enabled = _collisionDetectCheck.Checked;
                SaveSettings();
            };
            scorePanel.Controls.Add(_collisionDetectCheck);
            MakeLabel(scorePanel, Localization.T("score.collisiondetect.subtitle"), 24, 430, 372, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.collisiondetect.subtitle");

            MakeLabel(scorePanel, Localization.T("score.objectcollisiondetect"), 24, 466, 260, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.objectcollisiondetect");
            _objectCollisionDetectCheck = new MacToggleSwitch
            {
                Location = new Point(355, 471),
                Size = new Size(40, 20),
                Checked = true
            };
            _objectCollisionDetectCheck.CheckedChanged += (s, e) =>
            {
                _objectCollisionDetectionEnabled = _objectCollisionDetectCheck.Checked;
                SaveSettings();
            };
            scorePanel.Controls.Add(_objectCollisionDetectCheck);
            MakeLabel(scorePanel, Localization.T("score.objectcollisiondetect.subtitle"), 24, 496, 372, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.objectcollisiondetect.subtitle");

            var hitLevelsBtn = MakeButton(scorePanel, "HIT LEVELS", 24, 532, 372, 30, ApplePalette.Blue, locKey: "derby.levels.button");
            hitLevelsBtn.Click += (s, e) => ShowHitLevelsMenu();

            hudPanel = CreateCard(
                "Forza-like HUD Settings",
                GridSlot.Hud,
                420,
                450, locKey: "hud.title");

            MakeLabel(hudPanel, "STATUS", 300, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _hudMasterEnabledCheck = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = true
            };
            hudPanel.Controls.Add(_hudMasterEnabledCheck);
            _localizedControls.Add((_hudMasterEnabledCheck, "hud.master"));

            MakeLabel(hudPanel, Localization.T("hud.showoverlay"), 24, 228, 300, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.showoverlay");
            _showOverlayCheck = new MacToggleSwitch
            {
                Location = new Point(355, 233),
                Size = new Size(40, 20),
                Checked = true
            };
            hudPanel.Controls.Add(_showOverlayCheck);
            MakeLabel(hudPanel, Localization.T("hud.showoverlay.subtitle"), 24, 258, 380, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.showoverlay.subtitle");

            _showOverlayCheck.CheckedChanged += (s, e) =>
            {
                _overlay.ScoreOverlayEnabled = _showOverlayCheck.Checked;
                if (_showOverlayCheck.Checked)
                    _showHudCheck.Checked = false;

                UpdateOverlayAttachment();
                SaveSettings();
                RefreshColorButtonSwatches();
            };

            MakeLabel(hudPanel, Localization.T("hud.show"), 24, 298, 300, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.show");
            _showHudCheck = new MacToggleSwitch
            {
                Location = new Point(355, 303),
                Size = new Size(40, 20),
                Checked = false
            };
            hudPanel.Controls.Add(_showHudCheck);
            MakeLabel(hudPanel, Localization.T("hud.show.subtitle"), 24, 328, 380, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.show.subtitle");

            _showHudCheck.CheckedChanged += (s, e) =>
            {
                if (!_showHudCheck.Checked)
                {
                    _hud.ClearAllButtons();
                }
                else
                {
                    _showOverlayCheck.Checked = false;
                }
                RefreshColorButtonSwatches();
            };

            _hudMasterEnabledCheck.CheckedChanged += (s, e) =>
            {
                bool on = _hudMasterEnabledCheck.Checked;
                _showOverlayCheck.Enabled = on;
                _showHudCheck.Enabled = on;
                if (on)
                {
                    if (_scoreOverlayIntent.Wanted) _showOverlayCheck.Checked = true;
                    else if (_insimHudIntent.Wanted) _showHudCheck.Checked = true;
                }
                else
                {
                    _scoreOverlayIntent.Capture(_showOverlayCheck.Checked);
                    _insimHudIntent.Capture(_showHudCheck.Checked);
                    _showOverlayCheck.Checked = false;
                    _showHudCheck.Checked = false;
                }

                SyncRevLimiterHudAvailability();

                SaveSettings();
            };

            MakeLabel(hudPanel, Localization.T("hud.REVLimitter"), 24, 368, 300, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.REVLimitter");
            _showRPMHudCheck = new MacToggleSwitch
            {
                Location = new Point(355, 373),
                Size = new Size(40, 20),
                Checked = false
            };
            _showRPMHudCheck.CheckedChanged += (s, e) =>
            {

                _showRpmHudIntent.Capture(_showRPMHudCheck.Checked);
                SaveSettings();
            };
            hudPanel.Controls.Add(_showRPMHudCheck);
            MakeLabel(hudPanel, Localization.T("hud.REVLimitter.subtitle"), 24, 398, 380, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.REVLimitter.subtitle");

            var scoreColorsBtn = MakeButton(hudPanel, Localization.T("hud.scorecolors"), 90, 190, 250, 30, ApplePalette.Blue);
            scoreColorsBtn.Click += (s, e) => ShowScoreColorsMenu();
            _localizedControls.Add((scoreColorsBtn, "hud.scorecolors"));

            MakeLabel(hudPanel, Localization.T("hud.livepreview"), 24, 48, 300, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.MiddleLeft, locKey: "hud.livepreview");
            _hudPreview = new ForzaHudPreviewControl
            {
                Location = new Point(0, 40),
                Size = new Size(420, 170)
            };
            hudPanel.Controls.Add(_hudPreview);

            revLimiterPanel = CreateCard(
                "Rev Limiter",
                GridSlot.RevLimiter,
                420,
                330, locKey: "rev.title");

            var revStatusLabel = MakeLabel(revLimiterPanel, "STATUS", 300, 14, 55, 22,
            UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _revEnableSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = _revLimiter.Enabled
            };

            _revEnableSwitch.CheckedChanged += (s, e) =>
            {
                _revLimiter.Enabled = _revEnableSwitch.Checked;

                _revEnabledIntent.Capture(_revEnableSwitch.Checked);

                if (!_revLimiter.IsRunning)
                    _revLimiter.Start();

                SyncRevAutoCalibrateAvailability();

                SaveSettings();
            };

            revLimiterPanel.Controls.Add(_revEnableSwitch);

            _vehicleInfoLabel = MakeLabel(revLimiterPanel, "---", 24, 53, 190, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f));

            MakeLabel(revLimiterPanel, Localization.T("rev.unsupported.label"), 215, 50, 135, 22,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8f), ContentAlignment.MiddleRight, locKey: "rev.unsupported.label");
            _revUnsupportedSwitch = new MacToggleSwitch
            {
                Location = new Point(355, 51),
                Size = new Size(40, 20),
                Checked = false
            };
            _revUnsupportedSwitch.CheckedChanged += (s, e) =>
            {
                if (string.IsNullOrEmpty(_currentCarName))
                {
                    _revUnsupportedSwitch.SetCheckedSilent(false);
                    return;
                }
                SetCurrentCarRevUnsupported(_revUnsupportedSwitch.Checked, detectedAutomatically: false);
            };
            revLimiterPanel.Controls.Add(_revUnsupportedSwitch);

            _revLockedLabel = MakeLabel(revLimiterPanel, "", 24, 74, 380, 22,
                ApplePalette.StatusWarn, UiFonts.Get("Segoe UI", 9f), ContentAlignment.TopLeft);

            revBindingsBtn = MakeButton(revLimiterPanel, "BIND FUNCTIONS", 250, 286, 150, 30, locKey: "rev.bindfunctions");
            revBindingsBtn.Click += (s, e) => OpenRevLimiterBindings();

            _globalHotkey = new GlobalHotkey();

            _settingsOverlayHotkey = new GlobalHotkey(
                (Keys.D, () =>
                {
                    if ((ModifierKeys & Keys.Control) == 0) return;
                    PostToUi(ToggleInGameSettingsOverlay);
                }));

            MakeLabel(revLimiterPanel, "RPM", 24, 94, 60, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.rpm");
            _rpmLabel = MakeLabel(revLimiterPanel, "0", 90, 89, 90, 36, null, UiFonts.Get("Segoe UI", 18f, FontStyle.Bold), ContentAlignment.MiddleLeft);
            _revCutLabel = MakeLabel(revLimiterPanel, "", 190, 89, 50, 36,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 14f, FontStyle.Bold));

            _calibrateBtn1 = MakeButton(revLimiterPanel, "CALIBRATE", 250, 94, 150, 30, locKey: "rev.calibrate");

            _calibrateBtn1.Click += async (s, e) => await RPMLimitterCalibrate();

            _rpmBar = new MacProgressBar
            {
                Location = new Point(24, 134),
                Size = new Size(376, 18),
                Minimum = 0,
                Maximum = CalibratedMAXRPM,
                Value = 0
            };
            revLimiterPanel.Controls.Add(_rpmBar);

            _revLimitLabel = MakeLabel(revLimiterPanel, "RPM Limit", 24, 169, 220, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.limit");
            _revLimiterNumeric = MakeNumericUpDown(
                revLimiterPanel,
                250,
                169,
                150,
                30,
                RevLimitMinRpm,
                RevLimitMaxRpm,
                CalibratedMAXRPM,
                100,
                v =>
                {
                    _revLimiter.RpmLimit = (int)v;
                    _rpmBar.Maximum = (int)v;
                    _overlay.UpdateMaxRpm((int)v);
                    SaveVehicleRevSettings(_currentCarName);
                }
            );

            MakeLabel(revLimiterPanel, "Cut time [ms]", 24, 204, 220, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.cutms");
            _revCutMS = MakeNumericUpDown(
                revLimiterPanel,
                250,
                204,
                150,
                30,
                RevCutMinMs,
                RevCutMaxMs,
                SavedMSCUT,
                5,
                v =>
                {
                    _revLimiter.CutMs = (int)v;
                    SaveVehicleRevSettings(_currentCarName);
                }
            );

            MakeLabel(revLimiterPanel, Localization.T("rev.autocalibrate"), 24, 242, 300, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.autocalibrate");
            _revAutoCalibrateNewCarCheck = new MacToggleSwitch
            {
                Location = new Point(355, 238),
                Size = new Size(40, 20),
                Checked = true
            };
            revLimiterPanel.Controls.Add(_revAutoCalibrateNewCarCheck);
            _revAutoCalibrateNewCarCheck.CheckedChanged += (s, e) =>
            {
                _revAutoCalibrateIntent.Capture(_revAutoCalibrateNewCarCheck.Checked);
                SaveSettings();
            };
            MakeLabel(revLimiterPanel, Localization.T("rev.autocalibrate.subtitle"), 24, 266, 380, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "rev.autocalibrate.subtitle");

            MakeLabel(revLimiterPanel, "Bind revlimitter functions", 24, 290, 220, 26,
                      UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.calibratehint");

            indicatorPanel = CreateCard(
               "Turn Signals",
               GridSlot.Indicators,
               850,
               460, locKey: "indicators.title");

            MakeLabel(indicatorPanel, "STATUS", 730, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _indicatorsMasterEnabledCheck = new MacToggleSwitch
            {
                Location = new Point(790, 10),
                Checked = true
            };
            _indicatorsMasterEnabledCheck.CheckedChanged += (s, e) =>
            {
                bool on = _indicatorsMasterEnabledCheck.Checked;
                _indicators.Enabled = on;
                _bindLeftBtn.Enabled = on;
                _bindRightBtn.Enabled = on;
                _bindHazardBtn.Enabled = on;
                _indicatorArmThresholdNumeric.Enabled = on;
                _indicatorCenterThresholdNumeric.Enabled = on;

                SyncIndicatorSoundsAvailability();

                _indicatorAutoCancelCheck.Enabled = on;
                if (!on)
                {
                    _indicatorAutoCancelIntent.Capture(_indicatorAutoCancelCheck.Checked);
                    _indicatorAutoCancelCheck.Checked = false;
                }
                else
                {
                    _indicatorAutoCancelCheck.Checked = _indicatorAutoCancelIntent.Wanted;
                }

                SaveSettings();
            };
            indicatorPanel.Controls.Add(_indicatorsMasterEnabledCheck);
            _localizedControls.Add((_indicatorsMasterEnabledCheck, "indicators.master"));

            _wheelInput.Log += msg => AppendStatusMessage(msg);
            _wheelInput.SteeringChanged += pct => PostToUi(() =>
            {
                _wheelAngleLabel.Text = $"{pct:F0}%";
                _wheelAngleLabel.ForeColor = pct switch
                {
                    <= -60 or >= 60 => Color.FromArgb(255, 80, 80),
                    <= -25 or >= 25 => Color.FromArgb(255, 180, 60),
                    _ => Color.FromArgb(80, 220, 120)
                };

                IndicatorManager.IndicatorState newState = _indicators.CurrentState;

                if (_insim.IsConnected && _indicatorAutoCancelCheck != null && _indicatorAutoCancelCheck.Checked)
                {
                    int centerPct = _indicatorCenterThresholdPct;
                    if (indClickONOFF == true && pct < centerPct && newState == IndicatorState.Right || indClickONOFF == true && pct > -centerPct && newState == IndicatorState.Left)
                    {
                        indClickONOFF = false;
                        if (newState == IndicatorState.Right)
                        {
                            _indicators.ToggleRight();
                        }
                        if (newState == IndicatorState.Left)
                        {
                            _indicators.ToggleLeft();
                        }
                    }

                    int armPct = _indicatorArmThresholdPct;
                    if (indClickONOFF == false && pct >= armPct && newState == IndicatorState.Right || indClickONOFF == false && pct <= -armPct && newState == IndicatorState.Left)
                    {
                        indClickONOFF = true;
                    }
                }

            });

            Font groupHeaderFont = UiFonts.Get("Segoe UI Semibold", 9f);
            Color groupHeaderColor = Color.FromArgb(90, 90, 110);

            const int indCol1X = 24, indCol2X = 450, indColW = 380;

            const int bindColW = 190, bindColGap = 20;
            int bindCol2X = indCol1X + bindColW + bindColGap;

            int y1 = 54;
            MakeLabel(indicatorPanel, Localization.T("indicators.group.status"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.status");
            y1 += 20;

            MakeLabel(indicatorPanel, Localization.T("indicators.signal"), indCol1X, y1, 150, 16,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 7.5f, FontStyle.Bold), locKey: "indicators.signal");
            _indicatorDisplayLabel = MakeLabel(indicatorPanel, "---", indCol1X, y1 + 18, 150, 32,
                Color.FromArgb(100, 200, 255), UiFonts.Get("Segoe UI", 16f, FontStyle.Bold));

            MakeLabel(indicatorPanel, "STEERING", bindCol2X, y1, 150, 16, ApplePalette.Text,
                UiFonts.Get("Segoe UI", 7.5f, FontStyle.Bold), locKey: "indicators.steering");
            _wheelAngleLabel = MakeLabel(indicatorPanel, "0%", bindCol2X, y1 + 18, 150, 28,
                Color.FromArgb(255, 200, 50), UiFonts.Get("Segoe UI", 14f, FontStyle.Bold));
            y1 += 58;

            MakeLabel(indicatorPanel, Localization.T("indicators.lights"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.lights");
            y1 += 20;
            MakeLabel(indicatorPanel, Localization.T("indicators.dash.subtitle"), indCol1X, y1, indColW, 24,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.dash.subtitle");
            y1 += 26;

            _dashLeftLamp = new DashboardLampIcon { Kind = DashLampKind.TurnLeft, Location = new Point(indCol1X, y1) };
            _dashHighBeamLamp = new DashboardLampIcon { Kind = DashLampKind.HighBeam, Location = new Point(indCol1X + 72, y1) };
            _dashRightLamp = new DashboardLampIcon { Kind = DashLampKind.TurnRight, Location = new Point(indCol1X + 144, y1) };
            indicatorPanel.Controls.Add(_dashLeftLamp);
            indicatorPanel.Controls.Add(_dashHighBeamLamp);
            indicatorPanel.Controls.Add(_dashRightLamp);
            y1 += 52;

            _indicatorStatusLabel = MakeLabel(indicatorPanel, "", indCol1X, y1, indColW, 50,
               Color.FromArgb(150, 200, 100), UiFonts.Get("Segoe UI", 8f));
            y1 += 54;

            MakeLabel(indicatorPanel, Localization.T("indicators.keybind"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.keybind");
            y1 += 24;

            int bindRow = 0;
            Button MakeBindRow(string bindNameKey, InputBinding current)
            {
                int col = bindRow % 2;
                int row = bindRow / 2;
                int x = col == 0 ? indCol1X : bindCol2X;
                int y = y1 + row * 50;

                MakeLabel(indicatorPanel, Localization.T(bindNameKey), x, y, bindColW, 14,
                    UiColors.LabelDefault, UiFonts.Get("Segoe UI", 8f, FontStyle.Bold), locKey: bindNameKey);
                var btn = MakeButton(indicatorPanel,
                    current.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : current.ToString(),
                    x, y + 16, bindColW, 30, ApplePalette.Blue);
                bindRow++;
                return btn;
            }

            _bindLeftBtn = MakeBindRow("indicators.bindname.left", GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton));
            _bindRightBtn = MakeBindRow("indicators.bindname.right", GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton));
            _bindHazardBtn = MakeBindRow("indicators.bindname.hazard", GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton));
            _bindLightBtn = MakeBindRow("indicators.bindname.lights", _lightToggleBinding);
            y1 += 100;

            string BindDisplay(InputBinding b) => b.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : b.ToString();

            _bindLeftBtn.Click += (s, e) => BindKey("indicators.bindname.left", b =>
            {
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.LeftKeyCode = code,
                    wb => _indicators.LeftWheelButton = wb,
                    _indicators.LeftWheelButton,
                    () => _indicators.ToggleLeft());
                _bindLeftBtn.Text = BindDisplay(b);
                SaveSettings();
            });

            _bindRightBtn.Click += (s, e) => BindKey("indicators.bindname.right", b =>
            {
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.RightKeyCode = code,
                    wb => _indicators.RightWheelButton = wb,
                    _indicators.RightWheelButton,
                    () => _indicators.ToggleRight());
                _bindRightBtn.Text = BindDisplay(b);
                SaveSettings();
            });

            _bindHazardBtn.Click += (s, e) => BindKey("indicators.bindname.hazard", b =>
            {
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.HazardKeyCode = code,
                    wb => _indicators.HazardWheelButton = wb,
                    _indicators.HazardWheelButton,
                    () => _indicators.ToggleHazard());
                _bindHazardBtn.Text = BindDisplay(b);
                SaveSettings();
            });

            _bindLightBtn.Click += (s, e) => BindKey("indicators.bindname.lights", b =>
            {
                ApplyRevBinding(ref _lightToggleBinding, b, ExecuteLightToggle);
                _bindLightBtn.Text = BindDisplay(b);
                SaveSettings();
            });

            int y2 = 64;


            MakeLabel(indicatorPanel, Localization.T("indicators.group.wheel"), indCol2X, y2, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.wheel");

            y2 += 20;

            MakeLabel(indicatorPanel, Localization.T("indicators.centeroff"), indCol2X, y2, indColW - 50, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.centeroff");
            _indicatorAutoCancelCheck = new MacToggleSwitch
            {
                Location = new Point(indCol2X + indColW - 45, y2+6),
                Size = new Size(40, 20),
                Checked = true
            };
            _indicatorAutoCancelCheck.CheckedChanged += (s, e) => SaveSettings();
            indicatorPanel.Controls.Add(_indicatorAutoCancelCheck);

            y2 += 30;

            MakeLabel(indicatorPanel, Localization.T("indicators.centeroff.subtitle"), indCol2X, y2, indColW, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.centeroff.subtitle");

            y2 += 34;

            MakeLabel(indicatorPanel, Localization.T("indicators.armthreshold"), indCol2X, y2, indColW - 90, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.armthreshold");



            _indicatorArmThresholdNumeric = MakeNumericUpDown(
                indicatorPanel, indCol2X + indColW - 90, y2, 70, 28, 1, 100, _indicatorArmThresholdPct, 1,
                v => { _indicatorArmThresholdPct = (int)v; SaveSettings(); });

            MakeLabel(indicatorPanel, "%", indCol2X + indColW - 20, y2 + 4, 20, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 10f));

            y2 += 30;

            MakeLabel(indicatorPanel, Localization.T("indicators.armthreshold.subtitle"), indCol2X, y2, indColW - 50, 28,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.armthreshold.subtitle");

            y2 += 34;

            MakeLabel(indicatorPanel, Localization.T("indicators.centerthreshold"), indCol2X, y2, indColW - 90, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.centerthreshold");

            _indicatorCenterThresholdNumeric = MakeNumericUpDown(
                indicatorPanel, indCol2X + indColW - 90, y2, 70, 28, 1, 90, _indicatorCenterThresholdPct, 1,
                v => { _indicatorCenterThresholdPct = (int)v; SaveSettings(); });
            MakeLabel(indicatorPanel, "%", indCol2X + indColW - 20, y2 + 4, 20, 22, ApplePalette.Text, UiFonts.Get("Segoe UI", 10f));

            y2 += 30;

            MakeLabel(indicatorPanel, Localization.T("indicators.centerthreshold.subtitle"), indCol2X, y2, indColW - 50, 30,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.centerthreshold.subtitle");

            var indCol2Divider = new Panel { Location = new Point(indCol2X, 293), Size = new Size(indColW, 1), BackColor = ApplePalette.Border, Tag = "theme:border" };
            indicatorPanel.Controls.Add(indCol2Divider);

            int y3 = 315;


            MakeLabel(indicatorPanel, Localization.T("indicators.group.sounds"), indCol2X, y3, indColW - 45, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.sounds");

            y3 += 20;

            MakeLabel(indicatorPanel, Localization.T("indicators.soundscheck"), indCol2X, y3, indColW - 45, 30,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.soundscheck");
            _indicatorSoundsCheck = new MacToggleSwitch
            {
                Location = new Point(indCol2X + indColW - 45, y3 + 6),
                Size = new Size(40, 20),
                Checked = true
            };
            _indicatorSoundsCheck.CheckedChanged += (s, e) =>
            {
                _indicatorSoundsIntent.Capture(_indicatorSoundsCheck.Checked);
                if (!_indicatorSoundsCheck.Checked)
                {
                    _indicatorClickOn.Stop();
                    _indicatorClickOff.Stop();
                    _indicatorCancel.Stop();
                }
                SaveSettings();
            };

            y3 += 30;

            indicatorPanel.Controls.Add(_indicatorSoundsCheck);
            MakeLabel(indicatorPanel, Localization.T("indicators.soundscheck.subtitle"), indCol2X, y3, indColW, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.soundscheck.subtitle");

            y3 += 32;

            MakeLabel(indicatorPanel, Localization.T("indicators.volume"), indCol2X, y3, 100, 18,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.volume");

            y3 += 24;

            _indicatorVolumeSlider = new MacSlider
            {
                Location = new Point(indCol2X, y3),
                Size = new Size(indColW-45, 22),
                Minimum = 0,
                Maximum = 100,
                Value = _indicatorSoundsVolume
            };

            _indicatorVolumeValueLabel = MakeLabel(indicatorPanel, $"{_indicatorSoundsVolume}%", indCol2X + indColW - 35, y3, 50, 22,
                ApplePalette.Text, null, ContentAlignment.MiddleLeft);

            _indicatorVolumeSlider.ValueChanged += (s, e) =>
            {
                _indicatorSoundsVolume = _indicatorVolumeSlider.Value;
                _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                ApplyIndicatorVolume();
                SaveSettings();
            };

            indicatorPanel.Controls.Add(_indicatorVolumeSlider);

            const int caLeftX = 24, caLeftW = 260;
            const int caRightX = 304, caRightW = 522;

            var controllerAssistPanel = CreateCard(
                "Controller Assist",
                GridSlot.ControllerAssist,
                850,
                540, locKey: "controllerassist.title");

            MakeLabel(controllerAssistPanel, "STATUS", 730, 14, 55, 22,
                UiColors.LabelDefault, null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _caMasterSwitch = new MacToggleSwitch
            {
                Location = new Point(790, 10),
                Checked = false
            };
            _caMasterSwitch.CheckedChanged += (s, e) => ApplyControllerAssistMasterState(_caMasterSwitch.Checked);
            controllerAssistPanel.Controls.Add(_caMasterSwitch);

            _caBindBtn = MakeButton(controllerAssistPanel, "", 580, 9, 140, 30, ApplePalette.Blue);
            _caBindBtn.Click += (s, e) => BindKey("controllerassist.bindname", b =>
            {
                ApplyRevBinding(ref _caToggleBinding, b, ExecuteControllerAssistToggle);
                RefreshControllerAssistBindLabel();
                SaveSettings();
            });
            RefreshControllerAssistBindLabel();

            _caLockedLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.locked"), 24, 60, 800, 34,
                ApplePalette.StatusWarn, UiFonts.Get("Segoe UI", 9f), ContentAlignment.TopLeft, locKey: "controllerassist.locked");

            var caGroupHeaderFont = UiFonts.Get("Segoe UI", 7.5f, FontStyle.Bold);
            Color caGroupHeaderColor = UiColors.Muted;


            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.group.telemetry"), caLeftX, 104, caLeftW, 16,
                caGroupHeaderColor, caGroupHeaderFont, ContentAlignment.MiddleLeft, locKey: "controllerassist.group.telemetry");

            Label AddTelemetryField(int x, int y, int w, string labelKey, Color valueColor)
            {
                MakeLabel(controllerAssistPanel, Localization.T(labelKey), x, y, w, 16,
                    UiColors.Muted, UiFonts.Get("Segoe UI", 8f, FontStyle.Bold), ContentAlignment.MiddleLeft, locKey: labelKey);
                return MakeLabel(controllerAssistPanel, "0", x, y + 18, w, 26,
                    valueColor, UiFonts.Get("Segoe UI Semibold", 14f), ContentAlignment.MiddleLeft);
            }

            const int statColW = 123, statColGap = 14;
            int statCol2X = caLeftX + statColW + statColGap;
            _caTelemetrySpeedValue = AddTelemetryField(caLeftX, 124, statColW, "controllerassist.telemetry.speed", ApplePalette.TextBlue);
            _caTelemetryRearSlipValue = AddTelemetryField(statCol2X, 124, statColW, "controllerassist.telemetry.rearslip", ApplePalette.TextOrange);
            _caTelemetryRawSteerValue = AddTelemetryField(caLeftX, 176, statColW, "controllerassist.telemetry.rawsteer", ApplePalette.Text);
            _caTelemetryOutputValue = AddTelemetryField(statCol2X, 176, statColW, "controllerassist.telemetry.output", ApplePalette.TextGreen);
            _caTelemetryContributionValue = AddTelemetryField(caLeftX, 228, statColW, "controllerassist.telemetry.contribution", ApplePalette.TextPurple);
            _caTelemetryVibrationValue = AddTelemetryField(statCol2X, 228, statColW, "controllerassist.telemetry.vibration", ApplePalette.TextRed);

            var caLeftDivider = new Panel { Location = new Point(caLeftX, 282), Size = new Size(caLeftW, 1), BackColor = ApplePalette.Border, Tag = "theme:border" };
            controllerAssistPanel.Controls.Add(caLeftDivider);

            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.group.vibration"), caLeftX, 296, caLeftW, 16,
                caGroupHeaderColor, caGroupHeaderFont, ContentAlignment.MiddleLeft, locKey: "controllerassist.group.vibration");

            var caVibrationLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.vibration"), caLeftX, 318, 180, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "controllerassist.vibration");
            _caVibrationSwitch = new MacToggleSwitch { Location = new Point(caLeftX + caLeftW - 40, 314), Size = new Size(40, 20), Checked = true };
            _caVibrationSwitch.CheckedChanged += (s, e) =>
            {
                _assistEngine.RumbleEnabled = _caVibrationSwitch.Checked;
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caVibrationSwitch);

            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.vibrationstrength"), caLeftX, 350, caLeftW, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "controllerassist.vibrationstrength");
            _caVibrationStrengthSlider = new MacSlider
            {
                Location = new Point(caLeftX, 368),
                Size = new Size(190, 22),
                Minimum = 0,
                Maximum = 100,
                Value = (int)Math.Round(_assistEngine.Rumble.RumbleStrength * 100),
            };
            _caVibrationStrengthValueLabel = MakeLabel(controllerAssistPanel, $"{_assistEngine.Rumble.RumbleStrength * 100:0}%", caLeftX + 216, 368, 44, 22,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9f), ContentAlignment.MiddleLeft);
            _caVibrationStrengthSlider.ValueChanged += (s, e) =>
            {
                _assistEngine.Rumble.RumbleStrength = _caVibrationStrengthSlider.Value / 100.0;
                _caVibrationStrengthValueLabel.Text = $"{_caVibrationStrengthSlider.Value}%";
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caVibrationStrengthSlider);

            var caRumbleBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.rumble.button"), caLeftX, 400, caLeftW, 30, ApplePalette.Blue, locKey: "controllerassist.rumble.button");
            caRumbleBtn.Click += (s, e) => ShowControllerAssistRumbleMenu();


            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.group.controller"), caRightX, 104, caRightW, 16,
                caGroupHeaderColor, caGroupHeaderFont, ContentAlignment.MiddleLeft, locKey: "controllerassist.group.controller");

            _caPadStatusLabel = MakeLabel(controllerAssistPanel, "", caRightX, 124, caRightW, 20,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);

            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.group.drivers"), caRightX, 150, caRightW, 16,
                caGroupHeaderColor, caGroupHeaderFont, ContentAlignment.MiddleLeft, locKey: "controllerassist.group.drivers");

            const int driverBtnW = 130, driverBtnH = 26;
            int driverStatusW = caRightW - driverBtnW - 10;

            _caVirtualPadStatusLabel = MakeLabel(controllerAssistPanel, "", caRightX, 170, driverStatusW, 20,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);
            _caVigemInstallBtn = MakeButton(controllerAssistPanel, Localization.T("drivers.button.install"), caRightX + driverStatusW + 10, 168, driverBtnW, driverBtnH, locKey: "drivers.button.install");
            _caVigemInstallBtn.Click += (s, e) => InstallDriverAsync(DriverKind.ViGEmBus, _caVigemInstallBtn);

            _caHidHideStatusLabel = MakeLabel(controllerAssistPanel, "", caRightX, 196, driverStatusW, 20,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleLeft);
            _caHidHideInstallBtn = MakeButton(controllerAssistPanel, Localization.T("drivers.button.install"), caRightX + driverStatusW + 10, 194, driverBtnW, driverBtnH, locKey: "drivers.button.install");
            _caHidHideInstallBtn.Click += (s, e) => InstallDriverAsync(DriverKind.HidHide, _caHidHideInstallBtn);

            var caHidePadLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.hidepad"), caRightX, 224, caRightW - 50, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 9.5f), ContentAlignment.MiddleLeft, locKey: "controllerassist.hidepad");
            _caHidePadSwitch = new MacToggleSwitch { Location = new Point(caRightX + caRightW - 40, 220), Size = new Size(40, 20) };
            _caHidePadSwitch.CheckedChanged += (s, e) =>
            {
                SaveSettings();
                if (_caMasterSwitch.Checked)
                    ApplyPadCloakState(_caHidePadSwitch.Checked);
            };
            controllerAssistPanel.Controls.Add(_caHidePadSwitch);

            var caRightDivider = new Panel { Location = new Point(caRightX, 252), Size = new Size(caRightW, 1), BackColor = ApplePalette.Border, Tag = "theme:border" };
            controllerAssistPanel.Controls.Add(caRightDivider);

            var caSteeringAssistLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.steeringassist"), caRightX, 266, 250, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "controllerassist.steeringassist");
            _caSteeringAssistSwitch = new MacToggleSwitch { Location = new Point(caRightX + caRightW - 40, 262), Size = new Size(40, 20), Checked = true };
            _caSteeringAssistSwitch.CheckedChanged += (s, e) =>
            {
                _assistEngine.MasterEnabled = _caSteeringAssistSwitch.Checked;
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caSteeringAssistSwitch);

            var caPresetLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.preset"), caRightX, 298, 100, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "controllerassist.preset");

            void SelectPreset(AssistPreset preset)
            {
                _assistEngine.Settings = AssistSettings.ForPreset(preset);
                RefreshControllerAssistPresetButtons();
                SaveSettings();
            }

            const int presetBtnW = 123, presetBtnGap = 10;
            _caPresetOffBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.preset.off"), caRightX, 320, presetBtnW, 30, locKey: "controllerassist.preset.off");
            _caPresetOffBtn.Click += (s, e) => SelectPreset(AssistPreset.Off);
            _caPresetLightBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.preset.light"), caRightX + (presetBtnW + presetBtnGap), 320, presetBtnW, 30, locKey: "controllerassist.preset.light");
            _caPresetLightBtn.Click += (s, e) => SelectPreset(AssistPreset.Light);
            _caPresetMediumBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.preset.medium"), caRightX + (presetBtnW + presetBtnGap) * 2, 320, presetBtnW, 30, locKey: "controllerassist.preset.medium");
            _caPresetMediumBtn.Click += (s, e) => SelectPreset(AssistPreset.Medium);
            _caPresetStrongBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.preset.strong"), caRightX + (presetBtnW + presetBtnGap) * 3, 320, presetBtnW, 30, locKey: "controllerassist.preset.strong");
            _caPresetStrongBtn.Click += (s, e) => SelectPreset(AssistPreset.Strong);

            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.trim.countersteer"), caRightX, 362, caRightW, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "controllerassist.trim.countersteer");
            _caCountersteerTrimSlider = new MacSlider
            {
                Location = new Point(caRightX, 380),
                Size = new Size(caRightW - 70, 22),
                Minimum = -50,
                Maximum = 50,
                Value = (int)_assistEngine.CountersteerTrimPercent,
                CenterValue = 0,
            };
            _caCountersteerTrimValueLabel = MakeLabel(controllerAssistPanel, $"{_assistEngine.CountersteerTrimPercent:0}%", caRightX + caRightW - 60, 380, 60, 22,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9f), ContentAlignment.MiddleLeft);
            _caCountersteerTrimSlider.ValueChanged += (s, e) =>
            {
                _assistEngine.CountersteerTrimPercent = _caCountersteerTrimSlider.Value;
                _caCountersteerTrimValueLabel.Text = $"{_caCountersteerTrimSlider.Value}%";
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caCountersteerTrimSlider);

            MakeLabel(controllerAssistPanel, Localization.T("controllerassist.trim.strength"), caRightX, 412, caRightW, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "controllerassist.trim.strength");
            _caAssistStrengthTrimSlider = new MacSlider
            {
                Location = new Point(caRightX, 430),
                Size = new Size(caRightW - 70, 22),
                Minimum = -50,
                Maximum = 50,
                Value = (int)_assistEngine.AssistStrengthTrimPercent,
                CenterValue = 0,
            };
            _caAssistStrengthTrimValueLabel = MakeLabel(controllerAssistPanel, $"{_assistEngine.AssistStrengthTrimPercent:0}%", caRightX + caRightW - 60, 430, 60, 22,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9f), ContentAlignment.MiddleLeft);
            _caAssistStrengthTrimSlider.ValueChanged += (s, e) =>
            {
                _assistEngine.AssistStrengthTrimPercent = _caAssistStrengthTrimSlider.Value;
                _caAssistStrengthTrimValueLabel.Text = $"{_caAssistStrengthTrimSlider.Value}%";
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caAssistStrengthTrimSlider);

            var caTuningBtn = MakeButton(controllerAssistPanel, Localization.T("controllerassist.tuning.button"), caRightX, 462, 250, 30, ApplePalette.Blue, locKey: "controllerassist.tuning.button");
            caTuningBtn.Click += (s, e) => ShowControllerAssistTuningMenu();

            var caOverlayBarLabel = MakeLabel(controllerAssistPanel, Localization.T("controllerassist.overlaybar"), caRightX, 502, 350, 22,
                UiColors.LabelDefault, UiFonts.Get("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "controllerassist.overlaybar");
            _caOverlayBarSwitch = new MacToggleSwitch { Location = new Point(caRightX + caRightW - 40, 498), Size = new Size(40, 20) };
            _caOverlayBarSwitch.CheckedChanged += (s, e) =>
            {
                _overlay.ControllerAssistBarVisible = _caOverlayBarSwitch.Checked;
                SaveSettings();
            };
            controllerAssistPanel.Controls.Add(_caOverlayBarSwitch);

            _caControls.AddRange(new Control[]
            {
                caVibrationLabel, _caVibrationSwitch, _caVibrationStrengthSlider, _caVibrationStrengthValueLabel, caRumbleBtn,
                caSteeringAssistLabel, _caSteeringAssistSwitch,
                caPresetLabel, _caPresetOffBtn, _caPresetLightBtn, _caPresetMediumBtn, _caPresetStrongBtn,
                _caCountersteerTrimSlider, _caCountersteerTrimValueLabel,
                _caAssistStrengthTrimSlider, _caAssistStrengthTrimValueLabel,
                caTuningBtn, caOverlayBarLabel, _caOverlayBarSwitch,
                caHidePadLabel, _caHidePadSwitch,
            });

            RefreshControllerAssistPresetButtons();

            statusPanel = CreateCard(
                "Warnings & Events",
                GridSlot.Status,
                420,
                245, locKey: "status.title");

            var logClearBtn = MakeLabel(statusPanel, Localization.T("status.clear"), 420 - 16 - 60, 16, 60, 20,
                ApplePalette.Secondary, UiFonts.Get("Segoe UI Semibold", 8.5f), ContentAlignment.MiddleRight, locKey: "status.clear");
            logClearBtn.Cursor = Cursors.Hand;
            logClearBtn.MouseEnter += (s, e) => logClearBtn.ForeColor = ApplePalette.Blue;
            logClearBtn.MouseLeave += (s, e) => logClearBtn.ForeColor = ApplePalette.Secondary;
            logClearBtn.Click += (s, e) => _statusLabel.Clear();

            var logConsoleWrapper = new Panel
            {
                Location = new Point(16, 48),
                Size = new Size(420 - 32, 245 - 48 - 16),
                BackColor = Color.Transparent,
            };
            logConsoleWrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, logConsoleWrapper.Width - 1, logConsoleWrapper.Height - 1);
                using GraphicsPath path = DrawingHelpers.RoundedPath(rect, 10);
                using Pen border = new Pen(ApplePalette.Border);
                e.Graphics.DrawPath(border, path);
            };
            statusPanel.Controls.Add(logConsoleWrapper);

            _statusLabel = new NoWheelRichTextBox
            {
                Location = new Point(6, 6),
                Size = new Size(logConsoleWrapper.Width - 12, logConsoleWrapper.Height - 12),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Consolas", 8f),
                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,
                WordWrap = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            logConsoleWrapper.Controls.Add(_statusLabel);
            AppendStatusMessage(Localization.T("status.hint"));

            ResumeLayout();

            BuildTitleBar();

            this.Paint += MainForm_Paint;
        }
    }
}
