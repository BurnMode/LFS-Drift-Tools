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
    public partial class MainForm : Form
    {

        private readonly string ConfigsDir =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "Configs");
        private readonly string SettingsFile =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "Configs", "settings.json");
        private readonly string ColorsFile =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "Configs", "colors.json");
        private readonly string RevLimiterFile =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "Configs", "revlimiter.json");

        private readonly string DataImagesDir =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Images");

        private readonly SoundPlayer _indicatorClickOn =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Sounds", "indicator_click_on.wav"));

        private readonly SoundPlayer _indicatorClickOff =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Sounds", "indicator_click_off.wav"));

        private readonly SoundPlayer _indicatorCancel =
            new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Sounds", "indicator_cancel.wav"));

        private string? _missingIndicatorSoundsWarning = null;

        private OverlayForm _overlay = null!;

        private InSimConnection _insim;

        private OutGaugeConnection _outGauge = new OutGaugeConnection();
        private OutSimConnection _outSim = new OutSimConnection();
        private readonly StateEstimator _controllerAssistState = new StateEstimator();
        private readonly AssistEngine _assistEngine = new AssistEngine();
        private readonly VirtualPad _virtualPad = new VirtualPad();
        private AssistLoop _assistLoop = null!;

        private DriftEngine _drift = new DriftEngine();
        private IndicatorManager _indicators = new IndicatorManager();
        private byte _lastKnownPlayerPLID = 0;
        private byte _myOwnPlid => _insim.LocalPlayerPLID;
        private bool _sawOwnOutGaugeSinceConnect = false;
        private bool _sawAnyOutGaugeSinceConnect = false;
        private bool _localPlayerParked = false;
        private string? _localPlayerNickAtPark = null;

        private double _speedKmh = 0;
        private double _driftAngle = 0;
        private long _runPoints = 0;
        private long _totalScore = 0;
        private double _combo = 1;
        private bool _isDrifting = false;
        private bool _isSpeeding = false;
        private bool _isBurnout = false;
        private string _lastAward = "";

        public int CalibratedMAXRPM = 7600;
        public int SavedMSCUT = 40;
        public bool calibrationON = false;

        private Dictionary<string, VehicleRevSettings> _vehicleRevSettings = new();
        private string _currentCarName = "";

        private RevLimiterCalibrationPromptForm? _revLimiterCalibrationPrompt;
        private readonly HashSet<string> _revLimiterPromptShownForCars = new();

        private readonly DataFreshnessGate _outGaugeFreshness = new(TimeSpan.FromMilliseconds(500));

        private bool _outGaugeConnectionEnabled = false;

        private readonly System.Windows.Forms.Timer _outGaugeFreshnessPoll = new() { Interval = 250 };
        private bool _wasOutGaugeFresh = false;

        private readonly System.Windows.Forms.Timer _controllerAssistDisplayTimer = new() { Interval = 33 };
        private System.Windows.Forms.Timer? _overlayDetachTimer;

        private void OnControllerAssistDisplayTick(object? sender, EventArgs e)
        {
            var snap = _assistEngine.LastSnapshot;
            bool assistRunning = _assistLoop.IsRunning;
            _overlay.UpdateControllerAssist(assistRunning, snap.RawSteerPercent, snap.OutputSteerPercent);

            int desiredInterval = assistRunning ? AssistDisplayActiveIntervalMs : AssistDisplayIdleIntervalMs;
            if (_controllerAssistDisplayTimer.Interval != desiredInterval)
                _controllerAssistDisplayTimer.Interval = desiredInterval;

            if (WindowState == FormWindowState.Minimized) return;
            if (assistRunning && ++_assistLabelTick % AssistLabelEveryNthTick != 0) return;

            _caTelemetrySpeedValue.Text = $"{snap.SpeedKmh:F0} km/h";
            _caTelemetryRearSlipValue.Text = $"{snap.RearSlipDeg:F1} deg";
            _caTelemetryRawSteerValue.Text = $"{snap.RawSteerPercent:F0} %";
            _caTelemetryOutputValue.Text = $"{snap.OutputSteerPercent:F0} %";
            _caTelemetryContributionValue.Text = $"{snap.AssistContribution * 100:F0} %";
            _caTelemetryVibrationValue.Text = $"{snap.RumbleLeft01 * 100:F0}% / {snap.RumbleRight01 * 100:F0}%";

            _caTelemetrySpeedValue.ForeColor = ApplePalette.TextBlue;
            _caTelemetryRearSlipValue.ForeColor = ApplePalette.TextOrange;
            _caTelemetryRawSteerValue.ForeColor = ApplePalette.Text;
            _caTelemetryOutputValue.ForeColor = ApplePalette.TextGreen;
            _caTelemetryContributionValue.ForeColor = ApplePalette.TextPurple;
            _caTelemetryVibrationValue.ForeColor = ApplePalette.TextRed;
        }

        private const int WM_DEVICECHANGE = 0x0219;
        private const double DeviceListMaxAgeSec = 3.0;
        private const double BaselineMaxAgeSec = 5.0;

        private bool _deviceListDirty = true;
        private bool _baselineDirty = true;
        private DateTime _deviceListRefreshedUtc = DateTime.MinValue;
        private DateTime _baselineCapturedUtc = DateTime.MinValue;
        private List<(Guid Guid, string Name)> _deviceListCache = new();

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                _deviceListDirty = true;
                _baselineDirty = true;
            }

            base.WndProc(ref m);
        }

        private bool RefreshDeviceListIfNeeded()
        {
            bool stale = (DateTime.UtcNow - _deviceListRefreshedUtc).TotalSeconds >= DeviceListMaxAgeSec;
            if (!_deviceListDirty && !stale) return false;

            _deviceListCache = _wheelInput.GetAvailableDevices();
            _deviceListRefreshedUtc = DateTime.UtcNow;
            _deviceListDirty = false;
            return true;
        }

        private List<(Guid Guid, string Name)> GetCachedDeviceList()
        {
            RefreshDeviceListIfNeeded();
            return _deviceListCache;
        }
        private void OnOutGaugeFreshnessPollTick(object? sender, EventArgs e)
        {
            UpdateOutGaugeStatusLabel();

            if (_virtualPad.IsAvailable)
                _virtualPadLastActiveUtc = DateTime.UtcNow;

            if (RefreshDeviceListIfNeeded())
                TryAutoSelectAssistController();

            SyncControllerAssistAvailability();

            // Keeps a rolling snapshot of "physical" XInput pads for PadCloak, taken only while
            // the virtual pad is definitely not connected yet, so whichever device(s) show up
            // here are never our own emulated one. Re-taken when Windows reports a device change
            // (or, as a fallback, every few seconds) instead of on every tick.
            bool baselineStale = (DateTime.UtcNow - _baselineCapturedUtc).TotalSeconds >= BaselineMaxAgeSec;
            if (!_assistLoop.IsRunning && !VirtualPadMayBePresent && (_baselineDirty || baselineStale))
            {
                PadCloak.CaptureBaseline();
                _baselineDirty = false;
                _baselineCapturedUtc = DateTime.UtcNow;
            }

            bool fresh = _outGaugeFreshness.IsFresh;
            if (fresh == _wasOutGaugeFresh) return;
            _wasOutGaugeFresh = fresh;

            if (_outGaugeStateLabel != null)
            {
                _outGaugeStateLabel.Text = Localization.T(fresh ? "status.outgauge.connected" : "status.outgauge.disconnected");
                _outGaugeStateLabel.ForeColor = fresh ? ApplePalette.StatusOk : ApplePalette.StatusIdle;
            }

            if (fresh) return;

            _drift.SetOutGaugeDataFresh(false);
            _drift.DeactivateLapCounting();
            _overlay.SetLapBoxVisible(false);
            _overlay.UpdateAccentColor(OverlayColorIdle);
            _overlay.ResetOutGaugeData();

            if (_dashLeftLamp != null)
            {
                _dashLeftLamp.Lit = false;
                _dashRightLamp.Lit = false;
                _dashHighBeamLamp.Lit = false;
            }
        }

        private void SyncAdvancedOutGaugeAvailability()
        {
            if (_advancedOutGaugeCheck == null) return;
            bool available = _outGaugeConnectionEnabled;
            _advancedOutGaugeCheck.Enabled = available;
            _drift.SetAdvancedOutGaugeEnabled(available && _advancedOutGaugeCheck.Checked);
        }

        private void SyncIndicatorSoundsAvailability()
        {
            if (_indicatorSoundsCheck == null) return;
            bool available = (_indicatorsMasterEnabledCheck?.Checked ?? true) && _outGaugeConnectionEnabled;
            _indicatorSoundsIntent.Apply(_indicatorSoundsCheck, available);
        }

        private void SyncRevLimiterHudAvailability()
        {
            if (_showRPMHudCheck == null) return;
            bool available = (_hudMasterEnabledCheck?.Checked ?? true) && _outGaugeConnectionEnabled;
            _showRpmHudIntent.Apply(_showRPMHudCheck, available);
        }

        private void ApplyRevUnsupportedState()
        {
            if (_revEnableSwitch == null || _revUnsupportedSwitch == null) return;

            _revUnsupportedSwitch.SetCheckedSilent(_revCarUnsupported);
            if (!_outGaugeConnectionEnabled) return;

            bool supported = !_revCarUnsupported;
            _revEnableSwitch.Enabled = supported;
            _calibrateBtn1.Enabled = supported;
            _revLimiter.Enabled = supported && _revEnabledIntent.Wanted;
            _revEnableSwitch.SetCheckedSilent(supported && _revEnabledIntent.Wanted);
            SyncRevAutoCalibrateAvailability();
        }

        private void SetCurrentCarRevUnsupported(bool unsupported, bool detectedAutomatically)
        {
            if (string.IsNullOrEmpty(_currentCarName)) return;

            _revCarUnsupported = unsupported;
            if (unsupported) _revLimiterCalibrationPrompt?.Close();

            SaveVehicleRevSettings(_currentCarName);
            ApplyRevUnsupportedState();

            if (detectedAutomatically)
                AppendStatusMessage(string.Format(Localization.T("rev.unsupported.auto"), _currentCarName, UnsupportedRevRpmThreshold), StatusErrorColor);
        }

        private void SyncRevAutoCalibrateAvailability()
        {
            if (_revAutoCalibrateNewCarCheck == null) return;
            bool available = _outGaugeConnectionEnabled && (_revEnableSwitch?.Checked ?? false);
            _revAutoCalibrateIntent.Apply(_revAutoCalibrateNewCarCheck, available);
        }

        private void UpdateOverlayAttachment()
        {
            bool wantsOverlay = (_showOverlayCheck?.Checked ?? false) || (_speedoTachoEnabledCheck?.Checked ?? false);
            if (wantsOverlay)
            {
                IntPtr lfsHwnd = LfsWindow.Find();
                if (lfsHwnd != IntPtr.Zero)
                {
                    _overlay.UpdateScore(_drift.TotalScore);
                    _overlay.UpdateAccentColor(OverlayColorIdle);
                    _overlay.UpdateMaxRpm(CalibratedMAXRPM);
                    _overlay.AttachTo(lfsHwnd);
                }
            }
            else
            {
                _overlay.Detach();
            }
        }

        private void ApplyOutGaugeConnectionState(bool on)
        {
            if (on)
            {
                _outGauge.UdpPort = (int)_outGaugePortBox.Value;
                _outGauge.Start();

                if (!_outGauge.IsRunning)
                {
                    _outGaugeConnectionSwitch.SetCheckedSilent(false);
                    return;
                }

                _outGaugeConnectionEnabled = true;

                _advancedOutGaugeCheck?.SetCheckedSilent(_advancedOutGaugeIntent.Wanted);
                SyncAdvancedOutGaugeAvailability();
                SyncIndicatorSoundsAvailability();
                SyncRevLimiterHudAvailability();

                _revEnableSwitch.Enabled = true;
                _calibrateBtn1.Enabled = true;
                _revLimiterNumeric.Enabled = true;
                _revCutMS.Enabled = true;
                revBindingsBtn.Enabled = true;
                _revLimiter.Enabled = _revEnabledIntent.Wanted;
                _revEnableSwitch.SetCheckedSilent(_revEnabledIntent.Wanted);
                SyncRevAutoCalibrateAvailability();
                ApplyRevUnsupportedState();
                if (!_revLimiter.IsRunning)
                    _revLimiter.Start();

                _speedoTachoEnabledCheck.Enabled = true;
                _speedoTachoEnabledCheck.SetCheckedSilent(_speedoTachoIntent.Wanted);
                _overlay.SpeedoTachoEnabled = _speedoTachoIntent.Wanted;
                UpdateOverlayAttachment();

                _revLockedLabel.Text = string.Format(Localization.T("rev.connected"), _outGauge.UdpPort);
                _revLockedLabel.ForeColor = ApplePalette.StatusOk;
            }
            else
            {
                // Only snapshot the live switch states on a real on->off transition: a repeated
                // "off" would otherwise read the already-forced-off values back over the user's
                // remembered choices.
                bool wasOn = _outGaugeConnectionEnabled;

                _outGauge.Stop();
                _outGaugeConnectionEnabled = false;
                SyncAdvancedOutGaugeAvailability();
                SyncIndicatorSoundsAvailability();
                SyncRevLimiterHudAvailability();

                _revLockedLabel.Text = Localization.T("rev.locked");
                _revLockedLabel.ForeColor = ApplePalette.StatusWarn;

                if (wasOn) _speedoTachoIntent.Capture(_speedoTachoEnabledCheck.Checked);
                _overlay.SpeedoTachoEnabled = false;
                _speedoTachoEnabledCheck.SetCheckedSilent(false);
                _speedoTachoEnabledCheck.Enabled = false;
                UpdateOverlayAttachment();

                if (_advancedOutGaugeCheck != null)
                {
                    if (wasOn) _advancedOutGaugeIntent.Capture(_advancedOutGaugeCheck.Checked);
                    _advancedOutGaugeCheck.SetCheckedSilent(false);
                }

                if (wasOn) _revEnabledIntent.Capture(_revEnableSwitch.Checked);
                _revLimiter.Enabled = false;
                _revEnableSwitch.SetCheckedSilent(false);
                _revEnableSwitch.Enabled = false;
                _calibrateBtn1.Enabled = false;
                _revLimiterNumeric.Enabled = false;
                _revCutMS.Enabled = false;
                revBindingsBtn.Enabled = false;
                SyncRevAutoCalibrateAvailability();
            }

            UpdateOutGaugeStatusLabel();

            if (_inGameSettingsOverlay != null && _inGameSettingsOverlay.Visible)
                RefreshInGameSettingsOverlayValues();
        }

        private void ApplyOutSimConnectionState(bool on)
        {
            if (on)
            {
                string hex = _outSimOptsBox.Text.Trim().TrimStart('0', 'x', 'X');
                if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint parsedOpts))
                {
                    _outSimStatusLabel.Text = string.Format(Localization.T("connection.outsim.badopts"), _outSimOptsBox.Text);
                    _outSimStatusLabel.ForeColor = StatusErrorColor;
                    _outSimConnectionSwitch.SetCheckedSilent(false);
                    return;
                }

                _outSim.Opts = (OutSimOpts)parsedOpts;
                _outSim.UdpPort = (int)_outSimPortBox.Value;
                _outSim.Start();

                if (!_outSim.IsRunning)
                {
                    _outSimConnectionSwitch.SetCheckedSilent(false);
                    return;
                }
            }
            else
            {
                _outSim.Stop();
                _drift.SetUpsideDown(false);
            }

            UpdateOutSimStatusLabel();
            SyncControllerAssistAvailability();
        }

        private const double OutGaugePacketFreshSec = 2.0;

        private void UpdateOutGaugeStatusLabel()
        {
            if (_outGaugeStatusLabel == null) return;

            string text;
            Color color;

            if (!_outGauge.IsRunning)
            {
                text = Localization.T("status.outgauge.disconnected");
                color = ApplePalette.StatusIdle;
            }
            else if (_outGauge.PacketsReceived == 0 ||
                     (DateTime.UtcNow - _outGauge.LastPacketUtc).TotalSeconds > OutGaugePacketFreshSec)
            {
                text = string.Format(Localization.T("status.outgauge.listening"), _outGauge.UdpPort);
                color = ApplePalette.StatusWarn;
            }
            else
            {
                text = string.Format(Localization.T("status.outgauge.receiving"), _outGauge.PacketsReceived);
                color = ApplePalette.StatusOk;
            }

            if (_outGaugeStatusLabel.Text != text) _outGaugeStatusLabel.Text = text;
            if (_outGaugeStatusLabel.ForeColor != color) _outGaugeStatusLabel.ForeColor = color;
        }

        private void UpdateOutSimStatusLabel()
        {
            if (_outSimStatusLabel == null) return;

            if (!_outSim.IsRunning)
            {
                _outSimStatusLabel.Text = Localization.T("status.outsim.disconnected");
                _outSimStatusLabel.ForeColor = ApplePalette.StatusIdle;
            }
            else if (_outSim.PacketsReceived == 0)
            {
                _outSimStatusLabel.Text = string.Format(Localization.T("status.outsim.listening"), _outSim.UdpPort);
                _outSimStatusLabel.ForeColor = ApplePalette.StatusWarn;
            }
            else
            {
                _outSimStatusLabel.Text = string.Format(Localization.T("status.outsim.receiving"), _outSim.PacketsReceived);
                _outSimStatusLabel.ForeColor = ApplePalette.StatusOk;
            }
        }

        private void ShowInfoPopup(string title, string message, Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 360, 180, out _);

            MakeLabel(card, title, 20, 15, 320, 24, ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, message, 20, 50, 320, 80, ApplePalette.Text, UiFonts.Get("Segoe UI", 9.5f), ContentAlignment.TopLeft);

            var okBtn = MakeButton(card, Localization.T("common.ok"), 20, 136, 320, 30, ApplePalette.Blue);
            okBtn.Click += (s, e) => popup.Close();

            ShowModalPopup(popup, ownerForm);
        }

        private const int TitleBarHeight = 40;
        private Panel _titleBar = null!;
        private Label _titleBarLabel = null!;
        private CaptionButton _btnMinimize = null!;
        private CaptionButton _btnClose = null!;

        private Label _rpmLabel = null!;
        private Label _revCutLabel = null!;
        private Label _revLimitLabel = null!;
        private MinimalNumericUpDown _revLimiterNumeric = null!;
        private MinimalNumericUpDown _revCutMS = null!;
        private MinimalNumericUpDown _minDriftSpeedNumeric = null!;
        private MinimalNumericUpDown _maxBurnoutSpeedNumeric = null!;

        private MacProgressBar _rpmBar = null!;

        private InGameHudManager _hud = null!;

        private Label _angleValueLabel = null!;
        private Label _comboValueLabel = null!;
        private Label _scoreValueLabel = null!;
        private Label _runValueLabel = null!;
        private Label _bestRunValueLabel = null!;
        private Label _bestDriftValueLabel = null!;
        private Label _bestDeepDriftValueLabel = null!;
        private Label _driverInfoLabel = null!;
        private Label _vehicleInfoLabel = null!;
        private MacToggleSwitch _revUnsupportedSwitch = null!;
        private bool _revCarUnsupported;
        private const int UnsupportedRevRpmThreshold = 20000;
        private Label _revLockedLabel = null!;
        private NoWheelRichTextBox _statusLabel = null!;
        private TextBox _hostBox = null!, _adminBox = null!;
        private MinimalNumericUpDown _portBox = null!;
        private Button _resetBtn = null!;
        private MacToggleSwitch _revEnableSwitch = null!;

        private readonly UserIntent _revEnabledIntent = new(true);
        private readonly UserIntent _revAutoCalibrateIntent = new(true);

        private MacToggleSwitch _revAutoCalibrateNewCarCheck = null!;
        private MacToggleSwitch _connectionSwitch = null!;

        private MacToggleSwitch _outGaugeConnectionSwitch = null!;
        private MinimalNumericUpDown _outGaugePortBox = null!;
        private MacToggleSwitch _outSimConnectionSwitch = null!;
        private MinimalNumericUpDown _outSimPortBox = null!;
        private TextBox _outSimOptsBox = null!;
        private Label _outSimStatusLabel = null!;
        private MacToggleSwitch _caMasterSwitch = null!;
        private Button _caBindBtn = null!;
        private InputBinding _caToggleBinding = InputBinding.None;
        private MacToggleSwitch _caSteeringAssistSwitch = null!;
        private MacToggleSwitch _caVibrationSwitch = null!;
        private Button _caPresetOffBtn = null!, _caPresetLightBtn = null!, _caPresetMediumBtn = null!, _caPresetStrongBtn = null!;
        private MacSlider _caCountersteerTrimSlider = null!;
        private Label _caCountersteerTrimValueLabel = null!;
        private MacSlider _caAssistStrengthTrimSlider = null!;
        private Label _caAssistStrengthTrimValueLabel = null!;
        private Label _caLockedLabel = null!;
        private Label _caPadStatusLabel = null!;
        private Label _caVirtualPadStatusLabel = null!;
        private Label _caHidHideStatusLabel = null!;
        private Button _caVigemInstallBtn = null!;
        private Button _caHidHideInstallBtn = null!;
        private MacToggleSwitch _caHidePadSwitch = null!;
        private MacToggleSwitch _caOverlayBarSwitch = null!;
        private Label _caTelemetrySpeedValue = null!;
        private Label _caTelemetryRearSlipValue = null!;
        private Label _caTelemetryRawSteerValue = null!;
        private Label _caTelemetryOutputValue = null!;
        private Label _caTelemetryContributionValue = null!;
        private Label _caTelemetryVibrationValue = null!;
        private MacSlider _caVibrationStrengthSlider = null!;
        private Label _caVibrationStrengthValueLabel = null!;
        private readonly List<Control> _caControls = new();
        private string _lfsGameFolder = "";
        private Label _lfsFolderStatusLabel = null!;
        private MacToggleSwitch _themeSwitch = null!;
        private Label _connectionStateLabel = null!;

        private Label _outGaugeStateLabel = null!;
        private Label _outGaugeStatusLabel = null!;
        private MacToggleSwitch _showHudCheck = null!;
        private MacToggleSwitch _showRPMHudCheck = null!;
        private MacToggleSwitch _showOverlayCheck = null!;

        private MacToggleSwitch _hudMasterEnabledCheck = null!;
        private readonly UserIntent _scoreOverlayIntent = new(true);
        private readonly UserIntent _insimHudIntent = new(false);

        private readonly UserIntent _showRpmHudIntent = new(false);

        private ForzaHudPreviewControl _hudPreview = null!;

        private MacToggleSwitch _advancedOutGaugeCheck = null!;

        private readonly UserIntent _advancedOutGaugeIntent = new(true);

        private MacToggleSwitch _speedoTachoEnabledCheck = null!;
        private readonly UserIntent _speedoTachoIntent = new(true);
        private MinimalNumericUpDown _speedoTachoOffsetXNumeric = null!;
        private MinimalNumericUpDown _speedoTachoOffsetYNumeric = null!;
        private MinimalNumericUpDown _speedoTachoScaleNumeric = null!;
        private MacToggleSwitch _speedUnitToggle = null!;
        private bool _useMph = false;

        private SpeedoTachoPreviewControl _speedoTachoPreview = null!;

        private Color _speedoTachoRedlineColor = Color.Red;
        private float _redlineThickness = 4f;
        private MacSlider? _redlineThicknessSlider;
        private Color _speedoTachoTextColor = Color.White;
        private Color _speedoTachoIndicatorColor = Color.FromArgb(255, 225, 225, 230);
        private Color _speedoTachoTickColor = Color.FromArgb(255, 215, 215, 218);
        private Color _speedoTachoBackgroundColor = Color.FromArgb(50, 15, 15, 20);

        private Image? _speedoBackgroundImage = null;
        private string _speedoBackgroundImagePath = "";
        private float _speedoBackgroundPanX = 0f;
        private float _speedoBackgroundPanY = 0f;
        private float _speedoBackgroundZoom = 1.0f;
        private float _speedoBackgroundOpacity = 1.0f;
        private CircularCropControl? _speedoBackgroundCrop;
        private MacSlider? _speedoBackgroundOpacitySlider;
        private MacSlider? _speedoBackgroundZoomSlider;

        private readonly SpeedoPresetData[] _speedoPresets = { new SpeedoPresetData(), SpeedoPresetData.Default2() };
        private int _activeSpeedoPresetIndex = 0;
        private Label _speedoPresetLabel = null!;

        private string InSimColor1 = "^7";
        private string InSimColor2 = "^6";
        private string InSimColor3 = "^3";
        private string InSimColor4 = "^5";
        private string InSimColor5 = "^1";

        private string InSimColor6 = "^2";

        private string InSimColorIdle = "^7";

        private Color OverlayColor1 = Color.FromArgb(255, 255, 243, 0);
        private Color OverlayColor2 = Color.FromArgb(255, 255, 147, 0);
        private Color OverlayColor3 = Color.FromArgb(255, 255, 74, 0);
        private Color OverlayColor4 = Color.FromArgb(255, 255, 0, 0);
        private Color OverlayColor5 = Color.FromArgb(255, 255, 0, 118);
        private Color OverlayColor6 = Color.FromArgb(255, 211, 0, 255);
        private Color OverlayColorIdle = Color.White;

        private Button? _colorBtn1;
        private Button? _colorBtn2;
        private Button? _colorBtn3;
        private Button? _colorBtn4;
        private Button? _colorBtn5;
        private Button? _colorBtn6;
        private Button? _colorBtnIdle;
        private Button _calibrateBtn1 = null!;
        private Button revBindingsBtn = null!;
        private Button _langBtn = null!;
        private Label _indicatorStatusLabel = null!;
        private DashboardLampIcon _dashLeftLamp = null!;
        private DashboardLampIcon _dashRightLamp = null!;
        private DashboardLampIcon _dashHighBeamLamp = null!;
        private MacToggleSwitch _indicatorSoundsCheck = null!;
        private MacToggleSwitch _indicatorAutoCancelCheck = null!;
        private MacSlider _indicatorVolumeSlider = null!;
        private Label _indicatorVolumeValueLabel = null!;
        private int _indicatorSoundsVolume = 100;

        private MacToggleSwitch _indicatorsMasterEnabledCheck = null!;

        private Panel _inputControllerTilesPanel = null!;

        private readonly UserIntent _indicatorSoundsIntent = new(true);
        private readonly UserIntent _indicatorAutoCancelIntent = new(true);

        private MinimalNumericUpDown _indicatorArmThresholdNumeric = null!;
        private int _indicatorArmThresholdPct = 35;

        private MinimalNumericUpDown _indicatorCenterThresholdNumeric = null!;
        private int _indicatorCenterThresholdPct = 5;

        private Button _bindLeftBtn = null!, _bindRightBtn = null!, _bindHazardBtn = null!, _bindLightBtn = null!;

        private Label _indicatorDisplayLabel = null!;

        private Label _wheelAngleLabel = null!;

        #region UI

        private NoScrollBarPanel _mainScrollPanel = null!;

        private MinimalScrollbar _mainScrollbar = null!;

        private RoundedPanel headerPanel = null!;

        private RoundedPanel speedometerPanel = null!;

        private RoundedPanel scorePanel = null!;

        private RoundedPanel connectionPanel = null!;
        private RoundedPanel hudPanel = null!;

        private RoundedPanel revLimiterPanel = null!;
        private RoundedPanel indicatorPanel = null!;

        private RoundedPanel statusPanel = null!;

        private readonly CollisionDetector _collisions = new();
        private MacToggleSwitch _collisionDetectCheck = null!;
        private MacToggleSwitch _objectCollisionDetectCheck = null!;
        private bool _objectCollisionDetectionEnabled = true;
        #endregion

        private WindowShadow _shadow;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;

            // Undoes a device hide left over from a run that never reached a clean shutdown
            // (crash, task-killed), before anything else in this session touches HidHide.
            PadCloak.RestoreFromCrash();

            ApplePalette.SetDark(true);
            BackColor = ApplePalette.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);

            _insim = new InSimConnection();

            _insim.CarDataReceived += OnCarData;
            _insim.StatusChanged += OnStatus;
            _insim.Connected += OnConnected;
            _insim.Disconnected += OnDisconnected;
            _insim.ConnectFailed += OnConnectFailed;
            _insim.RaceStateChanged += (s, inRace) => PostToUi(() =>
            {
                if (inRace)
                {
                    _hud.InitInGameHUD();
                }
                else
                {
                    _insim.DeleteAllButtons();
                    _hud.InvalidateCache();
                    _drift.DeactivateLapCounting();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(false);
                }
            });

            _insim.LapCompleted += (s, e) => PostToUi(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.OnLapCompleted();
                    _overlay.UpdateBestLapScore(_drift.BestLapScore);
                    if (_drift.LastLapScore >= LastLapPopupMinScore)
                    { _overlay.UpdateLastLapScore(_drift.LastLapScore); }

                    _overlay.ShowLapResult(_drift.LastLapScore);
                    _overlay.SetLapBoxVisible(true);
                }
            });

            _insim.TrackChanged += (s, track) => PostToUi(() =>
            {
                _drift.SetTrack(track);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            });

            _insim.LayoutChanged += (s, layout) => PostToUi(() =>
            {
                _drift.SetLayout(layout);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            });
            _insim.CarReset += (s, e) => PostToUi(() =>
            {
                if (e.PLID == _myOwnPlid)
                {
                    LoadVehicleRevSettings(_currentCarName);
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(false);
                }
            });

            _insim.PitLaneEntered += (s, e) => PostToUi(() =>
            {
                if (e.PLID == _myOwnPlid)
                {
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(false);
                }
            });

            _insim.PitLaneExited += (s, e) => PostToUi(() =>
            {
                bool isMe = e.PLID == _myOwnPlid;
                if (!isMe && _localPlayerParked && _localPlayerNickAtPark != null &&
                    _insim.GetPlayerName(e.PLID) == _localPlayerNickAtPark)
                {
                    isMe = true;
                }

                if (isMe)
                {
                    _lastKnownPlayerPLID = _myOwnPlid;
                    _localPlayerParked = false;
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(true);
                    LoadVehicleRevSettings(_currentCarName);
                }
            });

            _insim.RaceRestarted += (s, e) => PostToUi(() =>
            {
                _drift.OnRaceRestarted();
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.SetLapBoxVisible(false);
                _localPlayerParked = false;
                _localPlayerNickAtPark = null;
            });

            _insim.PlayerPitted += (s, e) => PostToUi(() =>
            {
                if (e.PLID == _myOwnPlid)
                    EnterParkedState();
            });

            _insim.CheckpointCrossed += (s, e) => PostToUi(() =>
            {
                if (e.PLID != _lastKnownPlayerPLID) return;
                if (e.CheckpointIndex != 1) return;

                if (_drift.ActivateLapCounting())
                {
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(true);
                    UpdateLapContextLabel();
                }

            });

            _insim.RestrictedAreaEntered += (s, e) => PostToUi(() =>
            {
                if (e.PLID != _lastKnownPlayerPLID) return;

                _drift.DeactivateLapCounting();
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.SetLapBoxVisible(false);
            });

            _insim.ObjectHit += (s, e) => PostToUi(() =>
            {
                if (!_objectCollisionDetectionEnabled) return;
                if (e.PLID != _lastKnownPlayerPLID) return;
                HandleObjectHit(e.ObjectName);
            });

            _insim.PlayerNamed += (s, e) => PostToUi(() => TryApplyInSimDriverName(e.PLID));
            _insim.CarContact += (s, e) => PostToUi(() =>
                _collisions.ProcessContact(e, _lastKnownPlayerPLID));

            _drift.SetDriver(Environment.UserName);
            _drift.Error += msg => AppendStatusMessage(msg, StatusErrorColor);
            _collisions.ContactDetected += (tier, otherPlid) => PostToUi(() => OnCollisionDetected(tier, otherPlid));
            _outGauge.DataReceived += OnRevData;
            _outGauge.Error += msg => AppendStatusMessage(msg, StatusErrorColor);

            _outSim.DataReceived += sample => _controllerAssistState.Ingest(sample);
            _outSim.DataReceived += sample =>
                _drift.SetUpsideDown(Math.Cos(sample.Pitch) * Math.Cos(sample.Roll) < UpsideDownUpZThreshold);
            _outSim.DataReceived += _ => PostToUi(UpdateOutSimStatusLabel);
            _outSim.Error += msg => PostToUi(() =>
            {
                AppendStatusMessage(msg, StatusErrorColor);
                _outSimStatusLabel.Text = msg;
                _outSimStatusLabel.ForeColor = StatusErrorColor;
            });
            _outSim.Warning += msg => PostToUi(() =>
            {
                _outSimStatusLabel.Text = msg;
                _outSimStatusLabel.ForeColor = ApplePalette.StatusWarn;
            });

            _revLimiter.CutStarted += () => PostToUi(() =>
            {
                _revCutLabel.Text = Localization.T("rev.cut");
                _overlay.SetRevCut(true);
            });
            _revLimiter.CutEnded += () => PostToUi(() =>
            {
                _revCutLabel.Text = "";
                _overlay.SetRevCut(false);
            });
            _revLimiter.Error += msg => AppendStatusMessage(msg, StatusErrorColor);
#if DEBUG
            _revLimiter.DebugTiming += msg => AppendStatusMessage(msg);
#endif

            try
            {
                _indicatorClickOn.LoadAsync();
                _indicatorClickOff.LoadAsync();
                _indicatorCancel.LoadAsync();

                string soundsDir = Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Sounds");
                var missingSoundFiles = new List<string>();
                foreach (var fileName in new[] { "indicator_click_on.wav", "indicator_click_off.wav", "indicator_cancel.wav" })
                {
                    if (!File.Exists(Path.Combine(soundsDir, fileName)))
                        missingSoundFiles.Add(fileName);
                }

                if (missingSoundFiles.Count > 0)
                    _missingIndicatorSoundsWarning = string.Format(Localization.T("status.missing_sounds"), string.Join(", ", missingSoundFiles));
            }
            catch { }

            _drift.DriftScored += OnDriftScored;
            _drift.DriftStarted += OnDriftStarted;
            _drift.DriftEnded += OnDriftEnded;

            _indicators.StateChanged += OnIndicatorStateChanged;

            _indicators.LampStateChanged += OnIndicatorLampStateChanged;

            SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

            _shadow = new WindowShadow(this);

            this.Load += (s, e) => _shadow.Reposition();
            this.Move += (s, e) => _shadow.Reposition();
            this.Resize += (s, e) => _shadow.Reposition();
            this.Activated += (s, e) => _shadow.Reposition();
            this.VisibleChanged += (s, e) => { if (Visible) _shadow.Reposition(); else _shadow.Hide(); };
            _overlay = new OverlayForm();
            _overlay.ComboTimeoutSec = DriftEngine.COMBO_TIMEOUT_SEC;
            Localization.LanguageChanged += ApplyLanguage;

            _outGaugeFreshnessPoll.Tick += OnOutGaugeFreshnessPollTick;
            _outGaugeFreshnessPoll.Start();

            BuildUI();

            _hud = new InGameHudManager(
                _insim, _drift, _revLimiter,
                _showHudCheck, _showRPMHudCheck,
                _revLimiterNumeric, _angleValueLabel);

            LoadSettings();
#if DEBUG
            foreach (var problem in Localization.Validate())
                AppendStatusMessage("DEBUG LOC " + problem);
#endif
            SyncControllerAssistAvailability();
            RefreshDriverStatusLabels();

            _controllerAssistDisplayTimer.Tick += OnControllerAssistDisplayTick;
            _controllerAssistDisplayTimer.Start();

            UpdateBestStatsLabels();

            if (!string.IsNullOrEmpty(_missingIndicatorSoundsWarning))
                AppendStatusMessage(_missingIndicatorSoundsWarning);

            UpdateIndicatorDiagnosticsLabel();

            _inGameSettingsOverlay = new InGameSettingsOverlay();
            BuildInGameSettingsOverlayContent(_inGameSettingsOverlay.ContentHost);

            WarmUpUI();
        }

        private void WarmUpUI()
        {
            if (_mainScrollPanel == null) return;

            int originalY = _mainScrollPanel.ScrollY;

            _mainScrollPanel.ScrollTo(_mainScrollPanel.MaxScrollY);
            _mainScrollPanel.PerformLayout();
            _mainScrollPanel.Invalidate(true);
            _mainScrollPanel.Update();

            _mainScrollPanel.ScrollTo(originalY);
            _mainScrollPanel.PerformLayout();
            _mainScrollPanel.Invalidate(true);
            _mainScrollPanel.Update();

            _mainScrollbar?.SyncToTarget();
            ForceRepaintRecursive(this);
        }

        private static void ForceRepaintRecursive(Control control)
        {
            if (!control.IsHandleCreated) control.CreateControl();

            foreach (Control child in control.Controls)
                ForceRepaintRecursive(child);

            control.Invalidate(true);
            control.Update();
        }

        private SteeringWheelInput _wheelInput = null!;
        private GlobalHotkey _globalHotkey = null!;
        private RevLimiter _revLimiter = new RevLimiter();

        private void ApplyLanguage()
        {
            foreach (var (ctrl, key) in _localizedControls)
            {
                ctrl.Text = Localization.T(key);
            }

            _connectionStateLabel.Text = _insim.IsConnected
                ? Localization.T("status.connected")
                : Localization.T("status.disconnected");
            if (_outGaugeStateLabel != null)
                _outGaugeStateLabel.Text = _outGaugeFreshness.IsFresh
                    ? Localization.T("status.outgauge.connected")
                    : Localization.T("status.outgauge.disconnected");
            _langBtn.Text = Localization.LanguageDisplayName(Localization.CurrentLanguage);
            if (_ovLangBtn != null)
                _ovLangBtn.Text = Localization.LanguageDisplayName(Localization.CurrentLanguage);
            UpdateLfsFolderStatusLabel();
            UpdateOutSimStatusLabel();
            UpdateOutGaugeStatusLabel();
            RefreshIndicatorBindButtonLabels();
            _lastInputControllerTilesSignature = null;
            SyncControllerAssistAvailability();

            if (_driverInfoLabel != null)
                _driverInfoLabel.Text = string.Format(Localization.T("score.driver"), _drift.CurrentDriver);
            if (_vehicleInfoLabel != null && !string.IsNullOrEmpty(_currentCarName))
                _vehicleInfoLabel.Text = string.Format(Localization.T("rev.vehicle"), _currentCarName);

            UpdateSpeedUnitLabels();

            RefreshHudPreview();
        }

        private void UpdateSpeedUnitLabels()
        {
            RefreshSpeedoPreview();
        }

        private void RefreshSpeedoPreview()
        {
            if (_speedoTachoPreview == null) return;
            _speedoTachoPreview.RedlineColor = _speedoTachoRedlineColor;
            _speedoTachoPreview.RedlineThickness = _redlineThickness;
            _speedoTachoPreview.TextColor = _speedoTachoTextColor;
            _speedoTachoPreview.IndicatorColor = _speedoTachoIndicatorColor;
            _speedoTachoPreview.TickColor = _speedoTachoTickColor;
            _speedoTachoPreview.BackgroundColor = _speedoTachoBackgroundColor;
            _speedoTachoPreview.UseMph = _useMph;
            _speedoTachoPreview.Invalidate();
        }

        private void RefreshHudPreview()
        {
            if (_hudPreview == null) return;
            _hudPreview.IdleColor = OverlayColorIdle;
            _hudPreview.Color1 = OverlayColor1;
            _hudPreview.Color2 = OverlayColor2;
            _hudPreview.Color3 = OverlayColor3;
            _hudPreview.Color4 = OverlayColor4;
            _hudPreview.Color5 = OverlayColor5;
            _hudPreview.Color6 = OverlayColor6;
            _hudPreview.Invalidate();
        }

        private static Form CreatePopupForm() => new Form { StartPosition = FormStartPosition.CenterParent };

        private Region CreateSmoothRoundedRegion(int width, int height, int radius)
        {
            const int scale = 4;

            int sw = width * scale;
            int sh = height * scale;
            int sr = radius * scale;

            using var mask = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(mask))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var path = DrawingHelpers.RoundedPath(new Rectangle(0, 0, sw - 1, sh - 1), sr);
                using var brush = new SolidBrush(Color.Black);
                g.FillPath(brush, path);
            }

            var region = new Region(Rectangle.Empty);

            var bits = mask.LockBits(new Rectangle(0, 0, sw, sh),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int stride = bits.Stride;
                byte[] rowBuffer = new byte[stride];

                for (int y = 0; y < height; y++)
                {
                    int sampleY = Math.Min(y * scale + scale / 2, sh - 1);

                    IntPtr rowPtr = bits.Scan0 + sampleY * stride;
                    Marshal.Copy(rowPtr, rowBuffer, 0, stride);

                    int runStart = -1;
                    for (int x = 0; x <= width; x++)
                    {
                        bool filled = false;
                        if (x < width)
                        {
                            int sampleX = Math.Min(x * scale + scale / 2, sw - 1);
                            byte alpha = rowBuffer[sampleX * 4 + 3];
                            filled = alpha >= 128;
                        }

                        if (filled && runStart == -1)
                            runStart = x;
                        else if (!filled && runStart != -1)
                        {
                            region.Union(new Rectangle(runStart, y, x - runStart, 1));
                            runStart = -1;
                        }
                    }
                }
            }
            finally
            {
                mask.UnlockBits(bits);
            }

            return region;
        }

        private Form CreateDimOverlay(Form? ownerForm = null)
        {
            ownerForm ??= this;
            return new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = ownerForm.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = ownerForm,
                TopMost = ownerForm.TopMost
            };
        }

        private RoundedPanel BuildPopupChrome(Form popup, int width, int height, out Button closeButton)
        {
            popup.FormBorderStyle = FormBorderStyle.None;
            popup.ShowInTaskbar = false;
            popup.Size = new Size(width, height);
            popup.BackColor = ApplePalette.Background;

            popup.Shown += (s, e) => popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 20);

            var card = new RoundedPanel { Dock = DockStyle.Fill };
            popup.Controls.Add(card);

            closeButton = new Button
            {
                Text = "×",
                Size = new Size(28, 28),
                Location = new Point(width - 38, 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ApplePalette.Secondary,
                Font = UiFonts.Get("Segoe UI Semibold", 12f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 240);
            closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 220, 225);
            closeButton.Region = CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);
            closeButton.Click += (s, e) => { popup.DialogResult = DialogResult.Cancel; popup.Close(); };
            card.Controls.Add(closeButton);

            popup.Paint += (s, e) => DrawingHelpers.PaintSoftShadow(e.Graphics, popup.Width, popup.Height);

            return card;
        }

        private DialogResult ShowModalPopup(Form popup, Form? ownerForm = null)
        {
            Form overlayBg = CreateDimOverlay(ownerForm);
            overlayBg.Show();
            popup.Owner = overlayBg;
            popup.TopMost = overlayBg.TopMost;
            try { return popup.ShowDialog(overlayBg); }
            finally { overlayBg.Close(); overlayBg.Dispose(); }
        }

        private void LoadVehicleRevSettings(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName)) return;

            if (_vehicleInfoLabel != null)
                _vehicleInfoLabel.Text = string.Format(Localization.T("rev.vehicle"), carName);

            bool known = _vehicleRevSettings.TryGetValue(carName, out var vs);
            _revCarUnsupported = known && vs!.Unsupported;

            if (known)
            {
                ApplyVehicleRevValues(vs!.MaxRpm, vs.CutMs,
                    string.Format(Localization.T("rev.loaded_for_car"), carName, vs.MaxRpm, vs.CutMs));
                ApplyRevUnsupportedState();
                return;
            }

            ApplyVehicleRevValues(DefaultVehicleMaxRpm, DefaultVehicleCutMs,
                string.Format(Localization.T("rev.no_saved_for_car"), carName, DefaultVehicleMaxRpm, DefaultVehicleCutMs));
            ApplyRevUnsupportedState();

            ShowRevLimiterCalibrationPromptIfNeeded(carName);
        }

        private void ApplyVehicleRevValues(int maxRpm, int cutMs, string statusMessage)
        {
            maxRpm = SanitizeRevRpm(maxRpm);
            cutMs = SanitizeRevCutMs(cutMs);
            CalibratedMAXRPM = maxRpm;
            SavedMSCUT = cutMs;

            _revLimiterNumeric.Value = Math.Clamp((decimal)maxRpm, _revLimiterNumeric.Minimum, _revLimiterNumeric.Maximum);
            _revCutMS.Value = Math.Clamp((decimal)cutMs, _revCutMS.Minimum, _revCutMS.Maximum);

            _revLimiter.RpmLimit = maxRpm;
            _revLimiter.CutMs = cutMs;

            if (!string.IsNullOrEmpty(statusMessage))
                AppendStatusMessage(statusMessage);
        }

        private bool IsEnterBoundElsewhere() =>
            GetBindingTable().Any(entry =>
            {
                var binding = entry.Get();
                return binding.Kind == InputKind.Keyboard && binding.Key == Keys.Enter;
            });
        private void ShowRevLimiterCalibrationPromptIfNeeded(string carName)
        {
            if (_revLimiterPromptShownForCars.Contains(carName)) return;
            _revLimiterPromptShownForCars.Add(carName);

            if (_revAutoCalibrateNewCarCheck != null && !_revAutoCalibrateNewCarCheck.Checked) return;
            ShowRevLimiterCalibrationPrompt(carName, firstTimeForCar: true);
        }

        private void ShowRevLimiterCalibrationPrompt(string carName, bool firstTimeForCar)
        {
            _revLimiterCalibrationPrompt?.Close();

            bool enterAvailable = !IsEnterBoundElsewhere();

            _revLimiterCalibrationPrompt = new RevLimiterCalibrationPromptForm(
                this, carName, () => RPMLimitterCalibrate(), enterAvailable, firstTimeForCar);

            if (enterAvailable)
            {
                _globalHotkey.SetBinding(Keys.Enter, () => PostToUi(() =>
                {
                    if (_revLimiterCalibrationPrompt != null && !calibrationON)
                        _ = RPMLimitterCalibrate();
                }));
            }

            _revLimiterCalibrationPrompt.FormClosed += (s, e) =>
            {
                if (enterAvailable) _globalHotkey.RemoveBinding(Keys.Enter);
                _revLimiterCalibrationPrompt = null;
            };
            _revLimiterCalibrationPrompt.Show();
        }

        private void SaveVehicleRevSettings(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName)) return;

            _vehicleRevSettings[carName] = new VehicleRevSettings
            {
                MaxRpm = (int)_revLimiterNumeric.Value,
                CutMs = (int)_revCutMS.Value,
                Unsupported = _revCarUnsupported
            };

            SaveSettings();
        }

        private const int RpmUiIntervalMs = 66;
        private DateTime _lastRpmUiUpdateUtc = DateTime.MinValue;

        private const int AssistDisplayActiveIntervalMs = 33;
        private const int AssistDisplayIdleIntervalMs = 250;
        private const int AssistLabelEveryNthTick = 3;
        private int _assistLabelTick;

        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
        private readonly Queue<Action> _pendingUiActions = new();

        private bool IsUiThread => Environment.CurrentManagedThreadId == _uiThreadId;

        private void PostToUi(Action action)
        {
            if (IsDisposed) return;

            if (!IsHandleCreated)
            {
                lock (_pendingUiActions)
                    _pendingUiActions.Enqueue(action);

                if (IsHandleCreated)
                    FlushPendingUiActions();
                return;
            }

            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void FlushPendingUiActions()
        {
            while (true)
            {
                Action? next;
                lock (_pendingUiActions)
                {
                    if (_pendingUiActions.Count == 0) return;
                    next = _pendingUiActions.Dequeue();
                }

                if (IsDisposed) continue;

                try { BeginInvoke(next); }
                catch (InvalidOperationException) { }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FlushPendingUiActions();
        }

        private void OnRevData(OutGaugeData data)
        {

            _revLimiter.ProcessOutGaugeData(data);

            PostToUi(() =>
            {

                _sawAnyOutGaugeSinceConnect = true;

                if (_localPlayerParked)
                {
                    if (data.PLID != 0 && _localPlayerNickAtPark != null &&
                        _insim.GetPlayerName(data.PLID) == _localPlayerNickAtPark)
                    {
                        _lastKnownPlayerPLID = data.PLID;
                        _localPlayerParked = false;
                        _sawOwnOutGaugeSinceConnect = true;
                        AppendStatusMessage($"DEBUG OnRevData UNPARKED via OutGauge: new PLID={data.PLID}");
                    }
                    else
                    {
                        return;
                    }
                }

                _overlay.NotifyOutGaugeData();
                _overlay.UpdateSpeedGaugeDash(data.Speed * 3.6);
                _outGaugeFreshness.Ping();

                if (data.PLID != 0 && data.PLID != _lastKnownPlayerPLID)
                {
                    if (_lastKnownPlayerPLID != 0)
                        OnTrackedPlayerChanged();

                    _lastKnownPlayerPLID = data.PLID;
                    TryApplyInSimDriverName(data.PLID);
                    if (_insim.GetPlayerName(data.PLID) == null)
                        _insim.RequestPlayerList();
                }

                if (data.PLID != 0 && data.PLID == _myOwnPlid)
                    _sawOwnOutGaugeSinceConnect = true;

                _drift.SetHandbrakeActive(data.HandbrakeOn);
                _overlay.UpdateRpm((int)data.RPM);
                _overlay.UpdateGear((int)data.Gear);

                DateTime rpmUiNow = DateTime.UtcNow;
                if (WindowState != FormWindowState.Minimized &&
                    (rpmUiNow - _lastRpmUiUpdateUtc).TotalMilliseconds >= RpmUiIntervalMs)
                {
                    _lastRpmUiUpdateUtc = rpmUiNow;

                    _rpmLabel.Text = ((int)data.RPM).ToString("N0");
                    _rpmBar.Value = (int)Math.Min(data.RPM, _rpmBar.Maximum);

                    _rpmLabel.ForeColor = data.RPM >= _revLimiter.RpmLimit * 0.95
                        ? Color.FromArgb(255, 60, 60)
                        : Color.FromArgb(80, 220, 120);
                }

                if (calibrationON == true)
                {
                    if (data.RPM > CalibratedMAXRPM) { CalibratedMAXRPM = (int)data.RPM; } else { }
                    _revLimiterCalibrationPrompt?.UpdateLiveRpm(CalibratedMAXRPM);
                }

                if (!string.IsNullOrEmpty(data.Car) && data.Car != _currentCarName)
                {
                    if (!string.IsNullOrEmpty(_currentCarName))
                        SaveVehicleRevSettings(_currentCarName);

                    _currentCarName = data.Car;
                    LoadVehicleRevSettings(_currentCarName);

                    _insim.RequestState();
                    _insim.RequestPlayerList();
                }

                if (data.RPM > UnsupportedRevRpmThreshold && !_revCarUnsupported && !string.IsNullOrEmpty(_currentCarName))
                    SetCurrentCarRevUnsupported(true, detectedAutomatically: true);

                _hud.ShowInGameRPMLimitter((int)_revLimiterNumeric.Value);

                _drift.UpdateEngineTelemetry(data.RPM, data.Throttle, (int)data.Gear, data.Speed * 3.6);

                _indicators.UpdateFromOutGauge(data.LeftSignalOn, data.RightSignalOn, data.AnySignalOn);

                if (_dashLeftLamp != null)
                {
                    _dashLeftLamp.Lit = _indicators.LeftLampOn;
                    _dashRightLamp.Lit = _indicators.RightLampOn;
                    _dashHighBeamLamp.Lit = data.FullBeamOn;
                }

                _outGaugePacketCount++;
                _lastShowLightsRaw = data.ShowLights;
                if ((DateTime.UtcNow - _lastIndicatorDiagUpdate).TotalMilliseconds >= 200)
                {
                    _lastIndicatorDiagUpdate = DateTime.UtcNow;
                    UpdateIndicatorDiagnosticsLabel();
                }

            });
        }

        private bool _isDarkTheme = true;

        private readonly List<Label> _cardBackedLabels = new();

        private void ApplyTheme(bool dark)
        {
            _isDarkTheme = dark;
            ApplePalette.SetDark(dark);

            this.BackColor = ApplePalette.Background;
            if (_mainScrollPanel != null)
                _mainScrollPanel.BackColor = ApplePalette.Background;
            _mainScrollbar?.ApplyThemeColor(ApplePalette.Background);

            ApplyThemeRecursive(this);

            foreach (Label cardLabel in _cardBackedLabels)
            {
                if (cardLabel.Parent is RoundedPanel cardParent)
                    cardLabel.BackColor = cardParent.BackColor;
            }

            if (_titleBar != null)
            {
                _titleBar.BackColor = ApplePalette.Card;
                _titleBarLabel.ForeColor = ApplePalette.Title;
                _titleBar.Invalidate();
                _btnMinimize.Invalidate();
                _btnClose.Invalidate();
            }

            this.Invalidate(true);

            _mainScrollPanel?.Refresh();
        }

        private void ApplyThemeRecursive(Control root)
        {
            foreach (Control c in root.Controls)
            {
                switch (c)
                {
                    case RoundedPanel rp:
                        rp.BackColor = ApplePalette.Card;
                        rp.Invalidate();
                        break;

                    case MacCheckBox mcb:
                        mcb.Invalidate();
                        break;

                    case TextBox tb:
                        tb.BackColor = ApplePalette.Card;
                        tb.ForeColor = ApplePalette.Text;
                        break;

                    case RichTextBox rtb:
                        rtb.BackColor = ApplePalette.Card;
                        rtb.ForeColor = ApplePalette.Text;
                        break;

                    case Panel p when p.Tag as string == "theme:border":
                        p.BackColor = ApplePalette.Border;
                        break;

                    case Panel p when p.Tag as string == "theme:card":
                        p.BackColor = ApplePalette.Card;
                        break;

                    case Label lbl when lbl.Tag as string == "status:live":
                        break;

                    case Label lbl when lbl.Tag as string == "theme:title":
                        lbl.ForeColor = ApplePalette.Title;
                        break;

                    case Label lbl:
                        RecolorLabel(lbl);
                        break;
                }

                if (c.HasChildren)
                    ApplyThemeRecursive(c);
            }
        }

        private static readonly Color DarkThemeTextSnapshot = Color.FromArgb(220, 220, 225);
        private static readonly Color DarkThemeSecondarySnapshot = Color.FromArgb(150, 150, 160);

        private void RecolorLabel(Label lbl)
        {

            if (lbl.Tag == null)
            {
                Color current = lbl.ForeColor;
                if (current == DarkThemeTextSnapshot)
                    lbl.Tag = "theme:dyntext";
                else if (current == DarkThemeSecondarySnapshot)
                    lbl.Tag = "theme:dynsecondary";
                else
                    lbl.Tag = current;
            }

            if (lbl.Tag as string == "theme:dyntext")
            {
                lbl.ForeColor = ApplePalette.Text;
                return;
            }
            if (lbl.Tag as string == "theme:dynsecondary")
            {
                lbl.ForeColor = ApplePalette.Secondary;
                return;
            }

            if (!(lbl.Tag is Color original))
                return;

            double luma = (0.299 * original.R + 0.587 * original.G + 0.114 * original.B) / 255.0;

            if (!_isDarkTheme)
            {
                lbl.ForeColor = luma >= 0.5 ? DrawingHelpers.Darken(original, 0.35) : original;
                return;
            }

            lbl.ForeColor = luma < 0.55 ? DrawingHelpers.Lighten(original, 0.65) : original;
        }

        public bool indClickONOFF = false;

        private Color ButtonColor => _isDarkTheme
    ? Color.FromArgb(70, 70, 90)
    : Color.FromArgb(205, 205, 220);
        protected override void OnPaintBackground(PaintEventArgs e)
        {

        }
        private void MainForm_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bgBrush = new SolidBrush(this.BackColor))
                g.FillRectangle(bgBrush, this.ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, 2))
            using (Pen border = new Pen(ApplePalette.Border, 1.6f))
            {
                g.DrawPath(border, path);
            }
        }
        private void OpenLanguagePicker(Button anchorBtn, Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 240, 320, out _);

            MakeLabel(
                card,
                Localization.T("header.language"),
                20,
                15,
                150,
                28,
                ApplePalette.Title,
                UiFonts.Get("Segoe UI Semibold", 11f));

            var languages = new[]
            {
                AppLanguage.English,
                AppLanguage.Polish,
                AppLanguage.Turkish,
                AppLanguage.German,
                AppLanguage.Spanish
            };

            int y = 50;

            foreach (var lang in languages)
            {
                var btn = MakeButton(
                    card,
                    Localization.LanguageDisplayName(lang),
                    20,
                    y,
                    200,
                    45,
                    ApplePalette.Blue);

                btn.Click += (s, e) =>
                {
                    Localization.SetLanguage(lang);
                    SaveSettings();
                    popup.DialogResult = DialogResult.OK;
                    popup.Close();
                };

                y += 52;
            }

            ShowModalPopup(popup, ownerForm);
        }

        private InputBinding _revToggleBinding = InputBinding.None;
        private InputBinding _revCalibrateBinding = InputBinding.None;
        private InputBinding _revDecreaseBinding = InputBinding.None;
        private InputBinding _revIncreaseBinding = InputBinding.None;

        private InputBinding _lightToggleBinding = InputBinding.None;

        private void ExecuteRevToggle()
        {
            if (!_insim.IsConnected) return;
            PostToUi(() => { if (_revEnableSwitch.Enabled) _revEnableSwitch.Checked = !_revEnableSwitch.Checked; });
        }

        private void ExecuteRevCalibrate()
        {
            if (!_insim.IsConnected) return;
            PostToUi(() =>
            {
                if (!calibrationON)
                    _ = RPMLimitterCalibrate();
            });
        }

        private void ExecuteRevDecrease()
        {
            if (!_insim.IsConnected) return;
            PostToUi(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value - 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            });
        }

        private void ExecuteRevIncrease()
        {
            if (!_insim.IsConnected) return;
            PostToUi(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value + 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            });
        }

        private InputBinding GetIndicatorBinding(int keyCode, int? wheelButton)
        {
            if (wheelButton.HasValue)
                return InputBinding.FromWheelButton(wheelButton.Value);

            if (keyCode == (int)Keys.None)
                return InputBinding.None;

            return InputBinding.FromKey((Keys)keyCode);
        }

        private void RefreshIndicatorBindButtonLabels()
        {
            if (_bindLeftBtn == null) return;

            string Display(InputBinding b) => b.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : b.ToString();

            _bindLeftBtn.Text = Display(GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton));
            _bindRightBtn.Text = Display(GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton));
            _bindHazardBtn.Text = Display(GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton));
            _bindLightBtn.Text = Display(_lightToggleBinding);
            RefreshControllerAssistBindLabel();
        }

        private void RefreshControllerAssistBindLabel()
        {
            if (_caBindBtn == null) return;
            _caBindBtn.Text = _caToggleBinding.Kind == InputKind.None
                ? Localization.T("controllerassist.bindbtn")
                : _caToggleBinding.ToString();
        }

        private void ExecuteControllerAssistToggle()
        {
            PostToUi(() =>
            {
                if (!_caMasterSwitch.Enabled) return;

                if (!_caMasterSwitch.Checked)
                {
                    _caMasterSwitch.Checked = true;
                    return;
                }

                if (!_caSteeringAssistSwitch.Enabled) return;

                _caSteeringAssistSwitch.Checked = !_caSteeringAssistSwitch.Checked;
                _overlay.ShowControllerAssistNotification(
                    Localization.T(_caSteeringAssistSwitch.Checked ? "controllerassist.notification.on" : "controllerassist.notification.off"));
            });
        }

        private void ApplyIndicatorBinding(
            InputBinding newBinding,
            Action<int> setKeyCode,
            Action<int?> setWheelButton,
            int? previousWheelButton,
            Action wheelAction)
        {
            if (previousWheelButton.HasValue)
                _wheelInput.RemoveBinding(previousWheelButton.Value);

            if (newBinding.Kind == InputKind.Keyboard)
            {
                setKeyCode((int)newBinding.Key);
                setWheelButton(null);
            }
            else if (newBinding.Kind == InputKind.WheelButton)
            {
                setWheelButton(newBinding.WheelButton);

                _wheelInput.SetBinding(newBinding.WheelButton, () =>
                {
                    if (_insim.IsConnected) wheelAction();
                });
            }
            else
            {
                setKeyCode((int)Keys.None);
                setWheelButton(null);
            }
        }

        private void ApplyRevBinding(ref InputBinding currentBinding, InputBinding newBinding, Action action)
        {
            if (currentBinding.Kind == InputKind.Keyboard)
                _globalHotkey.RemoveBinding(currentBinding.Key);
            else if (currentBinding.Kind == InputKind.WheelButton)
                _wheelInput.RemoveBinding(currentBinding.WheelButton);

            currentBinding = newBinding;

            if (newBinding.Kind == InputKind.Keyboard)
                _globalHotkey.SetBinding(newBinding.Key, action);
            else if (newBinding.Kind == InputKind.WheelButton)
                _wheelInput.SetBinding(newBinding.WheelButton, action);
        }

        private async Task RPMLimitterCalibrate()
        {
            if (_revCarUnsupported) return;

            if (!_outGauge.IsRunning)
            {
                AppendStatusMessage(Localization.T("rev.calibrate.needs_outgauge"));
                return;
            }

            if (_revLimiterCalibrationPrompt == null && !string.IsNullOrEmpty(_currentCarName))
                ShowRevLimiterCalibrationPrompt(_currentCarName, firstTimeForCar: false);

            CalibratedMAXRPM = 1200;

            calibrationON = true;

            bool wasEnabledBeforeCalibration = _revLimiter.Enabled;
            _revLimiter.Enabled = false;
            _revEnableSwitch.SetCheckedSilent(false);
            SyncRevAutoCalibrateAvailability();

            _hud.ShowInGameAward(Localization.T("rev.calibration_award.start"));
            _revLimiterCalibrationPrompt?.ShowCalibratingState();

            _revLimitLabel.Text = Localization.T("rev.calibrating");

            await Task.Delay(RevCalibrationMeasureMs);

            calibrationON = false;

            CalibratedMAXRPM = CalibratedMAXRPM - 50;
            _revLimitLabel.Text = Localization.T("rev.limit");
            _revLimiterNumeric.Value = CalibratedMAXRPM;

            _revLimiter.Enabled = wasEnabledBeforeCalibration;
            _revEnableSwitch.SetCheckedSilent(wasEnabledBeforeCalibration);
            SyncRevAutoCalibrateAvailability();
            ApplyRevUnsupportedState();
            _hud.ShowInGameAward(string.Format(Localization.T("rev.limit.value"), CalibratedMAXRPM));
            _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            _revLimiterCalibrationPrompt?.ShowDoneState(CalibratedMAXRPM);
            await Task.Delay(RevCalibrationStepMs);

            _hud.ShowInGameAward(Localization.T("rev.calibration_award.done"));
            await Task.Delay(RevCalibrationStepMs);
            _hud.ShowInGameAward(string.Format(Localization.T("rev.limit.value"), CalibratedMAXRPM));
            await Task.Delay(RevCalibrationStepMs);
            _hud.ShowInGameAward(Localization.T("rev.calibration_award.done"));

            await Task.Delay(RevCalibrationFinalHoldMs);
            _hud.ShowInGameAward($"");
            _revLimitLabel.Text = Localization.T("rev.limit");
            SaveSettings();

            _revLimiterCalibrationPrompt?.Close();
            _revLimiterCalibrationPrompt = null;
        }

        private enum GridSlot { Header, ConnectionInSim, ConnectionOutGauge, ConnectionOutSim, Score, RevLimiter, Speedometer, Hud, Indicators, ControllerAssist, Status, InputController }

        private const int GridMargin = 20;
        private const int GridGap = 10;

        private readonly Dictionary<GridSlot, Point> _gridPositions = new();

        private int _gridContentHeight;

        private const int GridColumnWidth = 420;

        private void ComputeGridLayout()
        {
            var slots = new (GridSlot slot, int col, int width, int height)[]
            {
                (GridSlot.Header,             0, 420, 170),
                (GridSlot.ConnectionInSim,    0, 420, 295),
                (GridSlot.Score,              0, 420, 586),
                (GridSlot.Speedometer,        0, 420, 450),

                (GridSlot.InputController,    1, 420, 170),
                (GridSlot.ConnectionOutGauge, 1, 420, 115),
                (GridSlot.ConnectionOutSim,   1, 420, 170),
                (GridSlot.RevLimiter,         1, 420, 330),
                (GridSlot.Hud,                1, 420, 450),
                (GridSlot.Status,             1, 420, 245),

                (GridSlot.Indicators,        -1, 850, 460),
                (GridSlot.ControllerAssist,  -1, 850, 540),

            };

            int startY = GridMargin + TitleBarHeight;
            var columnY = new Dictionary<int, int> { [0] = startY, [1] = startY };
            var columnX = new Dictionary<int, int> { [0] = GridMargin, [1] = GridMargin + GridColumnWidth + GridGap };

            int contentBottom = startY;

            foreach (var s in slots)
            {
                int x = s.col == -1 ? GridMargin : columnX[s.col];
                int y = s.col == -1 ? Math.Max(columnY[0], columnY[1]) : columnY[s.col];

                _gridPositions[s.slot] = new Point(x, y);

                int bottom = y + s.height;
                contentBottom = Math.Max(contentBottom, bottom);

                if (s.col == -1)
                    columnY[0] = columnY[1] = bottom + GridGap;
                else
                    columnY[s.col] = bottom + GridGap;
            }

            _gridContentHeight = contentBottom + GridMargin;
        }

        private RoundedPanel CreateCard(
            string title,
            GridSlot slot,
            int width,
            int height,
            string? locKey = null)
        {
            RoundedPanel panel = new RoundedPanel();

            panel.Location = _gridPositions[slot];
            panel.Size = new Size(width, height);

            _mainScrollPanel.Controls.Add(panel);

            if (!string.IsNullOrWhiteSpace(title) || locKey != null)
            {
                Label lbl = new Label();

                lbl.Text = locKey != null ? Localization.T(locKey) : title;
                if (locKey != null) _localizedControls.Add((lbl, locKey));

                lbl.Location = new Point(16, 16);

                lbl.AutoSize = true;

                lbl.Font = new Font(
                    "Segoe UI Semibold",
                    12f);

                lbl.ForeColor = ApplePalette.Title;
                lbl.Tag = "theme:title";

                panel.Controls.Add(lbl);

                Panel separator = new Panel();

                separator.Location = new Point(20, 50);

                separator.Size = new Size(width - 40, 1);

                separator.BackColor = ApplePalette.Border;
                separator.Tag = "theme:border";

                panel.Controls.Add(separator);
            }

            return panel;
        }

        private Guid _savedWheelGuid = Guid.Empty;
        private JoystickOffset _savedWheelAxis = JoystickOffset.X;
        private void ShowWheelSetupDialog(List<(Guid Guid, string Name)> devices)
        {
            using var dlg = new WheelSetupForm(this, _wheelInput, devices);
            if (ShowModalPopup(dlg) == DialogResult.OK)
            {
                _savedWheelGuid = dlg.SelectedDeviceGuid;
                _savedWheelAxis = dlg.SelectedAxis;
                SyncControllerAssistAvailability();
                SaveSettings();
                AppendStatusMessage(string.Format(Localization.T("wheelconfig.configured"), _wheelInput.DeviceName, _wheelInput.SteeringAxis));
            }
        }

        private static bool SameBinding(InputBinding a, InputBinding b)
        {
            if (a.Kind == InputKind.None || a.Kind != b.Kind) return false;
            return a.Kind == InputKind.Keyboard ? a.Key == b.Key : a.WheelButton == b.WheelButton;
        }

        private List<(string NameKey, Func<InputBinding> Get, Action Clear)> GetBindingTable() => new()
        {
            ("rev.bindings.toggle", () => _revToggleBinding,
                () => ApplyRevBinding(ref _revToggleBinding, InputBinding.None, ExecuteRevToggle)),
            ("rev.bindings.calibrate", () => _revCalibrateBinding,
                () => ApplyRevBinding(ref _revCalibrateBinding, InputBinding.None, ExecuteRevCalibrate)),
            ("rev.bindings.decrease", () => _revDecreaseBinding,
                () => ApplyRevBinding(ref _revDecreaseBinding, InputBinding.None, ExecuteRevDecrease)),
            ("rev.bindings.increase", () => _revIncreaseBinding,
                () => ApplyRevBinding(ref _revIncreaseBinding, InputBinding.None, ExecuteRevIncrease)),
            ("indicators.bindname.lights", () => _lightToggleBinding,
                () => ApplyRevBinding(ref _lightToggleBinding, InputBinding.None, ExecuteLightToggle)),
            ("controllerassist.bindname", () => _caToggleBinding,
                () => ApplyRevBinding(ref _caToggleBinding, InputBinding.None, ExecuteControllerAssistToggle)),
            ("indicators.bindname.left",
                () => GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton),
                () => ApplyIndicatorBinding(InputBinding.None, code => _indicators.LeftKeyCode = code,
                    wb => _indicators.LeftWheelButton = wb, _indicators.LeftWheelButton, () => _indicators.ToggleLeft())),
            ("indicators.bindname.right",
                () => GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton),
                () => ApplyIndicatorBinding(InputBinding.None, code => _indicators.RightKeyCode = code,
                    wb => _indicators.RightWheelButton = wb, _indicators.RightWheelButton, () => _indicators.ToggleRight())),
            ("indicators.bindname.hazard",
                () => GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton),
                () => ApplyIndicatorBinding(InputBinding.None, code => _indicators.HazardKeyCode = code,
                    wb => _indicators.HazardWheelButton = wb, _indicators.HazardWheelButton, () => _indicators.ToggleHazard())),
        };

        private Action? _onBindingsChanged;

        private void ResolveBindingConflicts(string ownNameKey, InputBinding newBinding)
        {
            bool changed = false;

            foreach (var entry in GetBindingTable())
            {
                if (entry.NameKey == ownNameKey) continue;
                if (!SameBinding(entry.Get(), newBinding)) continue;

                entry.Clear();
                changed = true;
                AppendStatusMessage(string.Format(
                    Localization.T("status.binding.moved"),
                    newBinding.ToString(),
                    Localization.T(entry.NameKey)));
            }

            if (!changed) return;

            RefreshIndicatorBindButtonLabels();
            _onBindingsChanged?.Invoke();
            SaveSettings();
        }

        private void BindKey(string nameKey, Action<InputBinding> onBound)
        {
            string keyName = Localization.T(nameKey);

            using var dialog = new KeyBindingForm(this, keyName, _wheelInput);
            if (ShowModalPopup(dialog) == DialogResult.OK)
            {
                InputBinding result = dialog.Result;

                bool isValidKeyboard = result.Kind == InputKind.Keyboard && result.Key != Keys.Escape;
                bool isValidWheel = result.Kind == InputKind.WheelButton;
                bool isUnbind = result.Kind == InputKind.None;

                if (isValidKeyboard || isValidWheel || isUnbind)
                {
                    if (!isUnbind)
                        ResolveBindingConflicts(nameKey, result);

                    onBound(result);
                    AppendStatusMessage(string.Format(
                        Localization.T("status.keybound"),
                        keyName,
                        isUnbind ? Localization.T("rev.bindings.unbound") : result.ToString()));
                }
            }
        }
        private static Image LoadDetachedImage(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var temp = Image.FromStream(fs);
            return new Bitmap(temp);
        }

        private void ApplySpeedoBackgroundToOverlay()
        {
            _overlay.SpeedoBackgroundImage = _speedoBackgroundImage;
            _overlay.SpeedoBackgroundPanX = _speedoBackgroundPanX;
            _overlay.SpeedoBackgroundPanY = _speedoBackgroundPanY;
            _overlay.SpeedoBackgroundZoom = _speedoBackgroundZoom;
            _overlay.SpeedoBackgroundOpacity = _speedoBackgroundOpacity;

            if (_speedoTachoPreview == null) return;
            _speedoTachoPreview.SpeedoBackgroundImage = _speedoBackgroundImage;
            _speedoTachoPreview.SpeedoBackgroundPanX = _speedoBackgroundPanX;
            _speedoTachoPreview.SpeedoBackgroundPanY = _speedoBackgroundPanY;
            _speedoTachoPreview.SpeedoBackgroundZoom = _speedoBackgroundZoom;
            _speedoTachoPreview.SpeedoBackgroundOpacity = _speedoBackgroundOpacity;
            _speedoTachoPreview.Invalidate();
        }

        private SpeedoPresetData CaptureSpeedoPreset() => new SpeedoPresetData
        {
            ShowRpmDigits = _overlay.SpeedoTachoShowRpmDigits,
            ShowUnit = _overlay.SpeedoTachoShowUnit,
            ShowMinorTicks = _overlay.SpeedoTachoShowMinorTicks,
            ShowMajorTicks = _overlay.SpeedoTachoShowMajorTicks,
            PlainDigits = _overlay.SpeedoTachoPlainDigits,
            InstantSpeed = _overlay.SpeedoTachoInstantSpeed,
            AirSpeed = _overlay.SpeedoTachoAirSpeed,
            RedlineThickness = _redlineThickness,
            BackgroundImagePath = _speedoBackgroundImagePath,
            BackgroundPanX = _speedoBackgroundPanX,
            BackgroundPanY = _speedoBackgroundPanY,
            BackgroundZoom = _speedoBackgroundZoom,
            BackgroundOpacity = _speedoBackgroundOpacity,
            RedlineColorArgb = _speedoTachoRedlineColor.ToArgb(),
            TextColorArgb = _speedoTachoTextColor.ToArgb(),
            IndicatorColorArgb = _speedoTachoIndicatorColor.ToArgb(),
            TickColorArgb = _speedoTachoTickColor.ToArgb(),
            BackgroundColorArgb = _speedoTachoBackgroundColor.ToArgb(),
        };

        private void ApplySpeedoPreset(SpeedoPresetData preset)
        {
            _overlay.SpeedoTachoShowRpmDigits = preset.ShowRpmDigits;
            _overlay.SpeedoTachoShowUnit = preset.ShowUnit;
            _overlay.SpeedoTachoShowMinorTicks = preset.ShowMinorTicks;
            _overlay.SpeedoTachoShowMajorTicks = preset.ShowMajorTicks;
            _overlay.SpeedoTachoPlainDigits = preset.PlainDigits;
            _overlay.SpeedoTachoInstantSpeed = preset.InstantSpeed;
            _overlay.SpeedoTachoAirSpeed = preset.AirSpeed;
            if (_speedoTachoPreview != null)
            {
                _speedoTachoPreview.ShowRpmDigits = preset.ShowRpmDigits;
                _speedoTachoPreview.ShowUnit = preset.ShowUnit;
                _speedoTachoPreview.ShowMinorTicks = preset.ShowMinorTicks;
                _speedoTachoPreview.ShowMajorTicks = preset.ShowMajorTicks;
                _speedoTachoPreview.PlainDigits = preset.PlainDigits;
            }

            _redlineThickness = preset.RedlineThickness <= 0 ? 4f : preset.RedlineThickness;
            _overlay.RedlineThickness = _redlineThickness;
            if (_speedoTachoPreview != null) _speedoTachoPreview.RedlineThickness = _redlineThickness;
            _redlineThicknessSlider?.SetValueSilent((int)Math.Round(_redlineThickness * 10));

            _speedoTachoRedlineColor = Color.FromArgb(preset.RedlineColorArgb);
            _speedoTachoTextColor = Color.FromArgb(preset.TextColorArgb);
            _speedoTachoIndicatorColor = Color.FromArgb(preset.IndicatorColorArgb);
            _speedoTachoTickColor = Color.FromArgb(preset.TickColorArgb);
            _speedoTachoBackgroundColor = Color.FromArgb(preset.BackgroundColorArgb);
            _overlay.RedlineColor = _speedoTachoRedlineColor;
            _overlay.SpeedoTachoTextColor = _speedoTachoTextColor;
            _overlay.SpeedoTachoIndicatorColor = _speedoTachoIndicatorColor;
            _overlay.SpeedoTachoTickColor = _speedoTachoTickColor;
            _overlay.SpeedoTachoBackgroundColor = _speedoTachoBackgroundColor;
            RefreshSpeedoPreview();

            _overlay.SpeedoBackgroundImage = null;
            _speedoBackgroundImage?.Dispose();
            _speedoBackgroundImage = null;

            _speedoBackgroundImagePath = preset.BackgroundImagePath ?? "";
            _speedoBackgroundPanX = preset.BackgroundPanX;
            _speedoBackgroundPanY = preset.BackgroundPanY;
            _speedoBackgroundZoom = preset.BackgroundZoom <= 0 ? 1f : preset.BackgroundZoom;
            _speedoBackgroundOpacity = Math.Clamp(preset.BackgroundOpacity, 0f, 1f);

            if (!string.IsNullOrEmpty(_speedoBackgroundImagePath) && File.Exists(_speedoBackgroundImagePath))
            {
                try { _speedoBackgroundImage = LoadDetachedImage(_speedoBackgroundImagePath); }
                catch { _speedoBackgroundImage = null; }
            }

            ApplySpeedoBackgroundToOverlay();
            _speedoTachoPreview?.Invalidate();
            UpdateSpeedoPresetLabel();
        }

        private void UpdateSpeedoPresetLabel()
        {
            if (_speedoPresetLabel == null) return;
            _speedoPresetLabel.Text = string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1);
        }

        private void SwitchSpeedoPreset(int newIndex)
        {
            if (newIndex == _activeSpeedoPresetIndex) return;
            _speedoPresets[_activeSpeedoPresetIndex] = CaptureSpeedoPreset();
            _activeSpeedoPresetIndex = newIndex;
            ApplySpeedoPreset(_speedoPresets[_activeSpeedoPresetIndex]);
            SaveSettings();
        }

        private void ShowSpeedoBackgroundMenu()
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 360, 470, out _);

            MakeLabel(card, Localization.T("speedometer.background.title"), 20, 15, 300, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("speedometer.background.subtitle"), 20, 40, 320, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            _speedoBackgroundCrop = new CircularCropControl
            {
                Location = new Point((360 - 220) / 2, 80),
                Size = new Size(220, 220),
                SourceImage = _speedoBackgroundImage,
                PanX = _speedoBackgroundPanX,
                PanY = _speedoBackgroundPanY,
                Zoom = _speedoBackgroundZoom,
            };
            card.Controls.Add(_speedoBackgroundCrop);

            int y = 312;

            MakeLabel(card, Localization.T("speedometer.background.zoom"), 20, y, 150, 20, ApplePalette.Text);
            _speedoBackgroundZoomSlider = new MacSlider
            {
                Location = new Point(20, y + 20),
                Size = new Size(320, 22),
                Minimum = 50,
                Maximum = 300,
                Value = (int)Math.Round(_speedoBackgroundZoom * 100),
            };
            _speedoBackgroundZoomSlider.ValueChanged += (s, e) =>
            {
                _speedoBackgroundZoom = _speedoBackgroundZoomSlider.Value / 100f;
                _speedoBackgroundCrop.Zoom = _speedoBackgroundZoom;
                ApplySpeedoBackgroundToOverlay();
                SaveSettings();
            };
            card.Controls.Add(_speedoBackgroundZoomSlider);
            y += 50;

            MakeLabel(card, Localization.T("speedometer.background.opacity"), 20, y, 150, 20, ApplePalette.Text);
            _speedoBackgroundOpacitySlider = new MacSlider
            {
                Location = new Point(20, y + 20),
                Size = new Size(320, 22),
                Minimum = 0,
                Maximum = 100,
                Value = (int)Math.Round(_speedoBackgroundOpacity * 100),
            };
            _speedoBackgroundOpacitySlider.ValueChanged += (s, e) =>
            {
                _speedoBackgroundOpacity = _speedoBackgroundOpacitySlider.Value / 100f;
                ApplySpeedoBackgroundToOverlay();
                SaveSettings();
            };
            card.Controls.Add(_speedoBackgroundOpacitySlider);
            y += 50;

            _speedoBackgroundCrop.PanZoomChanged += (s, e) =>
            {
                _speedoBackgroundPanX = _speedoBackgroundCrop.PanX;
                _speedoBackgroundPanY = _speedoBackgroundCrop.PanY;
                _speedoBackgroundZoom = _speedoBackgroundCrop.Zoom;
                _speedoBackgroundZoomSlider.SetValueSilent((int)Math.Round(_speedoBackgroundZoom * 100));
                ApplySpeedoBackgroundToOverlay();
                SaveSettings();
            };

            var importBtn = MakeButton(card, Localization.T("speedometer.background.import"), 20, y, 150, 30, ApplePalette.Blue);
            importBtn.Click += (s, e) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "PNG image|*.png",
                    Title = Localization.T("speedometer.background.import"),
                };
                if (dlg.ShowDialog(popup) != DialogResult.OK) return;

                try
                {
                    Directory.CreateDirectory(DataImagesDir);
                    string destPath = Path.Combine(DataImagesDir, "speedo_background.png");

                    _overlay.SpeedoBackgroundImage = null;
                    _speedoBackgroundCrop.SourceImage = null;
                    _speedoBackgroundImage?.Dispose();
                    _speedoBackgroundImage = null;

                    byte[] sourceBytes = File.ReadAllBytes(dlg.FileName);
                    using (var ms = new MemoryStream(sourceBytes))
                    using (var src = Image.FromStream(ms))
                    using (var bmp = new Bitmap(src))
                        bmp.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);

                    _speedoBackgroundImage = LoadDetachedImage(destPath);
                    _speedoBackgroundImagePath = destPath;
                    _speedoBackgroundPanX = 0f;
                    _speedoBackgroundPanY = 0f;
                    _speedoBackgroundZoom = 1f;

                    _speedoBackgroundCrop.SourceImage = _speedoBackgroundImage;
                    _speedoBackgroundCrop.PanX = 0f;
                    _speedoBackgroundCrop.PanY = 0f;
                    _speedoBackgroundCrop.Zoom = 1f;
                    _speedoBackgroundZoomSlider.SetValueSilent(100);

                    ApplySpeedoBackgroundToOverlay();
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    AppendStatusMessage(ex.Message, StatusErrorColor);
                }
            };

            var removeBtn = MakeButton(card, Localization.T("speedometer.background.remove"), 190, y, 150, 30, Color.FromArgb(200, 20, 20));
            removeBtn.Click += (s, e) =>
            {
                _overlay.SpeedoBackgroundImage = null;
                _speedoBackgroundCrop.SourceImage = null;
                _speedoBackgroundImage?.Dispose();
                _speedoBackgroundImage = null;
                _speedoBackgroundImagePath = "";
                _speedoBackgroundPanX = 0f;
                _speedoBackgroundPanY = 0f;
                _speedoBackgroundZoom = 1f;

                _speedoBackgroundCrop.PanX = 0f;
                _speedoBackgroundCrop.PanY = 0f;
                _speedoBackgroundCrop.Zoom = 1f;
                _speedoBackgroundZoomSlider.SetValueSilent(100);

                try
                {
                    string destPath = Path.Combine(DataImagesDir, "speedo_background.png");
                    if (File.Exists(destPath)) File.Delete(destPath);
                }
                catch { }

                ApplySpeedoBackgroundToOverlay();
                SaveSettings();
            };

            try { ShowModalPopup(popup); }
            finally { _speedoBackgroundCrop = null; _speedoBackgroundZoomSlider = null; _speedoBackgroundOpacitySlider = null; }
        }

        private void OpenRevLimiterBindings(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 300, 320, out _);

            MakeLabel(card, Localization.T("rev.bindings.title"), 20, 15, 220, 28,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            int y = 55;

            var rowRefreshers = new List<Action>();

            void MakeRow(string labelKey, Func<InputBinding> getBinding, Action<InputBinding> onBound)
            {
                MakeLabel(card, Localization.T(labelKey), 20, y + 4, 120, 22, ApplePalette.Text);

                string RowText() => getBinding().Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : getBinding().ToString();

                var bindBtn = MakeButton(card, RowText(), 170, y - 4, 110, 30, ApplePalette.Blue);
                rowRefreshers.Add(() => bindBtn.Text = RowText());

                bindBtn.Click += (s, e) =>
                {
                    BindKey(labelKey, b =>
                    {
                        onBound(b);
                        bindBtn.Text = b.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : b.ToString();
                        SaveSettings();
                    });
                };

                y += 42;
            }

            MakeRow("rev.bindings.toggle", () => _revToggleBinding,
                b => ApplyRevBinding(ref _revToggleBinding, b, ExecuteRevToggle));
            MakeRow("rev.bindings.calibrate", () => _revCalibrateBinding,
                b => ApplyRevBinding(ref _revCalibrateBinding, b, ExecuteRevCalibrate));
            MakeRow("rev.bindings.decrease", () => _revDecreaseBinding,
                b => ApplyRevBinding(ref _revDecreaseBinding, b, ExecuteRevDecrease));
            MakeRow("rev.bindings.increase", () => _revIncreaseBinding,
                b => ApplyRevBinding(ref _revIncreaseBinding, b, ExecuteRevIncrease));

            _onBindingsChanged = () => { foreach (var refresh in rowRefreshers) refresh(); };
            try { ShowModalPopup(popup, ownerForm); }
            finally { _onBindingsChanged = null; }
        }

        private void OpenInSimPalette(Button targetBtn, Action<string> setColor, Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 240, 270, out _);

            MakeLabel(
                card,
                Localization.T("insim.colorpicker.title"),
                20,
                15,
                180,
                28,
                ApplePalette.Title,
                new Font(
                    "Segoe UI Semibold",
                    11f));

            int x = 25;
            int y = 55;

            foreach (var item in InSimPalette)
            {
                Button b = new Button
                {
                    Size = new Size(42, 42),
                    Location = new Point(x, y),

                    BackColor = item.color,

                    FlatStyle = FlatStyle.Flat,

                    Cursor = Cursors.Hand,

                    Text = ""

                };

                b.FlatAppearance.BorderSize = 0;

                b.Region = CreateSmoothRoundedRegion(b.Width, b.Height, 20);

                b.Click += (s, e) =>
                {
                    SetButtonColor(targetBtn, item.color);
                    setColor(item.code);

                    popup.Close();
                };

                b.MouseEnter += (s, e) =>
                {
                    b.Size = new Size(46, 46);
                };

                b.MouseLeave += (s, e) =>
                {
                    b.Size = new Size(42, 42);
                };

                card.Controls.Add(b);

                x += 50;

                if (x > 210)
                {
                    x = 25;
                    y += 50;
                }
            }

            ShowModalPopup(popup, ownerForm);
        }
        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        private void ApplyIndicatorVolume()
        {
            int pct = Math.Clamp(_indicatorSoundsVolume, 0, 100);
            ushort vol = (ushort)(0xFFFF * pct / 100);
            uint vol32 = ((uint)vol << 16) | vol;
            waveOutSetVolume(IntPtr.Zero, vol32);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        private void ExecuteLightToggle()
        {
            if (!_insim.IsConnected) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IntPtr hwnd = LfsWindow.Find();
                if (hwnd == IntPtr.Zero) return;

                const int VK_SHIFT = 0x10;
                const int VK_3 = 0x33;

                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT, (IntPtr)0x002A0001);
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_3, (IntPtr)0x00040001);

                System.Threading.Thread.Sleep(IndicatorKeyDelayMs);

                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_3, (IntPtr)0xC0040001);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_SHIFT, (IntPtr)0xC02A0001);
            });
        }

        private GlobalHotkey _settingsOverlayHotkey = null!;
        private InGameSettingsOverlay _inGameSettingsOverlay;
        private bool _ovRefreshing = false;
        private readonly List<Panel> _ovColumns = new();
        private Label _ovHintLabel = null!;

        private MacToggleSwitch _ovInSimConnected = null!;
        private MacToggleSwitch _ovOutGaugeConnected = null!;
        private Label _ovInSimStatusLabel = null!;
        private Label _ovOutGaugeStatusLabel = null!;
        private MacToggleSwitch _ovDarkMode = null!;
        private Button _ovLangBtn = null!;
        private MacToggleSwitch _ovRevEnabled = null!;
        private MinimalNumericUpDown _ovRevLimitNumeric = null!;
        private MinimalNumericUpDown _ovRevCutMSNumeric = null!;
        private MacToggleSwitch _ovRevAutoCalibrate = null!;
        private MacToggleSwitch _ovSpeedoEnabled = null!;
        private MacToggleSwitch _ovAirSpeed = null!;
        private MacToggleSwitch _ovPlainDigits = null!;
        private MacToggleSwitch _ovInstantSpeed = null!;
        private MacToggleSwitch _ovSpeedUnit = null!;
        private Label _ovSpeedoPresetLabel = null!;
        private MacToggleSwitch _ovHudEnabled = null!;
        private MacToggleSwitch _ovHudStyleForza = null!;
        private MacToggleSwitch _ovHudStyleBtn = null!;
        private MacToggleSwitch _ovRpmHud = null!;
        private MacToggleSwitch _ovIndicatorsEnabled = null!;
        private MacToggleSwitch _ovIndicatorAutoCancel = null!;
        private MacToggleSwitch _ovIndicatorSounds = null!;
        private MacSlider _ovIndicatorVolumeSlider = null!;
        private Label _ovIndicatorVolumeValueLabel = null!;
        private MinimalNumericUpDown _ovIndicatorArmThresholdNumeric = null!;
        private MinimalNumericUpDown _ovIndicatorCenterThresholdNumeric = null!;
        private MinimalNumericUpDown _ovMinDriftSpeedNumeric = null!;
        private MinimalNumericUpDown _ovMaxBurnoutSpeedNumeric = null!;
        private MacToggleSwitch _ovAdvancedOutGauge = null!;
        private MacToggleSwitch _ovCarCollisionDetect = null!;
        private MacToggleSwitch _ovObjectCollisionDetect = null!;

        private void ToggleInGameSettingsOverlay()
        {
            if (_inGameSettingsOverlay.Visible)
            {
                _inGameSettingsOverlay.RestoreAndHide();
                return;
            }

            IntPtr lfsHwnd = LfsWindow.Find();
            if (lfsHwnd == IntPtr.Zero || GetForegroundWindow() != lfsHwnd) return;

            _inGameSettingsOverlay.ShowOverWindow(lfsHwnd, ReflowInGameSettingsOverlayColumns, RefreshInGameSettingsOverlayValues);
        }

        private RoundedPanel MakeOvColumn(Control parent, int w, int h)
        {
            var col = new RoundedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, h),
                BackColor = ApplePalette.Card,
                Radius = 16,
            };
            parent.Controls.Add(col);
            _ovColumns.Add(col);
            return col;
        }

        private void ReflowInGameSettingsOverlayColumns(int availableWidth)
        {
            const int colW = 210, colGap = 20, colH = 380, rowGap = 20, margin = 30;

            if (_ovColumns.Count == 0) return;

            int usable = Math.Max(colW, availableWidth - margin);
            int colsPerRow = Math.Max(1, Math.Min(_ovColumns.Count, (usable + colGap) / (colW + colGap)));
            int rows = (int)Math.Ceiling(_ovColumns.Count / (double)colsPerRow);

            for (int i = 0; i < _ovColumns.Count; i++)
            {
                int row = i / colsPerRow;
                int col = i % colsPerRow;
                _ovColumns[i].Location = new Point(col * (colW + colGap), row * (colH + rowGap));
            }

            int contentW = colsPerRow * (colW + colGap) - colGap;
            int contentH = rows * (colH + rowGap) - rowGap;

            _ovHintLabel.Location = new Point(0, contentH + 14);
            _ovHintLabel.Size = new Size(contentW, 20);

            _inGameSettingsOverlay.ContentHost.Size = new Size(contentW, contentH + 14 + 20);
        }

        private MacToggleSwitch AddOvToggleRow(Control parent, string labelKey, int y, EventHandler onChanged)
        {
            var label = MakeLabel(parent, Localization.T(labelKey), 14, y + 6, 120, 20,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 9.5f), ContentAlignment.MiddleLeft, locKey: labelKey);
            _localizedControls.Add((label, labelKey));
            var toggle = new MacToggleSwitch { Location = new Point(140, y), Size = new Size(40, 20) };
            toggle.CheckedChanged += onChanged;
            parent.Controls.Add(toggle);
            return toggle;
        }

        private void BuildInGameSettingsOverlayContent(Panel host)
        {
            const int colW = 210, colH = 380, pad = 14;

            var connCol = MakeOvColumn(host, colW, colH);
            var connTitleLabel = MakeLabel(connCol, Localization.T("ingamesettings.connections"), pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f), locKey: "ingamesettings.connections");
            _localizedControls.Add((connTitleLabel, "ingamesettings.connections"));

            _ovInSimConnected = AddOvToggleRow(connCol, "ingamesettings.insim", 48, (s, e) =>
            {
                if (_ovRefreshing) return;
                _connectionSwitch.Checked = _ovInSimConnected.Checked;
            });
            _ovInSimStatusLabel = MakeLabel(connCol, "", pad, 78, colW - pad * 2, 18,
                ApplePalette.StatusIdle, UiFonts.Get("Segoe UI", 8f));
            connCol.Controls.Add(_ovInSimStatusLabel);

            _ovOutGaugeConnected = AddOvToggleRow(connCol, "ingamesettings.outgauge", 116, (s, e) =>
            {
                if (_ovRefreshing) return;
                _outGaugeConnectionSwitch.Checked = _ovOutGaugeConnected.Checked;
            });
            _ovOutGaugeStatusLabel = MakeLabel(connCol, "", pad, 146, colW - pad * 2, 18,
                ApplePalette.StatusIdle, UiFonts.Get("Segoe UI", 8f));
            connCol.Controls.Add(_ovOutGaugeStatusLabel);

            _ovDarkMode = AddOvToggleRow(connCol, "header.theme", 190, (s, e) =>
            {
                if (_ovRefreshing) return;
                _themeSwitch.Checked = _ovDarkMode.Checked;
            });

            var ovLangLabel = MakeLabel(connCol, Localization.T("header.language"), pad, 236, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "header.language");
            _localizedControls.Add((ovLangLabel, "header.language"));
            _ovLangBtn = MakeButton(connCol, Localization.LanguageDisplayName(Localization.CurrentLanguage),
                pad, 256, colW - pad * 2, 30, ApplePalette.Blue);
            _ovLangBtn.Click += (s, e) => OpenLanguagePicker(_ovLangBtn, _inGameSettingsOverlay);

            var revCol = MakeOvColumn(host, colW, colH);
            var revTitleLabel = MakeLabel(revCol, "Rev Limiter", pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f), locKey: "rev.title");
            _localizedControls.Add((revTitleLabel, "rev.title"));
            _ovRevEnabled = AddOvToggleRow(revCol, "ingamesettings.enabled", 48, (s, e) =>
            {
                if (_ovRefreshing) return;
                if (!_revEnableSwitch.Enabled)
                {
                    _ovRevEnabled.SetCheckedSilent(_revEnableSwitch.Checked);
                    return;
                }
                _revEnableSwitch.Checked = _ovRevEnabled.Checked;
            });
            var revRpmLabel = MakeLabel(revCol, Localization.T("rev.rpm"), pad, 92, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "rev.rpm");
            _localizedControls.Add((revRpmLabel, "rev.rpm"));
            _ovRevLimitNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 112),
                Size = new Size(colW - pad * 2, 30),
                Minimum = RevLimitMinRpm,
                Maximum = RevLimitMaxRpm,
                Increment = 100,
            };
            _ovRevLimitNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _revLimiterNumeric.Value = v;
            };
            revCol.Controls.Add(_ovRevLimitNumeric);

            var revCutLabel = MakeLabel(revCol, Localization.T("rev.cutms"), pad, 146, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "rev.cutms");
            _localizedControls.Add((revCutLabel, "rev.cutms"));
            _ovRevCutMSNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 166),
                Size = new Size(colW - pad * 2, 30),
                Minimum = RevCutMinMs,
                Maximum = RevCutMaxMs,
                Increment = 5,
            };
            _ovRevCutMSNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _revCutMS.Value = v;
            };
            revCol.Controls.Add(_ovRevCutMSNumeric);

            _ovRevAutoCalibrate = AddOvToggleRow(revCol, "ingamesettings.autocalibrate", 206, (s, e) =>
            {
                if (_ovRefreshing) return;
                _revAutoCalibrateNewCarCheck.Checked = _ovRevAutoCalibrate.Checked;
            });

            var revBindFunctionsBtn = MakeButton(revCol, Localization.T("rev.bindfunctions"), pad, 250, colW - pad * 2, 30, ApplePalette.Blue, locKey: "rev.bindfunctions");
            revBindFunctionsBtn.Click += (s, e) => OpenRevLimiterBindings(_inGameSettingsOverlay);
            _localizedControls.Add((revBindFunctionsBtn, "rev.bindfunctions"));

            var speedoCol = MakeOvColumn(host, colW, colH);
            MakeLabel(speedoCol, "Speedometer", pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            _ovSpeedoEnabled = AddOvToggleRow(speedoCol, "ingamesettings.enabled", 48, (s, e) =>
            {
                if (_ovRefreshing) return;
                _speedoTachoEnabledCheck.Checked = _ovSpeedoEnabled.Checked;
            });
            _ovAirSpeed = AddOvToggleRow(speedoCol, "speedometer.elements.airspeed", 88, (s, e) =>
            {
                if (_ovRefreshing) return;
                _overlay.SpeedoTachoAirSpeed = _ovAirSpeed.Checked;
                SaveSettings();
            });
            _ovPlainDigits = AddOvToggleRow(speedoCol, "ingamesettings.plaindigits", 128, (s, e) =>
            {
                if (_ovRefreshing) return;
                _overlay.SpeedoTachoPlainDigits = _ovPlainDigits.Checked;
                SaveSettings();
            });
            _ovInstantSpeed = AddOvToggleRow(speedoCol, "ingamesettings.instantspeed", 168, (s, e) =>
            {
                if (_ovRefreshing) return;
                _overlay.SpeedoTachoInstantSpeed = _ovInstantSpeed.Checked;
                SaveSettings();
            });

            _ovSpeedoPresetLabel = MakeLabel(speedoCol, "", pad, 208, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleCenter);
            speedoCol.Controls.Add(_ovSpeedoPresetLabel);

            var speedoPresetPrevBtn = MakeButton(speedoCol, "‹", pad, 230, 50, 32, ApplePalette.Blue);
            speedoPresetPrevBtn.Click += (s, e) =>
            {
                SwitchSpeedoPreset(_activeSpeedoPresetIndex == 0 ? 1 : 0);
                _ovSpeedoPresetLabel.Text = string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1);
            };
            var speedoPresetNextBtn = MakeButton(speedoCol, "›", colW - pad - 50, 230, 50, 32, ApplePalette.Blue);
            speedoPresetNextBtn.Click += (s, e) =>
            {
                SwitchSpeedoPreset(_activeSpeedoPresetIndex == 0 ? 1 : 0);
                _ovSpeedoPresetLabel.Text = string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1);
            };

            _ovSpeedUnit = AddOvToggleRow(speedoCol, "speedometer.usemph", 270, (s, e) =>
            {
                if (_ovRefreshing) return;
                _speedUnitToggle.Checked = _ovSpeedUnit.Checked;
            });

            var speedoElementsBtn = MakeButton(speedoCol, Localization.T("speedometer.elements.button"), pad, 310, (colW - pad * 2 - 8) / 2, 30, ApplePalette.Blue, locKey: "speedometer.elements.button");
            speedoElementsBtn.Click += (s, e) => ShowSpeedoElementsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((speedoElementsBtn, "speedometer.elements.button"));

            var speedoColorsBtn = MakeButton(speedoCol, Localization.T("hud.colors"), pad + (colW - pad * 2 - 8) / 2 + 8, 310, (colW - pad * 2 - 8) / 2, 30, ApplePalette.Blue, locKey: "hud.colors");
            speedoColorsBtn.Click += (s, e) => ShowHudColorsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((speedoColorsBtn, "hud.colors"));

            var hudCol = MakeOvColumn(host, colW, colH);
            MakeLabel(hudCol, "HUD", pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            _ovHudEnabled = AddOvToggleRow(hudCol, "ingamesettings.enabled", 48, (s, e) =>
            {
                if (_ovRefreshing) return;
                _hudMasterEnabledCheck.Checked = _ovHudEnabled.Checked;
                _ovHudStyleForza.Enabled = _showOverlayCheck.Enabled;
                _ovHudStyleBtn.Enabled = _showHudCheck.Enabled;
                _ovRefreshing = true;
                _ovHudStyleForza.SetCheckedSilent(_showOverlayCheck.Checked);
                _ovHudStyleBtn.SetCheckedSilent(_showHudCheck.Checked);
                _ovRefreshing = false;
            });
            _ovHudStyleForza = AddOvToggleRow(hudCol, "hud.showoverlay", 88, (s, e) =>
            {
                if (_ovRefreshing) return;
                _showOverlayCheck.Checked = _ovHudStyleForza.Checked;
                _ovRefreshing = true;
                _ovHudStyleBtn.SetCheckedSilent(_showHudCheck.Checked);
                _ovRefreshing = false;
            });
            _ovHudStyleBtn = AddOvToggleRow(hudCol, "ingamesettings.isbtnhud", 128, (s, e) =>
            {
                if (_ovRefreshing) return;
                _showHudCheck.Checked = _ovHudStyleBtn.Checked;
                _ovRefreshing = true;
                _ovHudStyleForza.SetCheckedSilent(_showOverlayCheck.Checked);
                _ovRefreshing = false;
            });
            _ovRpmHud = AddOvToggleRow(hudCol, "ingamesettings.rpmhud", 168, (s, e) =>
            {
                if (_ovRefreshing) return;
                _showRPMHudCheck.Checked = _ovRpmHud.Checked;
            });
            var hudColorsBtn = MakeButton(hudCol, Localization.T("ingamesettings.hudcolors"), pad, 208, colW - pad * 2, 30, ApplePalette.Blue, locKey: "ingamesettings.hudcolors");
            hudColorsBtn.Click += (s, e) => ShowScoreColorsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((hudColorsBtn, "ingamesettings.hudcolors"));

            var indicatorsCol = MakeOvColumn(host, colW, colH);
            var indicatorsTitleLabel = MakeLabel(indicatorsCol, Localization.T("indicators.title"), pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f), locKey: "indicators.title");
            _localizedControls.Add((indicatorsTitleLabel, "indicators.title"));
            _ovIndicatorsEnabled = AddOvToggleRow(indicatorsCol, "ingamesettings.enabled", 48, (s, e) =>
            {
                if (_ovRefreshing) return;
                _indicatorsMasterEnabledCheck.Checked = _ovIndicatorsEnabled.Checked;
            });
            _ovIndicatorAutoCancel = AddOvToggleRow(indicatorsCol, "ingamesettings.autocancel", 88, (s, e) =>
            {
                if (_ovRefreshing) return;
                _indicatorAutoCancelCheck.Checked = _ovIndicatorAutoCancel.Checked;
            });
            _ovIndicatorSounds = AddOvToggleRow(indicatorsCol, "ingamesettings.sounds", 128, (s, e) =>
            {
                if (_ovRefreshing) return;
                _indicatorSoundsCheck.Checked = _ovIndicatorSounds.Checked;
            });

            var ovVolumeLabel = MakeLabel(indicatorsCol, Localization.T("indicators.volume"), pad, 168, 100, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "indicators.volume");
            _localizedControls.Add((ovVolumeLabel, "indicators.volume"));
            _ovIndicatorVolumeValueLabel = MakeLabel(indicatorsCol, "", colW - pad - 40, 168, 40, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleRight);
            indicatorsCol.Controls.Add(_ovIndicatorVolumeValueLabel);

            _ovIndicatorVolumeSlider = new MacSlider
            {
                Location = new Point(pad, 190),
                Size = new Size(colW - pad * 2, 22),
                Minimum = 0,
                Maximum = 100,
            };
            _ovIndicatorVolumeSlider.ValueChanged += (s, e) =>
            {
                if (_ovRefreshing) return;
                _indicatorVolumeSlider.Value = _ovIndicatorVolumeSlider.Value;
            };
            indicatorsCol.Controls.Add(_ovIndicatorVolumeSlider);

            var ovArmThresholdLabel = MakeLabel(indicatorsCol, Localization.T("indicators.armthreshold"), pad, 232, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "indicators.armthreshold");
            _localizedControls.Add((ovArmThresholdLabel, "indicators.armthreshold"));
            _ovIndicatorArmThresholdNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 252),
                Size = new Size(colW - pad * 2, 28),
                Minimum = 1,
                Maximum = 100,
            };
            _ovIndicatorArmThresholdNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _indicatorArmThresholdNumeric.Value = v;
            };
            indicatorsCol.Controls.Add(_ovIndicatorArmThresholdNumeric);

            var ovCenterThresholdLabel = MakeLabel(indicatorsCol, Localization.T("indicators.centerthreshold"), pad, 288, colW - pad * 2, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), locKey: "indicators.centerthreshold");
            _localizedControls.Add((ovCenterThresholdLabel, "indicators.centerthreshold"));
            _ovIndicatorCenterThresholdNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 308),
                Size = new Size(colW - pad * 2, 28),
                Minimum = 1,
                Maximum = 90,
            };
            _ovIndicatorCenterThresholdNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _indicatorCenterThresholdNumeric.Value = v;
            };
            indicatorsCol.Controls.Add(_ovIndicatorCenterThresholdNumeric);

            var scoreCol = MakeOvColumn(host, colW, colH);
            MakeLabel(scoreCol, "Score", pad, 12, colW - pad * 2, 22,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            var minDriftLabel = MakeLabel(scoreCol, Localization.T("score.mindriftspeed"), pad, 40, colW - pad * 2, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.TopLeft, locKey: "score.mindriftspeed");
            _localizedControls.Add((minDriftLabel, "score.mindriftspeed"));
            _ovMinDriftSpeedNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 74),
                Size = new Size(colW - pad * 2, 30),
                Minimum = 0,
                Maximum = 100,
                Increment = 1,
            };
            _ovMinDriftSpeedNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _minDriftSpeedNumeric.Value = v;
            };
            scoreCol.Controls.Add(_ovMinDriftSpeedNumeric);

            var maxBurnoutLabel = MakeLabel(scoreCol, Localization.T("score.maxburnoutspeed"), pad, 114, colW - pad * 2, 32,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.TopLeft, locKey: "score.maxburnoutspeed");
            _localizedControls.Add((maxBurnoutLabel, "score.maxburnoutspeed"));
            _ovMaxBurnoutSpeedNumeric = new MinimalNumericUpDown
            {
                Location = new Point(pad, 148),
                Size = new Size(colW - pad * 2, 30),
                Minimum = 0,
                Maximum = 150,
                Increment = 1,
            };
            _ovMaxBurnoutSpeedNumeric.ValueChanged += v =>
            {
                if (_ovRefreshing) return;
                _maxBurnoutSpeedNumeric.Value = v;
            };
            scoreCol.Controls.Add(_ovMaxBurnoutSpeedNumeric);

            _ovAdvancedOutGauge = AddOvToggleRow(scoreCol, "ingamesettings.advancedoutgauge", 188, (s, e) =>
            {
                if (_ovRefreshing) return;
                _advancedOutGaugeCheck.Checked = _ovAdvancedOutGauge.Checked;
            });
            _ovCarCollisionDetect = AddOvToggleRow(scoreCol, "ingamesettings.cars", 224, (s, e) =>
            {
                if (_ovRefreshing) return;
                _collisionDetectCheck.Checked = _ovCarCollisionDetect.Checked;
            });
            _ovObjectCollisionDetect = AddOvToggleRow(scoreCol, "ingamesettings.objects", 260, (s, e) =>
            {
                if (_ovRefreshing) return;
                _objectCollisionDetectCheck.Checked = _ovObjectCollisionDetect.Checked;
            });

            int halfW = (colW - pad * 2 - 8) / 2;
            var angleLevelsBtn = MakeButton(scoreCol, Localization.T("score.levels.button"), pad, 296, halfW, 30, ApplePalette.Blue, locKey: "score.levels.button");
            angleLevelsBtn.Click += (s, e) => ShowAngleLevelsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((angleLevelsBtn, "score.levels.button"));

            var speedLevelsBtn = MakeButton(scoreCol, Localization.T("score.levels.speed_button"), pad + halfW + 8, 296, halfW, 30, ApplePalette.Blue, locKey: "score.levels.speed_button");
            speedLevelsBtn.Click += (s, e) => ShowSpeedLevelsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((speedLevelsBtn, "score.levels.speed_button"));

            var hitLevelsBtn = MakeButton(scoreCol, Localization.T("derby.levels.button"), pad, 336, colW - pad * 2, 30, ApplePalette.Blue, locKey: "derby.levels.button");
            hitLevelsBtn.Click += (s, e) => ShowHitLevelsMenu(_inGameSettingsOverlay);
            _localizedControls.Add((hitLevelsBtn, "derby.levels.button"));

            _ovHintLabel = MakeLabel(host, Localization.T("ingamesettings.hint"), 0, 0,
                colW, 20, UiColors.Muted, UiFonts.Get("Segoe UI", 8.5f),
                ContentAlignment.MiddleCenter, locKey: "ingamesettings.hint");
            _localizedControls.Add((_ovHintLabel, "ingamesettings.hint"));

            ReflowInGameSettingsOverlayColumns(_ovColumns.Count * 230);
        }

        private void RefreshInGameSettingsOverlayValues()
        {
            _ovRefreshing = true;

            _ovInSimConnected.Enabled = _connectionSwitch.Enabled;
            _ovInSimConnected.SetCheckedSilent(_connectionSwitch.Checked);
            _ovInSimStatusLabel.Text = _connectionStateLabel.Text;
            _ovInSimStatusLabel.ForeColor = _connectionStateLabel.ForeColor;
            _ovOutGaugeConnected.SetCheckedSilent(_outGaugeConnectionSwitch.Checked);
            _ovOutGaugeStatusLabel.Text = _outGaugeStateLabel.Text;
            _ovOutGaugeStatusLabel.ForeColor = _outGaugeStateLabel.ForeColor;
            _ovDarkMode.SetCheckedSilent(_themeSwitch.Checked);

            _ovRevEnabled.SetCheckedSilent(_revEnableSwitch.Checked);
            _ovRevLimitNumeric.Value = _revLimiterNumeric.Value;
            _ovRevCutMSNumeric.Value = _revCutMS.Value;
            _ovRevAutoCalibrate.Enabled = _revAutoCalibrateNewCarCheck.Enabled;
            _ovRevAutoCalibrate.SetCheckedSilent(_revAutoCalibrateNewCarCheck.Checked);
            _ovSpeedoEnabled.SetCheckedSilent(_speedoTachoEnabledCheck.Checked);
            _ovAirSpeed.SetCheckedSilent(_overlay.SpeedoTachoAirSpeed);
            _ovPlainDigits.SetCheckedSilent(_overlay.SpeedoTachoPlainDigits);
            _ovInstantSpeed.SetCheckedSilent(_overlay.SpeedoTachoInstantSpeed);
            _ovSpeedUnit.SetCheckedSilent(_speedUnitToggle.Checked);
            _ovSpeedoPresetLabel.Text = string.Format(Localization.T("speedometer.preset.label"), _activeSpeedoPresetIndex + 1);
            _ovHudEnabled.SetCheckedSilent(_hudMasterEnabledCheck.Checked);
            _ovHudStyleForza.Enabled = _showOverlayCheck.Enabled;
            _ovHudStyleForza.SetCheckedSilent(_showOverlayCheck.Checked);
            _ovHudStyleBtn.Enabled = _showHudCheck.Enabled;
            _ovHudStyleBtn.SetCheckedSilent(_showHudCheck.Checked);
            _ovRpmHud.Enabled = _showRPMHudCheck.Enabled;
            _ovRpmHud.SetCheckedSilent(_showRPMHudCheck.Checked);
            _ovIndicatorsEnabled.SetCheckedSilent(_indicatorsMasterEnabledCheck.Checked);
            _ovIndicatorAutoCancel.Enabled = _indicatorAutoCancelCheck.Enabled;
            _ovIndicatorAutoCancel.SetCheckedSilent(_indicatorAutoCancelCheck.Checked);
            _ovIndicatorSounds.Enabled = _indicatorSoundsCheck.Enabled;
            _ovIndicatorSounds.SetCheckedSilent(_indicatorSoundsCheck.Checked);
            _ovIndicatorVolumeSlider.SetValueSilent(_indicatorVolumeSlider.Value);
            _ovIndicatorVolumeValueLabel.Text = $"{_indicatorVolumeSlider.Value}%";
            _ovIndicatorArmThresholdNumeric.Enabled = _indicatorArmThresholdNumeric.Enabled;
            _ovIndicatorArmThresholdNumeric.Value = _indicatorArmThresholdNumeric.Value;
            _ovIndicatorCenterThresholdNumeric.Enabled = _indicatorCenterThresholdNumeric.Enabled;
            _ovIndicatorCenterThresholdNumeric.Value = _indicatorCenterThresholdNumeric.Value;
            _ovMinDriftSpeedNumeric.Value = _minDriftSpeedNumeric.Value;
            _ovMaxBurnoutSpeedNumeric.Value = _maxBurnoutSpeedNumeric.Value;
            _ovAdvancedOutGauge.Enabled = _advancedOutGaugeCheck.Enabled;
            _ovAdvancedOutGauge.SetCheckedSilent(_advancedOutGaugeCheck.Checked);
            _ovCarCollisionDetect.SetCheckedSilent(_collisionDetectCheck.Checked);
            _ovObjectCollisionDetect.SetCheckedSilent(_objectCollisionDetectCheck.Checked);

            _ovRefreshing = false;
        }

        private byte _driverNamePlid = 0;

        private void TryApplyInSimDriverName(byte plid)
        {
            if (plid == 0) return;

            if (plid != _lastKnownPlayerPLID) return;

            string? name = _insim.GetPlayerName(plid);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (plid == _driverNamePlid && name == _drift.CurrentDriver) return;

            _driverNamePlid = plid;
            _drift.SetDriver(name);

            _drift.ResetCurrentRun();

            ForceReloadOverlayForCurrentDriver();
        }

        private void ForceReloadOverlayForCurrentDriver()
        {
            _overlay.UpdateScore(_drift.TotalScore);
            _overlay.UpdateRun(_drift.CurrentRunPoints);
            _overlay.UpdateCombo(_drift.ComboMultiplier);
            _overlay.UpdateLapScore(_drift.LapScore);
            _overlay.UpdateBestLapScore(_drift.BestLapScore);
            _overlay.UpdateLastLapScore(_drift.LastLapScore);
            _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
            UpdateLapContextLabel();
            UpdateScoreLabels();

            var confirmTimer = new System.Windows.Forms.Timer { Interval = OverlayReloadConfirmDelayMs };
            confirmTimer.Tick += (s, e) =>
            {
                confirmTimer.Stop();
                confirmTimer.Dispose();

                _overlay.UpdateScore(_drift.TotalScore);
                _overlay.UpdateRun(_drift.CurrentRunPoints);
                _overlay.UpdateCombo(_drift.ComboMultiplier);
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.UpdateLastLapScore(_drift.LastLapScore);
                UpdateScoreLabels();
            };
            confirmTimer.Start();
        }

        private void OnTrackedPlayerChanged()
        {
            _drift.ResetLapScore();
            _overlay.UpdateLapScore(_drift.LapScore);
            _overlay.SetLapBoxVisible(false);
            UpdateLapContextLabel();
        }

        private void EnterParkedState()
        {
            if (_localPlayerParked || _myOwnPlid == 0) return;

            _drift.ResetLapScore();
            _overlay.UpdateLapScore(_drift.LapScore);
            _overlay.SetLapBoxVisible(false);

            _lastKnownPlayerPLID = _myOwnPlid;
            _localPlayerNickAtPark = _insim.GetPlayerName(_myOwnPlid);
            _localPlayerParked = true;
            _overlay.ResetCarData();
            _overlay.ResetOutGaugeData();
            _controllerAssistState.Reset();

            if (_showHudCheck.Checked)
                _hud.ClearAllButtons();
        }

        private void OnCarData(object? sender, CarDataEventArgs e)
        {

            byte viewPlid = _insim.ViewPLID;
            byte expectedPlid = viewPlid != 0 ? viewPlid : _lastKnownPlayerPLID;

            if (expectedPlid == 0 || e.Car.PLID != expectedPlid)
                return;

            if (!IsHandleCreated || IsDisposed)
                return;

            double speed = e.Car.SpeedKmh;

            PostToUi(() =>
            {
                if (_localPlayerParked)
                    return;

                if (viewPlid != 0)
                {
                    if (viewPlid != _lastKnownPlayerPLID && _lastKnownPlayerPLID != 0)
                        OnTrackedPlayerChanged();

                    _lastKnownPlayerPLID = viewPlid;
                }
                else if (_lastKnownPlayerPLID == 0)
                {
                    return;
                }

                if (e.Car.PLID != _lastKnownPlayerPLID)
                    return;

                TryApplyInSimDriverName(_lastKnownPlayerPLID);

                if (!_showHudCheck.Checked)
                    _hud.ShowInGameAward("");

                _overlay.NotifyCarData();
                _drift.SetOutGaugeDataFresh(_outGaugeFreshness.IsFresh);
                _drift.Update(e.Car);
                _indicators.Update();

                _speedKmh = speed;
                _driftAngle = _drift.DriftAngleDeg;
                _isDrifting = _drift.IsDrifting;
                _isSpeeding = _drift.IsSpeeding;
                _isBurnout = _drift.IsBurnout;
                _overlay.SetDriftAngle(_driftAngle, _isDrifting, _drift.DriftSideRight);
                _overlay.UpdateSpeedGauge(_speedKmh);

                _overlay.SetActive(_isDrifting || _isSpeeding || _isBurnout);
                _overlay.UpdateLapScore(_drift.LapScore);

                if (!_isDrifting && !_isSpeeding && !_isBurnout)
                    _overlay.UpdateAccentColor(OverlayColorIdle);

                _angleValueLabel.Text = ((int)_driftAngle).ToString() + "°";
                _angleValueLabel.ForeColor = _isDrifting
                    ? Color.FromArgb(255, 80, 80) : Color.FromArgb(60, 180, 255);

                if (_showHudCheck.Checked && _insim.IsConnected)
                    _hud.UpdateInGameHUD();

                if (!string.IsNullOrEmpty(_drift.LastAwardedText) && _drift.LastAwardedText != _lastOverlayBonusText)
                {
                    _overlay.ShowBonus(_drift.LastAwardedText);
                    _lastOverlayBonusText = _drift.LastAwardedText;
                }
            });
        }

        private DriftLabelKind _driftLabelKind;

        private void OnDriftScored(long runPts, double combo, string label, string labelindr, string labelindl, DriftLabelKind labelKind)
        {
            PostToUi(() =>
            {
                _runPoints = runPts;
                _totalScore = _drift.TotalScore;
                _combo = combo;
                _driftLabelKind = labelKind;

                _hud.DriftLabel = label;
                _hud.CurrentLabelKind = labelKind;
                _hud.IndicatorLabelRight = labelindr;
                _hud.IndicatorLabelLeft = labelindl;

                UpdateScoreLabels();

                if (_showHudCheck.Checked && _insim.IsConnected)
                    _hud.UpdateInGameHUD();

                int colorTier = DriftEngine.GetColorTier(_driftLabelKind);
                string angleColCode = InSimColorForTier(colorTier);
                Color overlayAccent = GetOverlayColorSlot(colorTier);

                _overlay.UpdateScore(_totalScore);
                _overlay.UpdateRun(_runPoints);
                _overlay.UpdateCombo(_combo);
                _overlay.UpdateLabel(label);
                _overlay.UpdateAccentColor(overlayAccent);

                if (!string.IsNullOrEmpty(_drift.LastAwardedText) && _drift.LastAwardedText != _lastOverlayBonusText)
                {
                    _overlay.ShowBonus(_drift.LastAwardedText);
                    _lastOverlayBonusText = _drift.LastAwardedText;
                }
                else if (string.IsNullOrEmpty(_drift.LastAwardedText))
                {
                    _lastOverlayBonusText = "";
                }
            });
        }

        private void OnDriftStarted()
        {
            PostToUi(() =>
            {
                _runPoints = 0;
                UpdateScoreLabels();
            });
        }
        private string _lastOverlayBonusText = "";

        private DateTime _lastObjectHitTime = DateTime.MinValue;
        private static readonly TimeSpan ObjectHitCooldown = TimeSpan.FromMilliseconds(500);

        private bool _objectHitCheckPending = false;

        private void HandleObjectHit(string objectName)
        {
            var buforspeed = _speedKmh;
            var buforangle = _driftAngle;
            var now = DateTime.UtcNow;
            if (now - _lastObjectHitTime < ObjectHitCooldown) return;
            _lastObjectHitTime = now;

            if (_drift.IsSpeeding)
            {
                if (_objectHitCheckPending) return;
                _objectHitCheckPending = true;

                var speedingDelayTimer = new System.Windows.Forms.Timer { Interval = ObjectHitResolveDelayMs };
                speedingDelayTimer.Tick += (s, e) =>
                {
                    speedingDelayTimer.Stop();
                    speedingDelayTimer.Dispose();
                    _objectHitCheckPending = false;

                    if (buforspeed - _speedKmh < 10)
                        _drift.AwardUnstoppableBonus();
                    else
                    {
                        _drift.ApplyPostPoints(-100, string.Format(Localization.T("collision.hit"), objectName, 100));
                        _drift.RegisterCollisionPenalty();
                    }

                    PushObjectHitOverlayState();
                };
                speedingDelayTimer.Start();
                return;
            }

            if (!_drift.IsDrifting)
            {

                _drift.ApplyPostPoints(-100, string.Format(Localization.T("collision.hit"), objectName, 100));
                _drift.RegisterCollisionPenalty();
                PushObjectHitOverlayState();
                return;
            }

            if (_objectHitCheckPending) return;
            _objectHitCheckPending = true;

            var delayTimer = new System.Windows.Forms.Timer { Interval = ObjectHitResolveDelayMs };
            delayTimer.Tick += (s, e) =>
            {
                _objectHitCheckPending = false;
                double anglegap = 0;
                if (buforangle > _driftAngle) { anglegap = buforangle - _driftAngle; }
                if (buforangle < _driftAngle) { anglegap = _driftAngle - buforangle; }
                delayTimer.Stop();
                delayTimer.Dispose();
                if (_speedKmh >= 15 && _speedKmh >= buforspeed / 1.2 && anglegap <= 35)
                {
                    if (_drift.IsDrifting)
                    {
                        long bonus = (long)Math.Round(250 * _drift.ComboMultiplier);
                        _drift.ApplyPostPoints(bonus, string.Format(Localization.T("collision.kiss"), objectName, bonus));
                    }
                    else
                    {
                        _drift.ApplyPostPoints(-250, string.Format(Localization.T("collision.hit"), objectName, 250));
                        _drift.RegisterCollisionPenalty();
                    }
                }
                else
                {
                    _drift.ApplyPostPoints(-250, string.Format(Localization.T("collision.hit"), objectName, 250));
                    _drift.RegisterCollisionPenalty();
                }

                PushObjectHitOverlayState();
            };
            delayTimer.Start();
        }

        private void PushObjectHitOverlayState()
        {
            UpdateScoreLabels();
            _overlay.UpdateScore(_drift.TotalScore);
            _overlay.UpdateRun(_drift.CurrentRunPoints);
            _overlay.UpdateLapScore(_drift.LapScore);
            _overlay.UpdateCombo(_drift.ComboMultiplier);

            if (!string.IsNullOrEmpty(_drift.LastAwardedText) && _drift.LastAwardedText != _lastOverlayBonusText)
            {
                _overlay.ShowBonus(_drift.LastAwardedText);
                _lastOverlayBonusText = _drift.LastAwardedText;
            }

            if (_showHudCheck.Checked) _hud.ShowInGameAward(_drift.LastAwardedText);
        }

        private void OnDriftEnded(long runPts)
        {
            PostToUi(() =>
            {
                _lastAward = _drift.LastAwardedText;
                UpdateScoreLabels();
                if (_showHudCheck.Checked && _insim.IsConnected)
                {

                    _hud.ShowInGameAward(_lastAward);
                    _hud.StartAwardFlash();
                }
                else
                {
                    _hud.ClearAllButtons();
                }
            });
        }

        private static Color StatusErrorColor => ApplePalette.StatusError;
        private static Color StatusSuccessColor => ApplePalette.StatusOk;

        private void AppendStatusMessage(string message, Color? color = null)
        {
            if (_statusLabel == null || string.IsNullOrEmpty(message)) return;
            if (!IsUiThread)
            {
                PostToUi(() => AppendStatusMessage(message, color));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

            _statusLabel.SelectionStart = _statusLabel.TextLength;
            _statusLabel.SelectionLength = 0;
            _statusLabel.SelectionColor = color ?? ApplePalette.Text;
            _statusLabel.AppendText((_statusLabel.TextLength > 0 ? Environment.NewLine : "") + line);
            TrimStatusLines();

            _statusLabel.SelectionStart = _statusLabel.TextLength;
            _statusLabel.ScrollToCaret();
        }

        private const int MaxStatusLines = 500;

        private void TrimStatusLines()
        {
            int lineCount = _statusLabel.GetLineFromCharIndex(_statusLabel.TextLength) + 1;
            if (lineCount <= MaxStatusLines) return;

            int cut = _statusLabel.GetFirstCharIndexFromLine(lineCount - MaxStatusLines);
            if (cut <= 0) return;

            bool wasReadOnly = _statusLabel.ReadOnly;
            _statusLabel.ReadOnly = false;
            _statusLabel.Select(0, cut);
            _statusLabel.SelectedText = "";
            _statusLabel.ReadOnly = wasReadOnly;
        }

        private void OnStatus(object? sender, StatusEventArgs e)
        {
            PostToUi(() =>
            {
                AppendStatusMessage(e.Message, e.IsError ? StatusErrorColor : StatusSuccessColor);
            });
        }

        private DateTime _lastIndicatorCancelTime = DateTime.MinValue;
        private const int IndicatorCancelSuppressMs = 250;

        // A car can flip via Roll (tripping sideways) or Pitch (tumbling nose-over-tail off a
        // wall/curb) or any mix of the two, so checking Roll alone misses head-on flips. The
        // car's local up vector (0,0,1) rotated by pitch and roll has world Z component
        // cos(Pitch) * cos(Roll) regardless of which order the two rotations are applied in;
        // that's positive while upright and crosses zero at 90 degrees of combined tilt.
        // -0.5 corresponds to roughly 120 degrees of combined tilt from upright, past a car
        // resting hard on its side, short of requiring a dead-on roof landing.
        private const double UpsideDownUpZThreshold = -0.5;

        private long _outGaugePacketCount = 0;
        private uint _lastShowLightsRaw = 0;
        private DateTime _lastIndicatorDiagUpdate = DateTime.MinValue;

        private void UpdateIndicatorDiagnosticsLabel()
        {
            if (_indicatorStatusLabel == null) return;

            string stateLine = $"{Localization.T("indicators.currentstatus")} {_indicators.CurrentState}";

            string bitsLine =
                $"Lights=0x{_lastShowLightsRaw:X4} L={(_indicators.LeftLampOn ? "O" : "X")} R={(_indicators.RightLampOn ? "O" : "X")} Any={(_indicators.AnySignalLampOn ? "O" : "X")}";

            _indicatorStatusLabel.Text = stateLine + "\n" + bitsLine;
            _indicatorStatusLabel.ForeColor = _outGaugePacketCount > 0
                ? Color.FromArgb(150, 200, 100)
                : Color.FromArgb(220, 90, 90);
        }

        private void OnIndicatorStateChanged(IndicatorManager.IndicatorState newState)
        {

            PostToUi(() =>
            {

                if (!_insim.IsConnected)
                    return;

                _indicatorDisplayLabel.Text = _indicators.GetIndicatorText();

                _indicatorDisplayLabel.ForeColor = newState switch
                {
                    IndicatorManager.IndicatorState.Left => Color.FromArgb(100, 200, 255),
                    IndicatorManager.IndicatorState.Right => Color.FromArgb(100, 200, 255),
                    IndicatorManager.IndicatorState.Hazard => Color.FromArgb(255, 180, 0),
                    _ => Color.FromArgb(100, 100, 100)
                };

                System.Threading.ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(IndicatorKeyDelayMs);
                        _indicators.SendKeyToLFS();
                    }
                    catch { }
                });

                if (newState == IndicatorManager.IndicatorState.Off && _indicatorSoundsCheck.Checked)
                {
                    try
                    {
                        ApplyIndicatorVolume();

                        _indicatorClickOn.Stop();
                        _indicatorClickOff.Stop();
                        _indicatorCancel.Stop();
                        _indicatorCancel.Play();

                        _lastIndicatorCancelTime = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {

                        AppendStatusMessage(string.Format(Localization.T("indicators.sound_error"), ex.Message));
                    }
                }

                UpdateIndicatorDiagnosticsLabel();
            });
        }

        private void OnIndicatorLampStateChanged(bool isOn)
        {
            PostToUi(() =>
            {
                if (!_insim.IsConnected) return;
                if (!_indicatorSoundsCheck.Checked) return;

                if ((DateTime.UtcNow - _lastIndicatorCancelTime).TotalMilliseconds < IndicatorCancelSuppressMs)
                    return;

                try
                {
                    ApplyIndicatorVolume();

                    _indicatorClickOn.Stop();
                    _indicatorClickOff.Stop();

                    if (isOn)
                        _indicatorClickOn.Play();
                    else
                        _indicatorClickOff.Play();
                }
                catch (Exception ex)
                {

                    AppendStatusMessage(string.Format(Localization.T("indicators.sound_error"), ex.Message));
                }

                UpdateIndicatorDiagnosticsLabel();
            });
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            PostToUi(() =>
            {
                // A reconnect within the goodbye-notification window of a previous disconnect
                // must cancel that disconnect's pending overlay-detach timer, or it fires late
                // and rips the whole overlay (score/speedo/laps/steering bar) away right after
                // this reattaches it.
                if (_overlayDetachTimer != null)
                {
                    _overlayDetachTimer.Stop();
                    _overlayDetachTimer.Dispose();
                    _overlayDetachTimer = null;
                }

                _connectionSwitch.SetCheckedSilent(true);
                _connectionSwitch.Enabled = true;
                _resetBtn.Enabled = true;
                _revLimiter.Start();
                _connectionStateLabel.Text = Localization.T("status.connected");
                _connectionStateLabel.ForeColor = ApplePalette.StatusOk;

                _hud.InvalidateCache();

                if (_showHudCheck.Checked)
                    _hud.InitInGameHUD();

                UpdateOverlayAttachment();

                _overlay.ShowConnectionNotification(
                    Localization.T("overlay.welcome"),
                    Localization.T("overlay.connected"),
                    OverlayColor1,
                    5.0);

                _sawOwnOutGaugeSinceConnect = false;
                _sawAnyOutGaugeSinceConnect = false;
                var parkedCheckTimer = new System.Windows.Forms.Timer { Interval = ParkedCheckDelayMs };
                parkedCheckTimer.Tick += (ts, te) =>
                {
                    parkedCheckTimer.Stop();
                    parkedCheckTimer.Dispose();
                    if (!_localPlayerParked && _myOwnPlid != 0 &&
                        _sawAnyOutGaugeSinceConnect && !_sawOwnOutGaugeSinceConnect)
                        EnterParkedState();
                };
                parkedCheckTimer.Start();

                if (_inGameSettingsOverlay.Visible)
                    RefreshInGameSettingsOverlayValues();

            });
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            PostToUi(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _resetBtn.Enabled = false;
                _revLimiter.Stop();
                _connectionStateLabel.Text = Localization.T("status.disconnected");
                _connectionStateLabel.ForeColor = ApplePalette.StatusIdle;
                _speedKmh = 0; _driftAngle = 0;

                _angleValueLabel.Text = "0°";

                _overlay.ShowConnectionNotification(
                    Localization.T("overlay.goodbye"),
                    Localization.T("overlay.disconnected"),
                    OverlayColor4,
                    5.0);
                _overlayDetachTimer?.Stop();
                _overlayDetachTimer?.Dispose();
                _overlayDetachTimer = new System.Windows.Forms.Timer { Interval = OverlayDetachDelayMs };
                _overlayDetachTimer.Tick += (ts, te) =>
                {
                    _overlayDetachTimer?.Stop();
                    _overlayDetachTimer?.Dispose();
                    _overlayDetachTimer = null;
                    _overlay.Detach();
                };
                _overlayDetachTimer.Start();

                _overlay.ResetOutGaugeData();
                _overlay.ResetCarData();
                _controllerAssistState.Reset();
                _outGaugeFreshness.Reset();
                _drift.SetOutGaugeDataFresh(false);
                _hud.InvalidateCache();
                _localPlayerParked = false;
                _localPlayerNickAtPark = null;
                _lastKnownPlayerPLID = 0;

                if (_inGameSettingsOverlay.Visible)
                    RefreshInGameSettingsOverlayValues();
            });
        }

        private void OnConnectFailed(object? sender, string message)
        {
            PostToUi(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _connectionSwitch.Enabled = true;
                _connectionStateLabel.Text = Localization.T("status.disconnected");
                _connectionStateLabel.ForeColor = ApplePalette.StatusIdle;

                if (_inGameSettingsOverlay.Visible)
                    RefreshInGameSettingsOverlayValues();
            });
        }

        private void UpdateLapContextLabel()
        {
            string track = _insim.CurrentTrack;
            string layout = _insim.CurrentLayout;

            string label = string.IsNullOrEmpty(layout) ? track : $"{track}_{layout}";
            _overlay.UpdateLapContextLabel(label);
        }
        private void UpdateScoreLabels()
        {
            _scoreValueLabel.Text = _drift.TotalScore.ToString("N0");
            _runValueLabel.Text = _drift.IsSpeeding
                ? _drift.CurrentRunPoints.ToString("N0") : "—";
            _comboValueLabel.Text = $"x{_drift.ComboMultiplier}";
            _comboValueLabel.ForeColor = Color.FromArgb(255, 60, 60);

            if (_driverInfoLabel != null)
                _driverInfoLabel.Text = string.Format(Localization.T("score.driver"), _drift.CurrentDriver);

            UpdateBestStatsLabels();
        }

        private void UpdateBestStatsLabels()
        {
            if (_bestRunValueLabel == null) return;

            _bestRunValueLabel.Text = _drift.BestRunScore.ToString("N0");
            _bestDriftValueLabel.Text = $"{_drift.BestDriftDurationMs / 1000.0:F1}s";
            _bestDeepDriftValueLabel.Text = $"{_drift.BestDeepDriftDurationMs / 1000.0:F1}s";
        }

        private const int CollisionTooStrongTier = 4;
        private const double CollisionKissAngleToleranceDeg = 15.0;
        private const double CollisionKissSpeedToleranceKmh = 15.0;
        private const long CollisionKissBonusPoints = 150;
        private const long CollisionPenaltyPoints = 250;

        private void OnCollisionDetected(int tier, byte otherPlid)
        {
            string tierName = Localization.T($"derby.levels.tier{tier}");
            string driverName = _insim.GetPlayerName(otherPlid) ?? $"#{otherPlid}";
            string hitDesc = $"{tierName} ({driverName})";

            if (!_drift.IsDrifting)
            {
                _overlay.ShowBonus(hitDesc);
                return;
            }

            double angleAtContact = _drift.DriftAngleDeg;
            double speedAtContact = _drift.SpeedKmh;

            var timer = new System.Windows.Forms.Timer { Interval = DriftCollisionResolveDelayMs };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                ResolveDriftCollision(tier, tierName, driverName, angleAtContact, speedAtContact);
            };
            timer.Start();
        }

        private void ResolveDriftCollision(int tier, string tierName, string driverName, double angleAtContact, double speedAtContact)
        {
            bool stillStable = _drift.IsDrifting
                && Math.Abs(_drift.DriftAngleDeg - angleAtContact) <= CollisionKissAngleToleranceDeg
                && Math.Abs(_drift.SpeedKmh - speedAtContact) <= CollisionKissSpeedToleranceKmh
                && tier < CollisionTooStrongTier;

            if (stillStable)
            {
                _drift.ApplyCollisionPoints(CollisionKissBonusPoints,
                    string.Format(Localization.T("collision.car_kiss"), driverName, CollisionKissBonusPoints));
            }
            else
            {
                _drift.ApplyCollisionPoints(-CollisionPenaltyPoints,
                    string.Format(Localization.T("collision.car_penalty"), driverName, tierName, CollisionPenaltyPoints));
                _drift.RegisterCollisionPenalty();
            }

            PushObjectHitOverlayState();
        }

        private MinimalNumericUpDown MakeNumericUpDown(
        Control parent,
        int x,
        int y,
        int w,
        int h,
        decimal min,
        decimal max,
        decimal value,
        decimal increment = 1,
        Action<decimal>? onValueChanged = null,
        HorizontalAlignment textAlign = HorizontalAlignment.Right)
        {
            var control = new MinimalNumericUpDown(textAlign)
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                Minimum = min,
                Maximum = max,
                Increment = increment,
                Value = value,
            };

            if (onValueChanged != null)
                control.ValueChanged += onValueChanged;

            parent.Controls.Add(control);
            return control;
        }
        private readonly List<(Control ctrl, string key)> _localizedControls = new();

        private Label MakeLabel(
        Control parent,
        string text,
        int x,
        int y,
        int w,
        int h,
        Color? fore = null,
        Font? font = null,
        ContentAlignment align = ContentAlignment.MiddleLeft,
        bool secondary = false,
        string? locKey = null)
        {
            var l = new Label
            {
                Text = locKey != null ? Localization.T(locKey) : text,
                Location = new Point(x, y),
                Size = new Size(w, h),

                BackColor = parent is RoundedPanel ? parent.BackColor : Color.Transparent,

                ForeColor = fore ?? (secondary ? ApplePalette.Secondary : ApplePalette.Text),

                Font = font ?? (secondary
                    ? UiFonts.Get("Segoe UI", 8.5f, FontStyle.Regular)
                    : UiFonts.Get("Segoe UI Semibold", 9.5f, FontStyle.Regular)),

                TextAlign = align,

                AutoEllipsis = true,
                UseCompatibleTextRendering = true
            };

            if (parent is RoundedPanel)
                _cardBackedLabels.Add(l);

            parent.Controls.Add(l);

            if (locKey != null) _localizedControls.Add((l, locKey));
            return l;
        }

        private TextBox MakeTextBox(
        Control parent,
        string text,
        int x,
        int y,
        int w,
        int h,
        HorizontalAlignment textAlign = HorizontalAlignment.Right)
        {
            var tb = new TextBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),

                BorderStyle = BorderStyle.None,

                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,

                Font = UiFonts.Get("Segoe UI", 10f),

                Padding = new Padding(10),

                TextAlign = textAlign
            };

            var wrapper = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ApplePalette.Card,
                Tag = "theme:card"
            };

            parent.Controls.Add(wrapper);
            wrapper.Controls.Add(tb);

            tb.Location = new Point(10, 7);
            tb.Width = w - 20;
            tb.Height = h - 14;

            wrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(0, 0, wrapper.Width - 1, wrapper.Height - 1);

                using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, 10))
                using (Pen border = new Pen(ApplePalette.Border))
                {
                    e.Graphics.DrawPath(border, path);
                }
            };

            tb.GotFocus += (s, e) =>
            {
                wrapper.Invalidate();
                wrapper.BackColor = DrawingHelpers.Lighten(ApplePalette.Card, 0.02);
            };

            tb.LostFocus += (s, e) =>
            {
                wrapper.Invalidate();
                wrapper.BackColor = ApplePalette.Card;
            };

            parent.Controls.Add(wrapper);

            return tb;
        }
        private class CaptionButton : Control
        {
            public enum Kind { Minimize, Close }
            public Kind ButtonKind { get; }

            private bool _isHover = false;
            private bool _isDown = false;

            public CaptionButton(Kind kind)
            {
                ButtonKind = kind;
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor,
                    true);

                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                Size = new Size(46, TitleBarHeight);
                TabStop = false;
            }

            protected override void OnMouseEnter(EventArgs e) { _isHover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _isHover = false; _isDown = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _isDown = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _isDown = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var parentBrush = new SolidBrush(Parent?.BackColor ?? ApplePalette.Card))
                    g.FillRectangle(parentBrush, ClientRectangle);

                Color hoverFill = ButtonKind == Kind.Close
                    ? Color.FromArgb(232, 17, 35)
                    : (ApplePalette.IsDark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0));

                if (_isHover)
                {
                    Color fill = _isDown ? DrawingHelpers.Darken(hoverFill, 0.15) : hoverFill;
                    using (var b = new SolidBrush(fill))
                        g.FillRectangle(b, ClientRectangle);
                }

                Color glyphColor = (_isHover && ButtonKind == Kind.Close)
                    ? Color.White
                    : ApplePalette.Text;

                using (var pen = new Pen(glyphColor, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    int cx = Width / 2;
                    int cy = Height / 2;

                    if (ButtonKind == Kind.Minimize)
                    {
                        g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                    }
                    else
                    {
                        g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                    }
                }
            }

        }

        private class SmoothButton : Button
        {
            public SmoothButton()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);
            }
        }

        private Button MakeButton(
        Control parent,
        string text,
        int x,
        int y,
        int w,
        int h,
        Color? bg = null,
        string? locKey = null)
        {
            Color baseColor = bg ?? ApplePalette.Blue;

            var btn = new SmoothButton
            {
                Text = locKey != null ? Localization.T(locKey) : text,
                Location = new Point(x, y),
                Size = new Size(w, h),

                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.White,

                Font = UiFonts.Get("Segoe UI Semibold", 10f),
                Cursor = Cursors.Hand,
                TabStop = false,

                Tag = baseColor
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;

            bool isHover = false;
            bool isDown = false;
            const int margin = 3;
            const int radius = 10;

            btn.Paint += (s, e) =>
            {
                Color currentColor = btn.Enabled
                    ? (Color)btn.Tag
                    : (_isDarkTheme ? Color.FromArgb(58, 58, 62) : Color.FromArgb(212, 212, 216));

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle full = new Rectangle(0, 0, btn.Width, btn.Height);
                Rectangle rect = Rectangle.Inflate(full, -margin, -margin);

                using (SolidBrush parentBrush = new SolidBrush(parent.BackColor))
                    g.FillRectangle(parentBrush, full);

                if (isHover && btn.Enabled)
                {
                    for (int i = 5; i >= 1; i--)
                    {
                        using (GraphicsPath glowPath = DrawingHelpers.RoundedPath(Rectangle.Inflate(rect, i, i), radius + i))
                        using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(14, currentColor)))
                        {
                            g.FillPath(glowBrush, glowPath);
                        }
                    }
                }

                Color fill = isDown ? DrawingHelpers.Darken(currentColor, 0.10)
                            : isHover ? DrawingHelpers.Lighten(currentColor, 0.10)
                            : currentColor;

                using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, radius))
                {
                    using (var gradient = new LinearGradientBrush(
                               rect, DrawingHelpers.Lighten(fill, 0.06), DrawingHelpers.Darken(fill, 0.04), 90f))
                    {
                        g.FillPath(gradient, path);
                    }

                    using (Pen border = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                        g.DrawPath(border, path);
                }

                Color textColor = btn.Enabled
                    ? btn.ForeColor
                    : (_isDarkTheme ? Color.FromArgb(120, 120, 126) : Color.FromArgb(150, 150, 155));

                TextRenderer.DrawText(
                    g,
                    btn.Text,
                    btn.Font,
                    rect,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            };

            btn.MouseEnter += (s, e) => { isHover = true; btn.Invalidate(); };
            btn.MouseLeave += (s, e) => { isHover = false; isDown = false; btn.Invalidate(); };
            btn.MouseDown += (s, e) => { isDown = true; btn.Invalidate(); };
            btn.MouseUp += (s, e) => { isDown = false; btn.Invalidate(); };

            parent.Controls.Add(btn);
            if (locKey != null) _localizedControls.Add((btn, locKey));
            return btn;
        }

        private Color GetOverlayColorSlot(int slot) => slot switch
        {
            0 => OverlayColorIdle,
            1 => OverlayColor1,
            2 => OverlayColor2,
            3 => OverlayColor3,
            4 => OverlayColor4,
            5 => OverlayColor5,
            6 => OverlayColor6,
            _ => OverlayColor1
        };

        private string InSimColorForTier(int tier) => tier switch
        {
            2 => InSimColor2,
            3 => InSimColor3,
            4 => InSimColor4,
            5 => InSimColor5,
            6 => InSimColor6,
            _ => InSimColor1
        };

        private void SetOverlayColorSlot(int slot, Color c)
        {
            switch (slot)
            {
                case 0: OverlayColorIdle = c; break;
                case 1: OverlayColor1 = c; break;
                case 2: OverlayColor2 = c; break;
                case 3: OverlayColor3 = c; break;
                case 4: OverlayColor4 = c; break;
                case 5: OverlayColor5 = c; break;
                case 6: OverlayColor6 = c; break;
            }
            RefreshHudPreview();
        }

        private void SetInSimColorSlot(int slot, string code)
        {
            Color c = InSimCodeToColor(code);
            switch (slot)
            {
                case 0: InSimColorIdle = code; OverlayColorIdle = c; break;
                case 1: InSimColor1 = code; OverlayColor1 = c; break;
                case 2: InSimColor2 = code; OverlayColor2 = c; break;
                case 3: InSimColor3 = code; OverlayColor3 = c; break;
                case 4: InSimColor4 = code; OverlayColor4 = c; break;
                case 5: InSimColor5 = code; OverlayColor5 = c; break;
                case 6: InSimColor6 = code; OverlayColor6 = c; break;
            }
            SyncHudColors();
            RefreshHudPreview();
        }

        private void SyncHudColors()
        {
            if (_hud == null) return;
            _hud.InSimColorIdle = InSimColorIdle;
            _hud.InSimColor1 = InSimColor1;
            _hud.InSimColor2 = InSimColor2;
            _hud.InSimColor3 = InSimColor3;
            _hud.InSimColor4 = InSimColor4;
            _hud.InSimColor5 = InSimColor5;
            _hud.InSimColor6 = InSimColor6;
        }

        private void RefreshColorButtonSwatches()
        {
            bool useOverlayColors = _showOverlayCheck != null && _showOverlayCheck.Checked
                                  && (_showHudCheck == null || !_showHudCheck.Checked);

            if (_colorBtn1 == null) return;

            if (_colorBtnIdle != null)
                SetButtonColor(_colorBtnIdle, useOverlayColors ? OverlayColorIdle : InSimCodeToColor(InSimColorIdle));
            SetButtonColor(_colorBtn1, useOverlayColors ? OverlayColor1 : InSimCodeToColor(InSimColor1));
            if (_colorBtn2 != null) SetButtonColor(_colorBtn2, useOverlayColors ? OverlayColor2 : InSimCodeToColor(InSimColor2));
            if (_colorBtn3 != null) SetButtonColor(_colorBtn3, useOverlayColors ? OverlayColor3 : InSimCodeToColor(InSimColor3));
            if (_colorBtn4 != null) SetButtonColor(_colorBtn4, useOverlayColors ? OverlayColor4 : InSimCodeToColor(InSimColor4));
            if (_colorBtn5 != null) SetButtonColor(_colorBtn5, useOverlayColors ? OverlayColor5 : InSimCodeToColor(InSimColor5));
            if (_colorBtn6 != null)
                SetButtonColor(_colorBtn6, useOverlayColors ? OverlayColor6 : InSimCodeToColor(InSimColor6));
        }

        private void OpenCustomColorPicker(Button targetBtn, Color initialColor, string title, Action<Color> onApply, bool includeAlpha = false, Form? ownerForm = null)
        {
            Color initial = initialColor;

            int popupHeight = includeAlpha ? 320 : 275;

            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 300, popupHeight, out _);

            MakeLabel(card, title, 20, 15, 240, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));

            var preview = new Panel
            {
                Location = new Point(20, 55),
                Size = new Size(56, 56),
                BackColor = Color.Transparent
            };
            preview.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = DrawingHelpers.RoundedPath(new Rectangle(0, 0, preview.Width - 1, preview.Height - 1), 12);

                using (var checkerBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
                    e.Graphics.FillPath(checkerBrush, path);
                using (var checkerBrush2 = new SolidBrush(Color.FromArgb(200, 200, 200)))
                {
                    for (int cy2 = 0; cy2 < preview.Height; cy2 += 8)
                        for (int cx2 = 0; cx2 < preview.Width; cx2 += 8)
                            if (((cx2 / 8) + (cy2 / 8)) % 2 == 0)
                                e.Graphics.FillRectangle(checkerBrush2, cx2, cy2, 8, 8);
                }

                using var brush = new SolidBrush((Color)preview.Tag);
                e.Graphics.FillPath(brush, path);
                using var border = new Pen(ApplePalette.Border);
                e.Graphics.DrawPath(border, path);
            };
            preview.Tag = initial;
            preview.Region = CreateSmoothRoundedRegion(preview.Width, preview.Height, 12);
            card.Controls.Add(preview);

            int sliderX = 92, sliderW = 175, rowY = 58, rowGap = 34;

            MacSlider? sliderR = null, sliderG = null, sliderB = null;
            MacSlider? sliderA = null;
            Label? valR = null, valG = null, valB = null;
            Label? valA = null;

            MakeLabel(card, "R", sliderX, rowY, 16, 20, ApplePalette.Text);
            sliderR = new MacSlider { Location = new Point(sliderX + 18, rowY + 2), Size = new Size(sliderW - 30, 20), Minimum = 0, Maximum = 255, FillColor = Color.FromArgb(255, 70, 70) };
            sliderR.SetValueSilent(initial.R);
            valR = MakeLabel(card, initial.R.ToString(), sliderX + sliderW - 5, rowY, 30, 20, ApplePalette.Text);
            card.Controls.Add(sliderR);

            MakeLabel(card, "G", sliderX, rowY + rowGap, 16, 20, ApplePalette.Text);
            sliderG = new MacSlider { Location = new Point(sliderX + 18, rowY + rowGap + 2), Size = new Size(sliderW - 30, 20), Minimum = 0, Maximum = 255, FillColor = Color.FromArgb(70, 220, 90) };
            sliderG.SetValueSilent(initial.G);
            valG = MakeLabel(card, initial.G.ToString(), sliderX + sliderW - 5, rowY + rowGap, 30, 20, ApplePalette.Text);
            card.Controls.Add(sliderG);

            MakeLabel(card, "B", sliderX, rowY + rowGap * 2, 16, 20, ApplePalette.Text);
            sliderB = new MacSlider { Location = new Point(sliderX + 18, rowY + rowGap * 2 + 2), Size = new Size(sliderW - 30, 20), Minimum = 0, Maximum = 255, FillColor = Color.FromArgb(70, 140, 255) };
            sliderB.SetValueSilent(initial.B);
            valB = MakeLabel(card, initial.B.ToString(), sliderX + sliderW - 5, rowY + rowGap * 2, 30, 20, ApplePalette.Text);
            card.Controls.Add(sliderB);

            if (includeAlpha)
            {
                MakeLabel(card, "A", sliderX, rowY + rowGap * 3, 16, 20, ApplePalette.Text);
                sliderA = new MacSlider { Location = new Point(sliderX + 18, rowY + rowGap * 3 + 2), Size = new Size(sliderW - 30, 20), Minimum = 0, Maximum = 255, FillColor = Color.FromArgb(190, 190, 195) };
                sliderA.SetValueSilent(initial.A);
                valA = MakeLabel(card, initial.A.ToString(), sliderX + sliderW - 5, rowY + rowGap * 3, 30, 20, ApplePalette.Text);
                card.Controls.Add(sliderA);
            }

            void ApplyLive()
            {
                int a = includeAlpha ? sliderA!.Value : 255;
                Color c = Color.FromArgb(a, sliderR.Value, sliderG.Value, sliderB.Value);
                preview.Tag = c;
                preview.Invalidate();
                valR.Text = sliderR.Value.ToString();
                valG.Text = sliderG.Value.ToString();
                valB.Text = sliderB.Value.ToString();
                if (includeAlpha) valA!.Text = sliderA!.Value.ToString();

                SetButtonColor(targetBtn, c);
                onApply(c);
            }

            sliderR.ValueChanged += (s, e) => ApplyLive();
            sliderG.ValueChanged += (s, e) => ApplyLive();
            sliderB.ValueChanged += (s, e) => ApplyLive();
            if (includeAlpha) sliderA!.ValueChanged += (s, e) => ApplyLive();

            var defaultColors = new[]
            {
        Color.White,
        Color.Cyan,
        Color.Yellow,
        Color.Magenta,
        Color.Red
    };

            int presetSize = 34, presetGap = 8;
            int presetRowWidth = defaultColors.Length * presetSize + (defaultColors.Length - 1) * presetGap;
            int presetStartX = 20 + (260 - presetRowWidth) / 2;
            int presetY = includeAlpha ? 202 : 168;

            int px = presetStartX;
            foreach (var dc in defaultColors)
            {
                var swatch = new Button
                {
                    Size = new Size(presetSize, presetSize),
                    Location = new Point(px, presetY),
                    BackColor = dc,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Text = ""
                };
                swatch.FlatAppearance.BorderSize = 0;
                swatch.Region = CreateSmoothRoundedRegion(swatch.Width, swatch.Height, 10);

                var chosen = dc;
                swatch.Click += (s, e) =>
                {

                    sliderR.SetValueSilent(chosen.R);
                    sliderG.SetValueSilent(chosen.G);
                    sliderB.SetValueSilent(chosen.B);
                    ApplyLive();
                };

                swatch.MouseEnter += (s, e) => swatch.Size = new Size(presetSize + 4, presetSize + 4);
                swatch.MouseLeave += (s, e) => swatch.Size = new Size(presetSize, presetSize);

                card.Controls.Add(swatch);
                px += presetSize + presetGap;
            }

            var applyBtn = MakeButton(card, Localization.T("common.ok"), 20, includeAlpha ? 252 : 218, 260, 30, ApplePalette.Blue);
            applyBtn.Click += (s, e) => { SaveSettings(); popup.Close(); };

            ShowModalPopup(popup, ownerForm);
        }

        private void HandleColorButtonClick(Button btn, int slot, Form? ownerForm = null)
        {
            bool overlayOn = _showOverlayCheck != null && _showOverlayCheck.Checked;
            bool insimHudOn = _showHudCheck != null && _showHudCheck.Checked;

            if (overlayOn && !insimHudOn)
            {
                OpenCustomColorPicker(btn, GetOverlayColorSlot(slot), Localization.T("hud.customcolor.title"), c =>
                {
                    SetOverlayColorSlot(slot, c);
                    if (!_isDrifting && !_isSpeeding)
                        _overlay.UpdateAccentColor(OverlayColorIdle);
                }, ownerForm: ownerForm);
            }
            else
            {
                OpenInSimPalette(btn, code =>
                {
                    SetInSimColorSlot(slot, code);
                    SaveSettings();
                    RefreshColorButtonSwatches();
                }, ownerForm);
            }
        }

        private void BuildTitleBar()
        {
            _titleBar = new Panel
            {
                Location = new Point(1, 1),
                Size = new Size(ClientSize.Width - 2, TitleBarHeight),
                BackColor = ApplePalette.Card,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _titleBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(ApplePalette.Border, 1.6f))
                    e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
            };

            _titleBarLabel = new Label
            {
                Text = "Live For Speed - Drift Tools",
                Location = new Point(14, 0),
                Size = new Size(300, TitleBarHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ApplePalette.Title,
                Font = UiFonts.Get("Segoe UI Semibold", 9.5f),
                BackColor = Color.Transparent
            };

            _btnMinimize = new CaptionButton(CaptionButton.Kind.Minimize);
            _btnClose = new CaptionButton(CaptionButton.Kind.Close);

            _btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _btnClose.Click += (s, e) => Close();

            _titleBar.Resize += (s, e) => LayoutTitleBarButtons();

            _titleBar.Controls.Add(_titleBarLabel);
            _titleBar.Controls.Add(_btnMinimize);
            _titleBar.Controls.Add(_btnClose);

            LayoutTitleBarButtons();
            _titleBar.MouseDown += TitleBar_MouseDown;
            _titleBarLabel.MouseDown += TitleBar_MouseDown;

            Controls.Add(_titleBar);
            _titleBar.BringToFront();
        }

        private void LayoutTitleBarButtons()
        {
            int btnHeight = _titleBar.Height - 1;

            _btnClose.Size = new Size(_btnClose.Width, btnHeight);
            _btnClose.Location = new Point(_titleBar.Width - _btnClose.Width, 0);

            _btnMinimize.Size = new Size(_btnMinimize.Width, btnHeight);
            _btnMinimize.Location = new Point(_btnClose.Left - _btnMinimize.Width, 0);
        }

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private Color InSimCodeToColor(string code)
        {
            return code switch
            {
                "^0" => Color.Black,
                "^1" => Color.Red,
                "^2" => Color.LimeGreen,
                "^3" => Color.Yellow,
                "^4" => Color.Blue,
                "^5" => Color.Magenta,
                "^6" => Color.Cyan,
                "^7" => Color.White,
                "^8" => Color.Gray,
                "^9" => Color.LightBlue,
                _ => Color.White
            };
        }

        private void SetButtonColor(Button btn, Color color)
        {
            btn.Tag = color;
            btn.Invalidate();
        }

        private StartupCoverForm? _startupCover;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _startupCover = new StartupCoverForm(Bounds, ApplePalette.Background);
            _startupCover.Show();
            _startupCover.SetAlpha(255);
            _startupCover.ForceTopmost();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;

            PostToUi(CheckLfsCfgAgainstSettings);

            if (_startupCover == null) return;

            _startupCover.Bounds = Bounds;
            _startupCover.SetAlpha(255);

            var fadeTimer = new System.Windows.Forms.Timer { Interval = StartupFadeTickMs };
            int ticks = 0;
            fadeTimer.Tick += (s, e2) =>
            {
                ticks++;
                if (ticks <= 2)
                    return;

                int alpha = Math.Max(0, 255 - (ticks - 2) * 30);
                _startupCover.SetAlpha(alpha);

                if (alpha <= 0)
                {
                    fadeTimer.Stop();
                    fadeTimer.Dispose();
                    _startupCover.Close();
                    _startupCover.Dispose();
                    _startupCover = null;
                }
            };
            fadeTimer.Start();
        }

        private class StartupCoverForm : LayeredForm
        {
            private Bitmap? _bmp;
            private readonly Color _bgColor;

            public StartupCoverForm(Rectangle bounds, Color bgColor)
            {
                _bgColor = bgColor;
                Bounds = bounds;
            }

            public void SetAlpha(int alpha)
            {
                _bmp?.Dispose();
                _bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(_bmp))
                using (var brush = new SolidBrush(Color.FromArgb(alpha, _bgColor)))
                {
                    g.FillRectangle(brush, 0, 0, _bmp.Width, _bmp.Height);
                }
                SetBitmap(_bmp);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                _bmp?.Dispose();
                base.OnFormClosed(e);
            }
        }

        private static void ShutdownStep(Action step)
        {
            try { step(); }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ShutdownStep(PadCloak.Unhide);

            ShutdownStep(() =>
            {
                _settingsSaveTimer?.Stop();
                _settingsSaveTimer?.Dispose();
                _settingsSaveTimer = null;
            });
            ShutdownStep(SaveSettingsNow);
            ShutdownStep(_drift.FlushStats);

            ShutdownStep(() => { _outGaugeFreshnessPoll.Stop(); _outGaugeFreshnessPoll.Dispose(); });
            ShutdownStep(() => { _controllerAssistDisplayTimer.Stop(); _controllerAssistDisplayTimer.Dispose(); });
            ShutdownStep(() => { _lfsNoticeTimer?.Stop(); _lfsNoticeTimer?.Dispose(); _lfsNoticeTimer = null; });
            ShutdownStep(() => { _overlayDetachTimer?.Stop(); _overlayDetachTimer?.Dispose(); _overlayDetachTimer = null; });
            ShutdownStep(() => _lfsNoticeForm?.Close());
            ShutdownStep(() => Localization.LanguageChanged -= ApplyLanguage);

            ShutdownStep(() =>
            {
                if (_insim.IsConnected)
                {
                    _insim.DeleteAllButtons();
                    System.Threading.Thread.Sleep(ShutdownButtonsSettleMs);
                }
            });

            ShutdownStep(_insim.Dispose);
            ShutdownStep(_outGauge.Dispose);
            ShutdownStep(_outSim.Dispose);
            ShutdownStep(() => _assistLoop?.Dispose());
            ShutdownStep(_virtualPad.Dispose);
            ShutdownStep(_revLimiter.Dispose);
            ShutdownStep(_globalHotkey.Dispose);
            ShutdownStep(() => _settingsOverlayHotkey?.Dispose());
            ShutdownStep(() => _indicators?.Dispose());
            ShutdownStep(() => _inGameSettingsOverlay?.RestoreAndHide());
            ShutdownStep(() => _inGameSettingsOverlay?.Dispose());
            ShutdownStep(() => _wheelInput?.Dispose());
            ShutdownStep(() => _overlay?.Dispose());
            ShutdownStep(() => _hud?.Dispose());
            ShutdownStep(() => _shadow?.Close());
            ShutdownStep(() => _revLimiterCalibrationPrompt?.Close());
            ShutdownStep(() => _speedoBackgroundImage?.Dispose());
            ShutdownStep(_indicatorClickOn.Dispose);
            ShutdownStep(_indicatorClickOff.Dispose);
            ShutdownStep(_indicatorCancel.Dispose);

            base.OnFormClosing(e);
        }
    }

}
