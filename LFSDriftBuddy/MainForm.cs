using LFSDriftBuddy.InSim;
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
using static LFSDriftBuddy.DriftEngine;
using static LFSDriftBuddy.IndicatorManager;
using System.Reflection;

namespace LFSDriftBuddy
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

        private readonly SoundPlayer _indicatorClickOn =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click_on.wav"));

        private readonly SoundPlayer _indicatorClickOff =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click_off.wav"));

        private readonly SoundPlayer _indicatorCancel =
            new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_cancel.wav"));

        private string _missingIndicatorSoundsWarning = null;

        // Assigned partway through the constructor (see BuildUI/InGameHudManager setup), but
        // referenced by name from event-subscription lambdas set up earlier in the same
        // constructor — those lambdas only ever RUN once InSim is actually connected, long after
        // construction finishes, so this is genuinely always non-null by the time it's used.
        private OverlayForm _overlay = null!;

        private InSimConnection _insim;

        private OutGaugeConnection _outGauge = new OutGaugeConnection();

        private DriftEngine _drift = new DriftEngine();
        private IndicatorManager _indicators = new IndicatorManager();
        private byte _lastKnownPlayerPLID = 0;

        private double _speedKmh = 0;
        private double _driftAngle = 0;
        private long _runPoints = 0;
        private long _totalScore = 0;
        private double _combo = 1;
        private bool _isDrifting = false;
        private bool _isSpeeding = false;
        private bool _isBurnout = false;
        private string _driftLabel = "";
        private string _indicatorLabelR = "";
        private string _indicatorLabelL = "";
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
        private bool _wasOutGaugeFresh = true;

        private void OnOutGaugeFreshnessPollTick(object sender, EventArgs e)
        {
            bool fresh = _outGaugeFreshness.IsFresh;
            if (fresh == _wasOutGaugeFresh) return;
            _wasOutGaugeFresh = fresh;

            if (_outGaugeStateLabel != null)
            {
                _outGaugeStateLabel.Text = Localization.T(fresh ? "status.outgauge.connected" : "status.outgauge.disconnected");
                _outGaugeStateLabel.ForeColor = fresh ? Color.FromArgb(60, 220, 100) : Color.FromArgb(130, 130, 165);
            }

            if (fresh) return;

            _drift.SetOutGaugeDataFresh(false);
            _drift.DeactivateLapCounting();
            _overlay.SetLapBoxVisible(false);
            _overlay.UpdateAccentColor(OverlayColorIdle);

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
            _indicatorSoundsCheck.Enabled = available;
            _indicatorSoundsCheck.SetCheckedSilent(available && _indicatorSoundsCheckedBeforeMasterOff);
        }

        private void SyncRevLimiterHudAvailability()
        {
            if (_showRPMHudCheck == null) return;
            bool available = (_hudMasterEnabledCheck?.Checked ?? true) && _outGaugeConnectionEnabled;
            _showRPMHudCheck.Enabled = available;
            _showRPMHudCheck.SetCheckedSilent(available && _showRPMHudCheckedBeforeMasterOff);
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

                _advancedOutGaugeCheck?.SetCheckedSilent(_advancedOutGaugeCheckedBeforeOutGaugeOff);
                SyncAdvancedOutGaugeAvailability();
                SyncIndicatorSoundsAvailability();
                SyncRevLimiterHudAvailability();

                _revEnableSwitch.Enabled = true;
                _calibrateBtn1.Enabled = true;
                _revLimiterNumeric.Enabled = true;
                _revCutMS.Enabled = true;
                revBindingsBtn.Enabled = true;
                _revAutoCalibrateNewCarCheck.Enabled = true;
                _revLimiter.Enabled = _revEnabledBeforeOutGaugeOff;
                _revEnableSwitch.SetCheckedSilent(_revEnabledBeforeOutGaugeOff);
                if (!_revLimiter.IsRunning)
                    _revLimiter.Start();
            }
            else
            {
                _outGauge.Stop();
                _outGaugeConnectionEnabled = false;
                SyncAdvancedOutGaugeAvailability();
                SyncIndicatorSoundsAvailability();
                SyncRevLimiterHudAvailability();

                if (_advancedOutGaugeCheck != null)
                {
                    _advancedOutGaugeCheckedBeforeOutGaugeOff = _advancedOutGaugeCheck.Checked;
                    _advancedOutGaugeCheck.SetCheckedSilent(false);
                }

                _revEnabledBeforeOutGaugeOff = _revEnableSwitch.Checked;
                _revLimiter.Enabled = false;
                _revEnableSwitch.SetCheckedSilent(false);
                _revEnableSwitch.Enabled = false;
                _calibrateBtn1.Enabled = false;
                _revLimiterNumeric.Enabled = false;
                _revCutMS.Enabled = false;
                revBindingsBtn.Enabled = false;
                _revAutoCalibrateNewCarCheck.Enabled = false;
            }
        }

        private const int TitleBarHeight = 40;
        private Panel _titleBar;
        private Label _titleBarLabel;
        private CaptionButton _btnMinimize;
        private CaptionButton _btnClose;

        private Label _rpmLabel = null!;
        private Label _revCutLabel = null!;
        private Label _revLimitLabel = null!;
        private MinimalNumericUpDown _revLimiterNumeric = null!;
        private MinimalNumericUpDown _revCutMS = null!;
        private MinimalNumericUpDown _minDriftSpeedNumeric = null!;
        private MinimalNumericUpDown _maxBurnoutSpeedNumeric = null!;

        private MacProgressBar _rpmBar = null!;

        // Same story as _overlay above — constructed after BuildUI() but only ever read from
        // event lambdas that fire well after construction finishes.
        private InGameHudManager _hud = null!;

        private Label _angleValueLabel = null!;
        private Label _comboValueLabel;
        private Label _scoreValueLabel;
        private Label _runValueLabel;
        private Label _bestRunValueLabel;
        private Label _bestDriftValueLabel;
        private Label _bestDeepDriftValueLabel;
        private Label _driverInfoLabel;
        private Label _vehicleInfoLabel;
        private NoWheelRichTextBox _statusLabel;
        private TextBox _hostBox, _adminBox;
        private MinimalNumericUpDown _portBox;
        private Button _resetBtn;
        private MacToggleSwitch _revEnableSwitch;

        private bool _revEnabledBeforeOutGaugeOff = true;

        private MacToggleSwitch _revAutoCalibrateNewCarCheck;
        private MacToggleSwitch _connectionSwitch;

        private MacToggleSwitch _outGaugeConnectionSwitch;
        private MinimalNumericUpDown _outGaugePortBox;
        private MacToggleSwitch _themeSwitch;
        private Label _connectionStateLabel;

        private Label _outGaugeStateLabel;
        private MacToggleSwitch _showHudCheck = null!;
        private MacToggleSwitch _showRPMHudCheck = null!;
        private MacToggleSwitch _showOverlayCheck;

        private MacToggleSwitch _hudMasterEnabledCheck;
        private bool _lastHudStyleWasOverlay = true;

        private bool _showRPMHudCheckedBeforeMasterOff = false;

        private ForzaHudPreviewControl _hudPreview;

        private MacToggleSwitch _advancedOutGaugeCheck;

        private bool _advancedOutGaugeCheckedBeforeOutGaugeOff = true;

        private MacToggleSwitch _speedoTachoEnabledCheck;
        private MinimalNumericUpDown _speedoTachoOffsetXNumeric;
        private MinimalNumericUpDown _speedoTachoOffsetYNumeric;
        private MinimalNumericUpDown _speedoTachoScaleNumeric;
        private MacToggleSwitch _speedUnitToggle;
        private bool _useMph = false;

        private SpeedoTachoPreviewControl _speedoTachoPreview;

        private Color _speedoTachoRedlineColor = Color.Red;
        private Color _speedoTachoTextColor = Color.White;
        private Color _speedoTachoIndicatorColor = Color.FromArgb(255, 225, 225, 230);
        private Color _speedoTachoTickColor = Color.FromArgb(255, 215, 215, 218);
        private Color _speedoTachoBackgroundColor = Color.FromArgb(50, 15, 15, 20);

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

        private Button _colorBtn1;
        private Button _colorBtn2;
        private Button _colorBtn3;
        private Button _colorBtn4;
        private Button _colorBtn5;
        private Button _colorBtn6;
        private Button _colorBtnIdle;
        private Button _calibrateBtn1;
        private Button revBindingsBtn;
        private Button _langBtn;
        private Label _indicatorStatusLabel;
        private DashboardLampIcon _dashLeftLamp = null!;
        private DashboardLampIcon _dashRightLamp = null!;
        private DashboardLampIcon _dashHighBeamLamp = null!;
        private MacToggleSwitch _indicatorSoundsCheck;
        private MacToggleSwitch _indicatorAutoCancelCheck;
        private MacSlider _indicatorVolumeSlider;
        private Label _indicatorVolumeValueLabel;
        private int _indicatorSoundsVolume = 100;

        private MacToggleSwitch _indicatorsMasterEnabledCheck;
        private Button _wheelSetupBtn;

        private bool _indicatorSoundsCheckedBeforeMasterOff = true;
        private bool _indicatorAutoCancelCheckedBeforeMasterOff = true;

        private MinimalNumericUpDown _indicatorArmThresholdNumeric;
        private int _indicatorArmThresholdPct = 25;

        private MinimalNumericUpDown _indicatorCenterThresholdNumeric;
        private int _indicatorCenterThresholdPct = 5;

        private Button _bindLeftBtn, _bindRightBtn, _bindHazardBtn, _bindLightBtn;

        private Label _indicatorDisplayLabel;

        private Label _wheelAngleLabel;

        #region UI

        private NoScrollBarPanel _mainScrollPanel;

        private MinimalScrollbar _mainScrollbar;

        private RoundedPanel headerPanel;

        private RoundedPanel speedometerPanel;

        private RoundedPanel scorePanel;

        private RoundedPanel connectionPanel;
        private RoundedPanel hudPanel;

        private RoundedPanel revLimiterPanel;
        private RoundedPanel indicatorPanel;

        private RoundedPanel statusPanel;

        private readonly CollisionDetector _collisions = new();
        private MacToggleSwitch _collisionDetectCheck = null!;
        private MacToggleSwitch _objectCollisionDetectCheck = null!;
        private bool _objectCollisionDetectionEnabled = true;
        #endregion

        private WindowShadow _shadow;

        public MainForm()
        {

            _insim = new InSimConnection();

            _insim.CarDataReceived += OnCarData;
            _insim.StatusChanged += OnStatus;
            _insim.Connected += OnConnected;
            _insim.Disconnected += OnDisconnected;
            _insim.ConnectFailed += OnConnectFailed;
            _insim.RaceStateChanged += (s, inRace) => BeginInvoke((Action)(() =>
            {
                if (inRace)
                {
                    _overlay.SetMenuMode(false);
                    _hud.InitInGameHUD();
                }
                else
                {
                    _insim.DeleteAllButtons();
                    _hud.InvalidateCache();
                    _drift.DeactivateLapCounting();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetMenuMode(true);
                    _overlay.SetLapBoxVisible(false);
                }
            }));

            _insim.LapCompleted += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.OnLapCompleted();
                    _overlay.UpdateBestLapScore(_drift.BestLapScore);
                    if (_drift.LastLapScore >= 1000)
                    { _overlay.UpdateLastLapScore(_drift.LastLapScore); }

                    _overlay.ShowLapResult(_drift.LastLapScore);
                    _overlay.SetLapBoxVisible(true);
                }
            }));

            _insim.TrackChanged += (s, track) => BeginInvoke((Action)(() =>
            {
                _drift.SetTrack(track);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            }));

            _insim.LayoutChanged += (s, layout) => BeginInvoke((Action)(() =>
            {
                _drift.SetLayout(layout);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            }));
            _insim.CarReset += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {

                    LoadVehicleRevSettings(_currentCarName);
                }
            }));

            _insim.PitLaneEntered += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(false);
                }
            }));

            _insim.PitLaneExited += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(true);
                    LoadVehicleRevSettings(_currentCarName);
                }
            }));

            _insim.RaceRestarted += (s, e) => BeginInvoke((Action)(() =>
            {
                _drift.OnRaceRestarted();
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.SetLapBoxVisible(false);
            }));

            _insim.PlayerPitted += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(false);
                }
            }));

            _insim.CheckpointCrossed += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID != _lastKnownPlayerPLID) return;
                if (e.CheckpointIndex != 1) return;

                if (_drift.ActivateLapCounting())
                {
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(true);
                    UpdateLapContextLabel();
                }

            }));

            _insim.RestrictedAreaEntered += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID != _lastKnownPlayerPLID) return;

                _drift.DeactivateLapCounting();
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.SetLapBoxVisible(false);
            }));

            _insim.ObjectHit += (s, e) => BeginInvoke((Action)(() =>
            {
                if (!_objectCollisionDetectionEnabled) return;
                if (e.PLID != _lastKnownPlayerPLID) return;
                HandleObjectHit(e.ObjectName);
            }));

            _insim.PlayerNamed += (s, e) => BeginInvoke((Action)(() => TryApplyInSimDriverName(e.PLID)));
            _insim.CarContact += (s, e) => BeginInvoke((Action)(() =>
                _collisions.ProcessContact(e, _lastKnownPlayerPLID)));

            _drift.SetDriver(Environment.UserName);
            _drift.Error += msg => AppendStatusMessage(msg, StatusErrorColor);
            _collisions.ContactDetected += (tier, hitTypeKey) => BeginInvoke((Action)(() => OnCollisionDetected(tier, hitTypeKey)));
            _outGauge.DataReceived += OnRevData;
            _outGauge.Error += msg => AppendStatusMessage(msg, StatusErrorColor);

            _revLimiter.CutStarted += () => BeginInvoke((Action)(() =>
            {
                _revCutLabel.Text = Localization.T("rev.cut");
                _overlay.SetRevCut(true);
            }));
            _revLimiter.CutEnded += () => BeginInvoke((Action)(() =>
            {
                _revCutLabel.Text = "";
                _overlay.SetRevCut(false);
            }));
            _revLimiter.Error += msg => AppendStatusMessage(msg, StatusErrorColor);

            try
            {
                _indicatorClickOn.LoadAsync();
                _indicatorClickOff.LoadAsync();
                _indicatorCancel.LoadAsync();

                string soundsDir = Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds");
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

            InitializeComponent();

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

            UpdateBestStatsLabels();

            if (!string.IsNullOrEmpty(_missingIndicatorSoundsWarning))
                AppendStatusMessage(_missingIndicatorSoundsWarning);

            UpdateIndicatorDiagnosticsLabel();

        }

        private SteeringWheelInput _wheelInput;
        private GlobalHotkey _globalHotkey;
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

        private Form CreateDimOverlay()
        {
            return new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
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
                Font = new Font("Segoe UI Semibold", 12f),
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

        private DialogResult ShowModalPopup(Form popup)
        {
            Form overlayBg = CreateDimOverlay();
            overlayBg.Show();
            popup.Owner = overlayBg;
            try { return popup.ShowDialog(overlayBg); }
            finally { overlayBg.Close(); overlayBg.Dispose(); }
        }

        private static T LoadJsonOrDefault<T>(string path) where T : new()
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    T obj = JsonSerializer.Deserialize<T>(json);
                    if (obj != null) return obj;
                }
            }
            catch { }
            return new T();
        }

        private static void WriteJson<T>(string path, T obj)
        {
            string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private void LoadSettings()
        {
            try
            {
                Directory.CreateDirectory(ConfigsDir);

                bool anyConfigExists = File.Exists(SettingsFile) || File.Exists(ColorsFile) || File.Exists(RevLimiterFile);

                if (!anyConfigExists)
                {
                    ApplyTheme(true);
                    _isDarkTheme = true;
                    if (_themeSwitch != null)
                        _themeSwitch.SetCheckedSilent(_isDarkTheme);

                    _indicatorVolumeSlider.SetValueSilent(100);
                    _indicatorSoundsVolume = 100;
                    _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                    _indicatorSoundsCheck.Checked = true;
                    _indicatorAutoCancelCheck.Checked = true;

                    _overlay.RedlineColor = _speedoTachoRedlineColor;
                    _overlay.SpeedoTachoTextColor = _speedoTachoTextColor;
                    _overlay.SpeedoTachoIndicatorColor = _speedoTachoIndicatorColor;
                    _overlay.SpeedoTachoTickColor = _speedoTachoTickColor;
                    _overlay.SpeedoTachoBackgroundColor = _speedoTachoBackgroundColor;
                    UpdateSpeedUnitLabels();

                    RefreshColorButtonSwatches();
                    RefreshHudPreview();
                    SyncHudColors();

                    _outGaugeConnectionSwitch.SetCheckedSilent(true);
                    ApplyOutGaugeConnectionState(true);

                    return;
                }

                AppSettings settings = LoadJsonOrDefault<AppSettings>(SettingsFile);
                ColorSettings colors = LoadJsonOrDefault<ColorSettings>(ColorsFile);
                RevLimiterConfig revCfg = LoadJsonOrDefault<RevLimiterConfig>(RevLimiterFile);

                CalibratedMAXRPM = revCfg.CalibratedMAXRPM;
                SavedMSCUT = revCfg.SavedMSCUT;
                _revCutMS.Value = revCfg.SavedMSCUT;
                _revLimiter.RpmLimit = revCfg.CalibratedMAXRPM;
                _revLimiterNumeric.Value = CalibratedMAXRPM;

                _vehicleRevSettings = revCfg.VehicleRevLimiterSettings ?? new Dictionary<string, VehicleRevSettings>();
                _revAutoCalibrateNewCarCheck.Checked = revCfg.AutoCalibrateNewCar;

                _revLimiter.Enabled = revCfg.RevLimiterEnabled;
                _revEnableSwitch.SetCheckedSilent(revCfg.RevLimiterEnabled);
                _revEnabledBeforeOutGaugeOff = revCfg.RevLimiterEnabled;

                InSimColor1 = colors.InSimColor1;
                InSimColor2 = colors.InSimColor2;
                InSimColor3 = colors.InSimColor3;
                InSimColor4 = colors.InSimColor4;
                InSimColor5 = colors.InSimColor5;
                InSimColor6 = colors.InSimColor6 ?? "^2";
                InSimColorIdle = colors.InSimColorIdle ?? "^7";
                SyncHudColors();

                if (colors.OverlayColor1 != -1) OverlayColor1 = Color.FromArgb(colors.OverlayColor1);
                if (colors.OverlayColor2 != -1) OverlayColor2 = Color.FromArgb(colors.OverlayColor2);
                if (colors.OverlayColor3 != -1) OverlayColor3 = Color.FromArgb(colors.OverlayColor3);
                if (colors.OverlayColor4 != -1) OverlayColor4 = Color.FromArgb(colors.OverlayColor4);
                if (colors.OverlayColor5 != -1) OverlayColor5 = Color.FromArgb(colors.OverlayColor5);
                if (colors.OverlayColor6 != -1) OverlayColor6 = Color.FromArgb(colors.OverlayColor6);
                if (colors.OverlayColorIdle != -1) OverlayColorIdle = Color.FromArgb(colors.OverlayColorIdle);
                RefreshHudPreview();

                ApplyTheme(settings.DarkTheme);
                if (_themeSwitch != null)
                    _themeSwitch.SetCheckedSilent(settings.DarkTheme);

                Localization.SetLanguage(
                    Enum.Parse<AppLanguage>(settings.Language));

                ApplyRevBinding(ref _revToggleBinding, revCfg.RevToggleBinding, ExecuteRevToggle);
                ApplyRevBinding(ref _revCalibrateBinding, revCfg.RevCalibrateBinding, ExecuteRevCalibrate);
                ApplyRevBinding(ref _revDecreaseBinding, revCfg.RevDecreaseBinding, ExecuteRevDecrease);
                ApplyRevBinding(ref _revIncreaseBinding, revCfg.RevIncreaseBinding, ExecuteRevIncrease);
                ApplyRevBinding(ref _lightToggleBinding, settings.LightToggleBinding, ExecuteLightToggle);

                ApplyIndicatorBinding(
                    settings.IndicatorLeftBinding,
                    code => _indicators.LeftKeyCode = code,
                    wb => _indicators.LeftWheelButton = wb,
                    _indicators.LeftWheelButton,
                    () => _indicators.ToggleLeft());

                ApplyIndicatorBinding(
                    settings.IndicatorRightBinding,
                    code => _indicators.RightKeyCode = code,
                    wb => _indicators.RightWheelButton = wb,
                    _indicators.RightWheelButton,
                    () => _indicators.ToggleRight());

                ApplyIndicatorBinding(
                    settings.IndicatorHazardBinding,
                    code => _indicators.HazardKeyCode = code,
                    wb => _indicators.HazardWheelButton = wb,
                    _indicators.HazardWheelButton,
                    () => _indicators.ToggleHazard());

                RefreshIndicatorBindButtonLabels();

                if (Guid.TryParse(settings.SteeringWheelDeviceGuid, out var savedGuid) && savedGuid != Guid.Empty)
                {
                    if (Enum.TryParse<JoystickOffset>(settings.SteeringWheelAxis, out var savedAxis))
                    {
                        _savedWheelGuid = savedGuid;
                        _savedWheelAxis = savedAxis;
                        _wheelInput.ConnectToDevice(savedGuid, savedAxis);
                    }
                }

                try
                {
                    _indicatorSoundsVolume = Math.Clamp(settings.IndicatorSoundsVolume, 0, 100);
                    _indicatorVolumeSlider.SetValueSilent(_indicatorSoundsVolume);
                    _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                    _indicatorSoundsCheckedBeforeMasterOff = settings.IndicatorSoundsEnabled;
                    _indicatorAutoCancelCheck.Checked = settings.IndicatorAutoCancelOnCenter;
                    _indicatorArmThresholdPct = Math.Clamp(settings.IndicatorArmThresholdPct, 1, 100);
                    _indicatorArmThresholdNumeric.Value = _indicatorArmThresholdPct;
                    _indicatorCenterThresholdPct = Math.Clamp(settings.IndicatorCenterThresholdPct, 1, 90);
                    _indicatorCenterThresholdNumeric.Value = _indicatorCenterThresholdPct;

                    _speedoTachoEnabledCheck.Checked = settings.SpeedoTachoEnabled;
                    _speedoTachoOffsetXNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetX,
                        _speedoTachoOffsetXNumeric.Minimum, _speedoTachoOffsetXNumeric.Maximum);
                    _speedoTachoOffsetYNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetY,
                        _speedoTachoOffsetYNumeric.Minimum, _speedoTachoOffsetYNumeric.Maximum);
                    _speedoTachoScaleNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoScale,
                        _speedoTachoScaleNumeric.Minimum, _speedoTachoScaleNumeric.Maximum);
                    _speedoTachoRedlineColor = Color.FromArgb(colors.SpeedoTachoRedlineColor);
                    _speedoTachoTextColor = Color.FromArgb(colors.SpeedoTachoTextColor);
                    _speedoTachoIndicatorColor = Color.FromArgb(colors.SpeedoTachoIndicatorColor);
                    _speedoTachoTickColor = Color.FromArgb(colors.SpeedoTachoTickColor);
                    _speedoTachoBackgroundColor = Color.FromArgb(colors.SpeedoTachoBackgroundColor);

                    _overlay.SpeedoTachoEnabled = settings.SpeedoTachoEnabled;
                    _overlay.SpeedoTachoOffsetX = settings.SpeedoTachoOffsetX;
                    _overlay.SpeedoTachoOffsetY = settings.SpeedoTachoOffsetY;
                    _overlay.SpeedoTachoScale = settings.SpeedoTachoScale;
                    _overlay.RedlineColor = _speedoTachoRedlineColor;
                    _overlay.SpeedoTachoTextColor = _speedoTachoTextColor;
                    _overlay.SpeedoTachoIndicatorColor = _speedoTachoIndicatorColor;
                    _overlay.SpeedoTachoTickColor = _speedoTachoTickColor;
                    _overlay.SpeedoTachoBackgroundColor = _speedoTachoBackgroundColor;

                    _useMph = settings.SpeedoTachoUseMph;
                    _overlay.SpeedoTachoUseMph = _useMph;
                    if (_speedUnitToggle != null) _speedUnitToggle.SetCheckedSilent(_useMph);
                    UpdateSpeedUnitLabels();

                    if (_advancedOutGaugeCheck != null)
                    {
                        _advancedOutGaugeCheck.SetCheckedSilent(settings.AdvancedOutGaugeEnabled);
                        _advancedOutGaugeCheckedBeforeOutGaugeOff = settings.AdvancedOutGaugeEnabled;
                    }

                    if (_showRPMHudCheck != null)
                    {
                        _showRPMHudCheck.SetCheckedSilent(settings.ShowRPMHudEnabled);
                        _showRPMHudCheckedBeforeMasterOff = settings.ShowRPMHudEnabled;
                    }

                    if (_indicatorsMasterEnabledCheck != null)
                    {
                        _indicatorsMasterEnabledCheck.Checked = settings.IndicatorsMasterEnabled;
                    }

                    _drift.SetAngleLevelThresholds(settings.AngleLevelThresholds);
                    _drift.SetSpeedLevelThresholds(settings.SpeedLevelThresholds);

                    _drift.SetMinDriftSpeedKmh(settings.MinDriftSpeedKmh);
                    _drift.SetMaxBurnoutSpeedKmh(settings.MaxBurnoutSpeedKmh);
                    if (_minDriftSpeedNumeric != null) _minDriftSpeedNumeric.Value = (decimal)settings.MinDriftSpeedKmh;
                    if (_maxBurnoutSpeedNumeric != null) _maxBurnoutSpeedNumeric.Value = (decimal)settings.MaxBurnoutSpeedKmh;

                    _collisions.SetHitLevelThresholds(settings.HitLevelThresholds);
                    if (_collisionDetectCheck != null)
                    {
                        _collisionDetectCheck.Checked = settings.CollisionDetectionEnabled;
                        _collisions.Enabled = settings.CollisionDetectionEnabled;
                    }

                    _objectCollisionDetectionEnabled = settings.ObjectCollisionDetectionEnabled;
                    if (_objectCollisionDetectCheck != null)
                        _objectCollisionDetectCheck.Checked = settings.ObjectCollisionDetectionEnabled;
                }
                catch { }

                _outGaugeConnectionSwitch.SetCheckedSilent(settings.OutGaugeConnectionEnabled);
                ApplyOutGaugeConnectionState(settings.OutGaugeConnectionEnabled);
            }
            catch
            {
            }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(ConfigsDir);

                CalibratedMAXRPM = (int)_revLimiterNumeric.Value;

                SavedMSCUT = (int)_revCutMS.Value;

                AppSettings settings = new AppSettings()
                {
                    Language = Localization.CurrentLanguage.ToString(),

                    DarkTheme = _isDarkTheme,

                    LightToggleBinding = _lightToggleBinding,
                    IndicatorLeftBinding = GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton),
                    IndicatorRightBinding = GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton),
                    IndicatorHazardBinding = GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton),

                    SteeringWheelDeviceGuid = _savedWheelGuid.ToString(),
                    SteeringWheelAxis = _savedWheelAxis.ToString(),

                    IndicatorSoundsEnabled = _indicatorSoundsCheckedBeforeMasterOff,
                    IndicatorSoundsVolume = _indicatorVolumeSlider.Value,
                    IndicatorAutoCancelOnCenter = _indicatorAutoCancelCheck.Checked,
                    IndicatorArmThresholdPct = _indicatorArmThresholdPct,
                    IndicatorCenterThresholdPct = _indicatorCenterThresholdPct,

                    SpeedoTachoEnabled = _speedoTachoEnabledCheck.Checked,
                    SpeedoTachoOffsetX = (float)_speedoTachoOffsetXNumeric.Value,
                    SpeedoTachoOffsetY = (float)_speedoTachoOffsetYNumeric.Value,
                    SpeedoTachoScale = (float)_speedoTachoScaleNumeric.Value,
                    SpeedoTachoUseMph = _useMph,

                    AdvancedOutGaugeEnabled = _advancedOutGaugeCheckedBeforeOutGaugeOff,

                    OutGaugeConnectionEnabled = _outGaugeConnectionSwitch?.Checked ?? false,

                    ShowRPMHudEnabled = _showRPMHudCheckedBeforeMasterOff,

                    IndicatorsMasterEnabled = _indicatorsMasterEnabledCheck.Checked,

                    AngleLevelThresholds = _drift.AngleLevelThresholds.ToArray(),
                    SpeedLevelThresholds = _drift.SpeedLevelThresholds.ToArray(),

                    MinDriftSpeedKmh = _drift.MinDriftSpeedKmh,
                    MaxBurnoutSpeedKmh = _drift.MaxBurnoutSpeedKmh,

                    CollisionDetectionEnabled = _collisionDetectCheck?.Checked ?? false,
                    HitLevelThresholds = _collisions.HitLevelThresholds.ToArray(),
                    ObjectCollisionDetectionEnabled = _objectCollisionDetectCheck?.Checked ?? true,
                };

                ColorSettings colors = new ColorSettings()
                {
                    InSimColor1 = InSimColor1,
                    InSimColor2 = InSimColor2,
                    InSimColor3 = InSimColor3,
                    InSimColor4 = InSimColor4,
                    InSimColor5 = InSimColor5,
                    InSimColor6 = InSimColor6,
                    InSimColorIdle = InSimColorIdle,

                    OverlayColor1 = OverlayColor1.ToArgb(),
                    OverlayColor2 = OverlayColor2.ToArgb(),
                    OverlayColor3 = OverlayColor3.ToArgb(),
                    OverlayColor4 = OverlayColor4.ToArgb(),
                    OverlayColor5 = OverlayColor5.ToArgb(),
                    OverlayColor6 = OverlayColor6.ToArgb(),
                    OverlayColorIdle = OverlayColorIdle.ToArgb(),

                    SpeedoTachoRedlineColor = _speedoTachoRedlineColor.ToArgb(),
                    SpeedoTachoTextColor = _speedoTachoTextColor.ToArgb(),
                    SpeedoTachoIndicatorColor = _speedoTachoIndicatorColor.ToArgb(),
                    SpeedoTachoTickColor = _speedoTachoTickColor.ToArgb(),
                    SpeedoTachoBackgroundColor = _speedoTachoBackgroundColor.ToArgb(),
                };

                RevLimiterConfig revCfg = new RevLimiterConfig()
                {
                    CalibratedMAXRPM = CalibratedMAXRPM,
                    SavedMSCUT = SavedMSCUT,
                    VehicleRevLimiterSettings = _vehicleRevSettings,
                    RevToggleBinding = _revToggleBinding,
                    RevCalibrateBinding = _revCalibrateBinding,
                    RevDecreaseBinding = _revDecreaseBinding,
                    RevIncreaseBinding = _revIncreaseBinding,
                    AutoCalibrateNewCar = _revAutoCalibrateNewCarCheck?.Checked ?? true,

                    RevLimiterEnabled = _revEnabledBeforeOutGaugeOff,
                };

                WriteJson(SettingsFile, settings);
                WriteJson(ColorsFile, colors);
                WriteJson(RevLimiterFile, revCfg);
            }
            catch
            {
            }
        }

        private const int DefaultVehicleMaxRpm = 9000;
        private const int DefaultVehicleCutMs = 25;

        private void LoadVehicleRevSettings(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName)) return;

            if (_vehicleInfoLabel != null)
                _vehicleInfoLabel.Text = string.Format(Localization.T("rev.vehicle"), carName);

            if (_vehicleRevSettings.TryGetValue(carName, out var vs))
            {
                ApplyVehicleRevValues(vs.MaxRpm, vs.CutMs,
                    string.Format(Localization.T("rev.loaded_for_car"), carName, vs.MaxRpm, vs.CutMs));
                return;
            }

            ApplyVehicleRevValues(DefaultVehicleMaxRpm, DefaultVehicleCutMs,
                string.Format(Localization.T("rev.no_saved_for_car"), carName, DefaultVehicleMaxRpm, DefaultVehicleCutMs));

            ShowRevLimiterCalibrationPromptIfNeeded(carName);
        }

        private void ApplyVehicleRevValues(int maxRpm, int cutMs, string statusMessage)
        {
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
    (_revToggleBinding.Kind == InputKind.Keyboard && _revToggleBinding.Key == Keys.Enter) ||
    (_revCalibrateBinding.Kind == InputKind.Keyboard && _revCalibrateBinding.Key == Keys.Enter) ||
    (_revDecreaseBinding.Kind == InputKind.Keyboard && _revDecreaseBinding.Key == Keys.Enter) ||
    (_revIncreaseBinding.Kind == InputKind.Keyboard && _revIncreaseBinding.Key == Keys.Enter) ||
    (_lightToggleBinding.Kind == InputKind.Keyboard && _lightToggleBinding.Key == Keys.Enter);

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
                _globalHotkey.SetBinding(Keys.Enter, () => BeginInvoke((Action)(() =>
                {
                    if (_revLimiterCalibrationPrompt != null && !calibrationON)
                        _ = RPMLimitterCalibrate();
                })));
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
                CutMs = (int)_revCutMS.Value
            };

            SaveSettings();
        }

        private void OnRevData(OutGaugeData data)
        {

            _revLimiter.ProcessOutGaugeData(data);

            BeginInvoke((Action)(() =>
            {

                _overlay.NotifyOutGaugeData();
                _outGaugeFreshness.Ping();

                if (data.PLID != 0 && data.PLID != _lastKnownPlayerPLID)
                {
                    _lastKnownPlayerPLID = data.PLID;
                    TryApplyInSimDriverName(data.PLID);
                    if (_insim.GetPlayerName(data.PLID) == null)
                        _insim.RequestPlayerList();
                }

                _drift.SetHandbrakeActive(data.HandbrakeOn);
                _rpmLabel.Text = ((int)data.RPM).ToString("N0");
                _overlay.UpdateRpm((int)data.RPM);
                _overlay.UpdateGear((int)data.Gear);
                _rpmBar.Value = (int)Math.Min(data.RPM, _rpmBar.Maximum);

                _rpmLabel.ForeColor = data.RPM >= _revLimiter.RpmLimit * 0.95
                    ? Color.FromArgb(255, 60, 60)
                    : Color.FromArgb(80, 220, 120);

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

                _hud.ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());

                _drift.UpdateEngineTelemetry(data.RPM, data.Throttle, (int)data.Gear);

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

            }));
        }

        private bool _isDarkTheme = true;

        private void ApplyTheme(bool dark)
        {
            _isDarkTheme = dark;
            ApplePalette.SetDark(dark);

            this.BackColor = ApplePalette.Background;
            if (_mainScrollPanel != null)
                _mainScrollPanel.BackColor = ApplePalette.Background;
            _mainScrollbar?.ApplyThemeColor(ApplePalette.Background);

            ApplyThemeRecursive(this);

            if (_titleBar != null)
            {
                _titleBar.BackColor = ApplePalette.Card;
                _titleBarLabel.ForeColor = ApplePalette.Title;
                _titleBar.Invalidate();
                _btnMinimize.Invalidate();
                _btnClose.Invalidate();
            }

            this.Invalidate(true);
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

                    case Label lbl:
                        RecolorLabel(lbl);
                        break;
                }

                if (c.HasChildren)
                    ApplyThemeRecursive(c);
            }
        }

        private void RecolorLabel(Label lbl)
        {

            if (lbl.Tag == null)
                lbl.Tag = lbl.ForeColor;

            if (!(lbl.Tag is Color original))
                return;

            if (!_isDarkTheme)
            {
                lbl.ForeColor = original;
                return;
            }

            double luma = (0.299 * original.R + 0.587 * original.G + 0.114 * original.B) / 255.0;
            lbl.ForeColor = luma < 0.55 ? DrawingHelpers.Lighten(original, 0.65) : original;
        }

        public bool indClickONOFF = false;

        private Color ButtonColor => _isDarkTheme
    ? Color.FromArgb(70, 70, 90)
    : Color.FromArgb(205, 205, 220);
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

            int maxWindowHeight = Screen.PrimaryScreen?.WorkingArea.Height ?? 1200;
            MaximumSize = new Size(890, maxWindowHeight);

            DoubleBuffered = true;

            BackColor = Color.Black;

            Font = new Font("Segoe UI", 10f);

            _mainScrollPanel = new NoScrollBarPanel
            {
                Location = new Point(1, 1),
                Size = new Size(ClientSize.Width - 2 - MinimalScrollbar.TrackWidth, ClientSize.Height - 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BackColor = BackColor,
            };
            Controls.Add(_mainScrollPanel);

            _mainScrollbar = new MinimalScrollbar();
            _mainScrollbar.AttachTo(_mainScrollPanel);
            Controls.Add(_mainScrollbar);

            ComputeGridLayout();

            _mainScrollPanel.AutoScrollMinSize = new Size(0, _gridContentHeight);
            _mainScrollbar.SyncToTarget();

            _wheelInput = new SteeringWheelInput(
                this.Handle,
                (0, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("0"); }))),
                (1, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("1"); }))),
                (2, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("2"); }))),
                (3, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("3"); }))),
                (4, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("4"); }))),
                (5, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("5"); }))),
                (6, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("6"); }))),
                (7, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("7"); }))),
                (8, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("8"); }))),
                (9, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("9"); }))),
                (10, () => BeginInvoke((Action)(() =>
                {
                    if (!_connectionSwitch.Checked)
                    {
                        _insim.Connect(_hostBox.Text.Trim(), (int)_portBox.Value, _adminBox.Text);
                    }

                }))),
                (11, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("11"); }))),
                (12, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("12"); }))),
                (13, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("13"); }))),
                (14, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("14"); }))),
                (15, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("15"); }))),
                (16, () => BeginInvoke((Action)(() => { _hud.ShowInGameAward("16"); })))

            );

            _wheelInput.WheelNotFound += devices =>
            BeginInvoke((Action)(() => ShowWheelSetupDialog(devices)));

            headerPanel = CreateCard(
                "",
                GridSlot.Header,
                420,
                170, locKey: "header.title");

            Color disconnectedColor = Color.FromArgb(130, 130, 165);
            _connectionStateLabel = MakeLabel(headerPanel, "", 200, 10, 200, 15, disconnectedColor, new Font("Segoe UI", 8f), ContentAlignment.MiddleRight, locKey: "status.disconnected");
            _connectionStateLabel.Tag = "status:live";
            _outGaugeStateLabel = MakeLabel(headerPanel, "", 200, 25, 200, 15, disconnectedColor, new Font("Segoe UI", 8f), ContentAlignment.MiddleRight, locKey: "status.outgauge.disconnected");
            _outGaugeStateLabel.Tag = "status:live";

            MakeLabel(headerPanel, "Dark Mode", 24, 60, 155, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.theme");
            MakeLabel(headerPanel, "Language", 24, 95, 155, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.language");
            MakeLabel(headerPanel, "App Version", 24, 130, 155, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "header.appversion");

            MakeLabel(headerPanel, appVersion, 200, 130, 200, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleRight);

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
                170, locKey: "connection.title");

            var connectionLabel = MakeLabel(connectionPanel, "STATUS", 300, 14, 55, 22,
            Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

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
                        BeginInvoke((Action)(() => _hud.InvalidateCache()));
                        System.Threading.Thread.Sleep(80);
                        _insim.Disconnect();

                        BeginInvoke((Action)(() =>
                        {
                            _connectionSwitch.Enabled = true;
                        }));
                    });
                }
            };

            connectionPanel.Controls.Add(_connectionSwitch);

            MakeLabel(connectionPanel, "Host", 24, 60, 155, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.host");
            MakeLabel(connectionPanel, "Port", 24, 95, 155, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.port");
            MakeLabel(connectionPanel, "Admin password", 24, 130, 220, 30,
            Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.adminpass");

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

            var outGaugeConnectionPanel = CreateCard(
                "OutGauge Connection",
                GridSlot.ConnectionOutGauge,
                420,
                100, locKey: "connection.outgauge.title");

            MakeLabel(outGaugeConnectionPanel, "STATUS", 300, 14, 55, 22,
                Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

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
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "connection.port");
            _outGaugePortBox = MakeNumericUpDown(
                outGaugeConnectionPanel,
                250,
                60,
                150,
                30,
                1,
                65535,
                _outGauge.UdpPort);

            //MakeLabel(outGaugeConnectionPanel, Localization.T("connection.outgauge.subtitle"), 24, 55, 380, 34,
            //    Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "connection.outgauge.subtitle");

            speedometerPanel = CreateCard(
                "Speedometer",
                GridSlot.Speedometer,
                420,
                450, locKey: "speedometer.title");

            MakeLabel(speedometerPanel, "STATUS", 300, 14, 55, 22,
                Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _speedoTachoEnabledCheck = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = true
            };
            _speedoTachoEnabledCheck.CheckedChanged += (s, e) =>
            {
                _overlay.SpeedoTachoEnabled = _speedoTachoEnabledCheck.Checked;
                SaveSettings();
            };
            speedometerPanel.Controls.Add(_speedoTachoEnabledCheck);
            _localizedControls.Add((_speedoTachoEnabledCheck, "speedometer.showhud"));

            MakeLabel(speedometerPanel, Localization.T("speedometer.preview"), 24, 48, 100, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.MiddleLeft, locKey: "speedometer.preview");
            _speedoTachoPreview = new SpeedoTachoPreviewControl
            {
                Location = new Point(110, 60),
                Size = new Size(200, 160)
            };
            speedometerPanel.Controls.Add(_speedoTachoPreview);

            MakeLabel(speedometerPanel, "Offset X:", 24, 268, 155, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.offsetx");
            _speedoTachoOffsetXNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 268, 150, 30, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetX = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Offset Y:", 24, 303, 155, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.offsety");
            _speedoTachoOffsetYNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 303, 150, 30, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetY = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Scale:", 24, 338, 155, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.scale");
            _speedoTachoScaleNumeric = MakeNumericUpDown(
                speedometerPanel, 250, 338, 150, 30, 0.3m, 2.5m, 1.0m, 0.05m,
                v => { _overlay.SpeedoTachoScale = (float)v; SaveSettings(); });
            _speedoTachoScaleNumeric.DecimalPlaces = 2;

            MakeLabel(speedometerPanel, Localization.T("speedometer.usemph"), 24, 378, 300, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "speedometer.usemph");
            _speedUnitToggle = new MacToggleSwitch
            {
                Location = new Point(355, 383),
                Size = new Size(40, 20),
                Checked = false
            };
            speedometerPanel.Controls.Add(_speedUnitToggle);

            MakeLabel(speedometerPanel, Localization.T("speedometer.usemph.subtitle"), 24, 410, 380, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "speedometer.usemph.subtitle");

            _speedUnitToggle.CheckedChanged += (s, e) =>
            {
                _useMph = _speedUnitToggle.Checked;
                _overlay.SpeedoTachoUseMph = _useMph;
                UpdateSpeedUnitLabels();
                SaveSettings();
            };
            UpdateSpeedUnitLabels();

            var hudColorsBtn = MakeButton(speedometerPanel, Localization.T("hud.colors"), 90, 230, 250, 32, ApplePalette.Blue);
            hudColorsBtn.Click += (s, e) => ShowHudColorsMenu();
            _localizedControls.Add((hudColorsBtn, "hud.colors"));

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
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 8.5f));

            MakeLabel(scorePanel, Localization.T("hud.advanced_outgauge"), 24, 225, 300, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.advanced_outgauge");

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

                _advancedOutGaugeCheckedBeforeOutGaugeOff = _advancedOutGaugeCheck.Checked;
                SaveSettings();
            };
            scorePanel.Controls.Add(_advancedOutGaugeCheck);

            MakeLabel(scorePanel, Localization.T("hud.advanced_outgauge.subtitle"), 24, 255, 380, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.advanced_outgauge.subtitle");

            var driftLevelsBtn = MakeButton(scorePanel, "DRIFT LEVELS", 24, 190, 181, 30, ButtonColor, locKey: "score.levels.button");
            driftLevelsBtn.Click += (s, e) => ShowAngleLevelsMenu();

            var speedLevelsBtn = MakeButton(scorePanel, "SPEED LEVELS", 215, 190, 181, 30, ButtonColor, locKey: "score.levels.speed_button");
            speedLevelsBtn.Click += (s, e) => ShowSpeedLevelsMenu();

            const int scoreColW = 94;
            Label AddStatTile(string labelKey, int col, int row, Color valueColor, string initialValue = "0")
            {
                int x = 24 + col * scoreColW;
                int y = row == 0 ? 76 : 136;
                MakeLabel(scorePanel, "", x, y, scoreColW - 4, 14, ApplePalette.Text,
                    new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: labelKey);
                return MakeLabel(scorePanel, initialValue, x, y + 20, scoreColW - 4, 22,
                    valueColor, new Font("Segoe UI", 14f, FontStyle.Bold));
            }

            _scoreValueLabel = AddStatTile("score.total", 0, 0, Color.FromArgb(255, 200, 50));
            _runValueLabel = AddStatTile("score.run", 1, 0, Color.FromArgb(80, 220, 120));
            _comboValueLabel = AddStatTile("score.combo", 2, 0, Color.FromArgb(255, 120, 40), "x1");
            _angleValueLabel = AddStatTile("score.angle", 3, 0, ApplePalette.Text, "0°");

            _bestRunValueLabel = AddStatTile("score.bestrun", 0, 1, Color.FromArgb(255, 200, 50));
            _bestDriftValueLabel = AddStatTile("score.bestdrift", 1, 1, Color.FromArgb(60, 180, 255), "0.0s");
            _bestDeepDriftValueLabel = AddStatTile("score.bestdeepdrift", 2, 1, Color.FromArgb(255, 80, 80), "0.0s");

            MakeLabel(scorePanel, Localization.T("score.mindriftspeed"), 24, 296, 220, 22,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.mindriftspeed");
            _minDriftSpeedNumeric = MakeNumericUpDown(
                scorePanel, 270, 292, 90, 28, 0, 100, (decimal)_drift.MinDriftSpeedKmh, 1,
                v => { _drift.SetMinDriftSpeedKmh((double)v); SaveSettings(); });
            MakeLabel(scorePanel, "km/h", 366, 296, 60, 22, ApplePalette.Text, new Font("Segoe UI", 9f));
            MakeLabel(scorePanel, Localization.T("score.mindriftspeed.subtitle"), 24, 322, 372, 28,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.mindriftspeed.subtitle");

            MakeLabel(scorePanel, Localization.T("score.maxburnoutspeed"), 24, 358, 220, 22,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.maxburnoutspeed");
            _maxBurnoutSpeedNumeric = MakeNumericUpDown(
                scorePanel, 270, 344, 90, 28, 0, 150, (decimal)_drift.MaxBurnoutSpeedKmh, 1,
                v => { _drift.SetMaxBurnoutSpeedKmh((double)v); SaveSettings(); });
            MakeLabel(scorePanel, "km/h", 366, 348, 60, 22, ApplePalette.Text, new Font("Segoe UI", 9f));
            MakeLabel(scorePanel, Localization.T("score.maxburnoutspeed.subtitle"), 24, 374, 372, 28,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.maxburnoutspeed.subtitle");

            MakeLabel(scorePanel, Localization.T("score.collisiondetect"), 24, 400, 260, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.collisiondetect");
            _collisionDetectCheck = new MacToggleSwitch
            {
                Location = new Point(355, 405),
                Size = new Size(40, 20),
                Checked = false
            };
            _collisionDetectCheck.CheckedChanged += (s, e) =>
            {
                _collisions.Enabled = _collisionDetectCheck.Checked;
                SaveSettings();
            };
            scorePanel.Controls.Add(_collisionDetectCheck);
            MakeLabel(scorePanel, Localization.T("score.collisiondetect.subtitle"), 24, 430, 372, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.collisiondetect.subtitle");

            MakeLabel(scorePanel, Localization.T("score.objectcollisiondetect"), 24, 466, 260, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "score.objectcollisiondetect");
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
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "score.objectcollisiondetect.subtitle");

            var hitLevelsBtn = MakeButton(scorePanel, "HIT LEVELS", 24, 532, 372, 30, ButtonColor, locKey: "derby.levels.button");
            hitLevelsBtn.Click += (s, e) => ShowHitLevelsMenu();

            hudPanel = CreateCard(
                "Forza-like HUD Settings",
                GridSlot.Hud,
                420,
                450, locKey: "hud.title");

            MakeLabel(hudPanel, "STATUS", 300, 14, 55, 22,
                Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _hudMasterEnabledCheck = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = true
            };
            hudPanel.Controls.Add(_hudMasterEnabledCheck);
            _localizedControls.Add((_hudMasterEnabledCheck, "hud.master"));

            MakeLabel(hudPanel, Localization.T("hud.showoverlay"), 24, 228, 300, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.showoverlay");
            _showOverlayCheck = new MacToggleSwitch
            {
                Location = new Point(355, 233),
                Size = new Size(40, 20),
                Checked = true
            };
            hudPanel.Controls.Add(_showOverlayCheck);
            MakeLabel(hudPanel, Localization.T("hud.showoverlay.subtitle"), 24, 258, 380, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.showoverlay.subtitle");

            _showOverlayCheck.CheckedChanged += (s, e) =>
            {
                if (_showOverlayCheck.Checked)
                {
                    IntPtr lfsHwnd = FindLfsWindow();
                    if (lfsHwnd != IntPtr.Zero)
                    {
                        _overlay.UpdateScore(_drift.TotalScore);
                        _overlay.UpdateAccentColor(OverlayColorIdle);
                        _overlay.UpdateMaxRpm(CalibratedMAXRPM);
                        _overlay.AttachTo(lfsHwnd);
                    }
                    _showHudCheck.Checked = false;
                }
                else
                {
                    _overlay.Detach();
                }
                SaveSettings();
                RefreshColorButtonSwatches();
            };

            MakeLabel(hudPanel, Localization.T("hud.show"), 24, 298, 300, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.show");
            _showHudCheck = new MacToggleSwitch
            {
                Location = new Point(355, 303),
                Size = new Size(40, 20),
                Checked = false
            };
            hudPanel.Controls.Add(_showHudCheck);
            MakeLabel(hudPanel, Localization.T("hud.show.subtitle"), 24, 328, 380, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.show.subtitle");

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
                    if (_lastHudStyleWasOverlay) _showOverlayCheck.Checked = true;
                    else _showHudCheck.Checked = true;
                }
                else
                {
                    _lastHudStyleWasOverlay = _showOverlayCheck.Checked;
                    _showOverlayCheck.Checked = false;
                    _showHudCheck.Checked = false;
                }

                SyncRevLimiterHudAvailability();

                SaveSettings();
            };

            MakeLabel(hudPanel, Localization.T("hud.REVLimitter"), 24, 368, 300, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "hud.REVLimitter");
            _showRPMHudCheck = new MacToggleSwitch
            {
                Location = new Point(355, 373),
                Size = new Size(40, 20),
                Checked = false
            };
            _showRPMHudCheck.CheckedChanged += (s, e) =>
            {

                _showRPMHudCheckedBeforeMasterOff = _showRPMHudCheck.Checked;
                SaveSettings();
            };
            hudPanel.Controls.Add(_showRPMHudCheck);
            MakeLabel(hudPanel, Localization.T("hud.REVLimitter.subtitle"), 24, 398, 380, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "hud.REVLimitter.subtitle");

            var scoreColorsBtn = MakeButton(hudPanel, Localization.T("hud.scorecolors"), 90, 190, 250, 32, ApplePalette.Blue);
            scoreColorsBtn.Click += (s, e) => ShowScoreColorsMenu();
            _localizedControls.Add((scoreColorsBtn, "hud.scorecolors"));

            MakeLabel(hudPanel, Localization.T("hud.livepreview"), 24, 48, 300, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.MiddleLeft, locKey: "hud.livepreview");
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
            Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _revEnableSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = _revLimiter.Enabled
            };

            _revEnableSwitch.CheckedChanged += (s, e) =>
            {
                _revLimiter.Enabled = _revEnableSwitch.Checked;

                _revEnabledBeforeOutGaugeOff = _revEnableSwitch.Checked;

                if (!_revLimiter.IsRunning)
                    _revLimiter.Start();

                SaveSettings();
            };

            revLimiterPanel.Controls.Add(_revEnableSwitch);

            _vehicleInfoLabel = MakeLabel(revLimiterPanel, "---", 24, 53, 380, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 8.5f));

            revBindingsBtn = MakeButton(revLimiterPanel, "BIND FUNCTIONS", 250, 276, 150, 32, locKey: "rev.bindfunctions");
            revBindingsBtn.Click += (s, e) => OpenRevLimiterBindings();

            _globalHotkey = new GlobalHotkey();

            MakeLabel(revLimiterPanel, "RPM", 24, 80, 60, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.rpm");
            _rpmLabel = MakeLabel(revLimiterPanel, "0", 90, 75, 90, 36, null, new Font("Segoe UI", 18f, FontStyle.Bold), ContentAlignment.MiddleLeft);
            _revCutLabel = MakeLabel(revLimiterPanel, "", 190, 75, 50, 36,
                ApplePalette.Text, new Font("Segoe UI", 14f, FontStyle.Bold));

            _calibrateBtn1 = MakeButton(revLimiterPanel, "CALIBRATE", 250, 79, 150, 30, locKey: "rev.calibrate");

            _calibrateBtn1.Click += async (s, e) => await RPMLimitterCalibrate();

            _rpmBar = new MacProgressBar
            {
                Location = new Point(24, 120),
                Size = new Size(376, 18),
                Minimum = 0,
                Maximum = CalibratedMAXRPM,
                Value = 0
            };
            revLimiterPanel.Controls.Add(_rpmBar);

            _revLimitLabel = MakeLabel(revLimiterPanel, "RPM Limit", 24, 155, 220, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.limit");
            _revLimiterNumeric = MakeNumericUpDown(
                revLimiterPanel,
                250,
                155,
                150,
                30,
                500,
                20000,
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

            MakeLabel(revLimiterPanel, "Cut time [ms]", 24, 190, 220, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.cutms");
            _revCutMS = MakeNumericUpDown(
                revLimiterPanel,
                250,
                190,
                150,
                30,
                25,
                500,
                SavedMSCUT,
                5,
                v =>
                {
                    _revLimiter.CutMs = (int)v;
                    SaveVehicleRevSettings(_currentCarName);
                }
            );

            MakeLabel(revLimiterPanel, Localization.T("rev.autocalibrate"), 24, 228, 300, 22,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.autocalibrate");
            _revAutoCalibrateNewCarCheck = new MacToggleSwitch
            {
                Location = new Point(355, 224),
                Size = new Size(40, 20),
                Checked = true
            };
            revLimiterPanel.Controls.Add(_revAutoCalibrateNewCarCheck);
            _revAutoCalibrateNewCarCheck.CheckedChanged += (s, e) => SaveSettings();
            MakeLabel(revLimiterPanel, Localization.T("rev.autocalibrate.subtitle"), 24, 252, 380, 18,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "rev.autocalibrate.subtitle");

            MakeLabel(revLimiterPanel, "Bind revlimitter functions", 24, 282, 220, 26,
                      Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "rev.calibratehint");

            indicatorPanel = CreateCard(
               "Turn Signals",
               GridSlot.Indicators,
               850,
               460, locKey: "indicators.title");

            MakeLabel(indicatorPanel, "STATUS", 730, 14, 55, 22,
                Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

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
                _wheelSetupBtn.Enabled = on;
                _indicatorArmThresholdNumeric.Enabled = on;
                _indicatorCenterThresholdNumeric.Enabled = on;

                SyncIndicatorSoundsAvailability();

                _indicatorAutoCancelCheck.Enabled = on;
                if (!on)
                {
                    _indicatorAutoCancelCheckedBeforeMasterOff = _indicatorAutoCancelCheck.Checked;
                    _indicatorAutoCancelCheck.Checked = false;
                }
                else
                {
                    _indicatorAutoCancelCheck.Checked = _indicatorAutoCancelCheckedBeforeMasterOff;
                }

                SaveSettings();
            };
            indicatorPanel.Controls.Add(_indicatorsMasterEnabledCheck);
            _localizedControls.Add((_indicatorsMasterEnabledCheck, "indicators.master"));

            _wheelInput.Log += msg => AppendStatusMessage(msg);
            _wheelInput.SteeringChanged += pct => BeginInvoke((Action)(() =>
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

            }));

            Font groupHeaderFont = new Font("Segoe UI Semibold", 9f);
            Color groupHeaderColor = Color.FromArgb(90, 90, 110);

            const int indCol1X = 24, indCol2X = 450, indColW = 380;

            // ── Column 1: status/info, dashboard lamps, then key bindings ───────────
            const int bindColW = 190, bindColGap = 20;
            int bindCol2X = indCol1X + bindColW + bindColGap;

            int y1 = 54;
            MakeLabel(indicatorPanel, Localization.T("indicators.group.status"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.status");
            y1 += 20;

            MakeLabel(indicatorPanel, Localization.T("indicators.signal"), indCol1X, y1, 150, 16,
                ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), locKey: "indicators.signal");
            _indicatorDisplayLabel = MakeLabel(indicatorPanel, "---", indCol1X, y1 + 18, 150, 32,
                Color.FromArgb(100, 200, 255), new Font("Segoe UI", 16f, FontStyle.Bold));

            MakeLabel(indicatorPanel, "STEERING", bindCol2X, y1, 150, 16, ApplePalette.Text,
                new Font("Segoe UI", 7.5f, FontStyle.Bold), locKey: "indicators.steering");
            _wheelAngleLabel = MakeLabel(indicatorPanel, "0%", bindCol2X, y1 + 18, 150, 28,
                Color.FromArgb(255, 200, 50), new Font("Segoe UI", 14f, FontStyle.Bold));
            y1 += 58;

            MakeLabel(indicatorPanel, Localization.T("indicators.lights"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.lights");
            y1 += 20;
            MakeLabel(indicatorPanel, Localization.T("indicators.dash.subtitle"), indCol1X, y1, indColW, 24,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.dash.subtitle");
            y1 += 26;

            _dashLeftLamp = new DashboardLampIcon { Kind = DashLampKind.TurnLeft, Location = new Point(indCol1X, y1) };
            _dashHighBeamLamp = new DashboardLampIcon { Kind = DashLampKind.HighBeam, Location = new Point(indCol1X + 72, y1) };
            _dashRightLamp = new DashboardLampIcon { Kind = DashLampKind.TurnRight, Location = new Point(indCol1X + 144, y1) };
            indicatorPanel.Controls.Add(_dashLeftLamp);
            indicatorPanel.Controls.Add(_dashHighBeamLamp);
            indicatorPanel.Controls.Add(_dashRightLamp);
            y1 += 52;

            _indicatorStatusLabel = MakeLabel(indicatorPanel, "", indCol1X, y1, indColW, 50,
               Color.FromArgb(150, 200, 100), new Font("Segoe UI", 8f));
            y1 += 54;

            MakeLabel(indicatorPanel, Localization.T("indicators.keybind"), indCol1X, y1, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.keybind");
            y1 += 24;

            // 2×2 grid: each cell shows a readable action name above a button that always
            // displays the CURRENT binding (key or wheel/pad button) — not just a static action
            // label — so the assignment is visible at a glance instead of hiding behind a bare
            // "LEFT"/"RIGHT".
            int bindRow = 0;
            Button MakeBindRow(string bindNameKey, InputBinding current)
            {
                int col = bindRow % 2;
                int row = bindRow / 2;
                int x = col == 0 ? indCol1X : bindCol2X;
                int y = y1 + row * 50;

                MakeLabel(indicatorPanel, Localization.T(bindNameKey), x, y, bindColW, 14,
                    Color.FromArgb(60, 60, 60), new Font("Segoe UI", 8f, FontStyle.Bold), locKey: bindNameKey);
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

            _bindLeftBtn.Click += (s, e) => BindKey(Localization.T("indicators.bindname.left"), b =>
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

            _bindRightBtn.Click += (s, e) => BindKey(Localization.T("indicators.bindname.right"), b =>
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

            _bindHazardBtn.Click += (s, e) => BindKey(Localization.T("indicators.bindname.hazard"), b =>
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

            _bindLightBtn.Click += (s, e) => BindKey(Localization.T("indicators.bindname.lights"), b =>
            {
                ApplyRevBinding(ref _lightToggleBinding, b, ExecuteLightToggle);
                _bindLightBtn.Text = BindDisplay(b);
                SaveSettings();
            });

            // ── Column 2: steering wheel + auto-cancel-on-center settings ───────────
            int y2 = 64;


            MakeLabel(indicatorPanel, Localization.T("indicators.group.wheel"), indCol2X, y2, indColW, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.wheel");

            y2 += 20;

            _wheelSetupBtn = MakeButton(indicatorPanel, Localization.T("indicators.wheelsetup"), indCol2X, y2, indColW, 34,
                locKey: "indicators.wheelsetup");
            _wheelSetupBtn.Click += (s, e) => ShowWheelSetupDialog(_wheelInput.GetAvailableDevices());

            y2 += 34;

            MakeLabel(indicatorPanel, Localization.T("indicators.centeroff"), indCol2X, y2, indColW - 50, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.centeroff");
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
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.centeroff.subtitle");

            y2 += 34;

            MakeLabel(indicatorPanel, Localization.T("indicators.armthreshold"), indCol2X, y2, indColW - 90, 22,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.armthreshold");
            
           

            _indicatorArmThresholdNumeric = MakeNumericUpDown(
                indicatorPanel, indCol2X + indColW - 90, y2, 70, 28, 1, 100, _indicatorArmThresholdPct, 1,
                v => { _indicatorArmThresholdPct = (int)v; SaveSettings(); });

            MakeLabel(indicatorPanel, "%", indCol2X + indColW - 20, y2 + 4, 20, 22, ApplePalette.Text, new Font("Segoe UI", 10f));

            y2 += 30;

            MakeLabel(indicatorPanel, Localization.T("indicators.armthreshold.subtitle"), indCol2X, y2, indColW - 50, 28,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.armthreshold.subtitle");
            
            y2 += 34;

            MakeLabel(indicatorPanel, Localization.T("indicators.centerthreshold"), indCol2X, y2, indColW - 90, 22,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.centerthreshold");

            _indicatorCenterThresholdNumeric = MakeNumericUpDown(
                indicatorPanel, indCol2X + indColW - 90, y2, 70, 28, 1, 90, _indicatorCenterThresholdPct, 1,
                v => { _indicatorCenterThresholdPct = (int)v; SaveSettings(); });
            MakeLabel(indicatorPanel, "%", indCol2X + indColW - 20, y2 + 4, 20, 22, ApplePalette.Text, new Font("Segoe UI", 10f));

            y2 += 30;

            MakeLabel(indicatorPanel, Localization.T("indicators.centerthreshold.subtitle"), indCol2X, y2, indColW - 50, 30,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.centerthreshold.subtitle");

            // ── Column 3: sound settings ─────────────────────────────────────────────
            int y3 = 315;


            MakeLabel(indicatorPanel, Localization.T("indicators.group.sounds"), indCol2X, y3, indColW - 45, 16,
                groupHeaderColor, groupHeaderFont, ContentAlignment.MiddleLeft, locKey: "indicators.group.sounds");

            y3 += 20;

            MakeLabel(indicatorPanel, Localization.T("indicators.soundscheck"), indCol2X, y3, indColW - 45, 30,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.soundscheck");
            _indicatorSoundsCheck = new MacToggleSwitch
            {
                Location = new Point(indCol2X + indColW - 45, y3 + 6),
                Size = new Size(40, 20),
                Checked = true
            };
            _indicatorSoundsCheck.CheckedChanged += (s, e) =>
            {
                _indicatorSoundsCheckedBeforeMasterOff = _indicatorSoundsCheck.Checked;
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
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft, locKey: "indicators.soundscheck.subtitle");

            y3 += 32;

            MakeLabel(indicatorPanel, Localization.T("indicators.volume"), indCol2X, y3, 100, 18,
                Color.FromArgb(60, 60, 60), new Font("Segoe UI", 12f), ContentAlignment.MiddleLeft, locKey: "indicators.volume");

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

            statusPanel = CreateCard(
                "",
                GridSlot.Status,
                420,
                135);

            _statusLabel = new NoWheelRichTextBox
            {
                Location = new Point(20, 14),
                Size = new Size(385, 130),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Segoe UI", 8f),
                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,
                WordWrap = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            statusPanel.Controls.Add(_statusLabel);
            AppendStatusMessage(Localization.T("status.hint"));

            ResumeLayout();

            BuildTitleBar();

            this.Paint += MainForm_Paint;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {

        }
        private void MainForm_Paint(object sender, PaintEventArgs e)
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
        private void OpenLanguagePicker(Button anchorBtn)
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 240, 320, out _);

            MakeLabel(
                card,
                Localization.T("header.language"),
                20,
                15,
                150,
                28,
                ApplePalette.Title,
                new Font("Segoe UI Semibold", 11f));

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

            ShowModalPopup(popup);
        }

        private InputBinding _revToggleBinding = InputBinding.None;
        private InputBinding _revCalibrateBinding = InputBinding.None;
        private InputBinding _revDecreaseBinding = InputBinding.None;
        private InputBinding _revIncreaseBinding = InputBinding.None;

        private InputBinding _leftIndBinding = InputBinding.None;
        private InputBinding _rightIndBinding = InputBinding.None;
        private InputBinding _hazardIndBinding = InputBinding.None;
        private InputBinding _lightToggleBinding = InputBinding.None;

        private void ExecuteRevToggle()
        {
            if (!_insim.IsConnected) return;
            BeginInvoke((Action)(() => { _revEnableSwitch.Checked = !_revEnableSwitch.Checked; }));
        }

        private void ExecuteRevCalibrate()
        {
            if (!_insim.IsConnected) return;
            BeginInvoke((Action)(() =>
            {
                if (!calibrationON)
                    _ = RPMLimitterCalibrate();
            }));
        }

        private void ExecuteRevDecrease()
        {
            if (!_insim.IsConnected) return;
            BeginInvoke((Action)(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value - 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            }));
        }

        private void ExecuteRevIncrease()
        {
            if (!_insim.IsConnected) return;
            BeginInvoke((Action)(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value + 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            }));
        }

        private InputBinding GetIndicatorBinding(int keyCode, int? wheelButton)
        {
            if (wheelButton.HasValue)
                return InputBinding.FromWheelButton(wheelButton.Value);

            if (keyCode == (int)Keys.None)
                return InputBinding.None;

            return InputBinding.FromKey((Keys)keyCode);
        }

        // Bind buttons show the CURRENT binding as their text (see MakeBindRow in BuildUI) — at
        // construction time that already matches _indicators'/​_lightToggleBinding's constructed
        // defaults, but once LoadSettings overwrites them from disk the button text goes stale
        // until this re-syncs it.
        private void RefreshIndicatorBindButtonLabels()
        {
            if (_bindLeftBtn == null) return;

            string Display(InputBinding b) => b.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : b.ToString();

            _bindLeftBtn.Text = Display(GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton));
            _bindRightBtn.Text = Display(GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton));
            _bindHazardBtn.Text = Display(GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton));
            _bindLightBtn.Text = Display(_lightToggleBinding);
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
                // Explicit unbind (Delete in KeyBindingForm) — the wheel binding was already
                // removed above; also clear the keyboard code so KeyboardHook_OnKeyDown's
                // "keyCode == LeftKeyCode" check can never match a real keypress again.
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

            _hud.ShowInGameAward(Localization.T("rev.calibration_award.start"));
            _revLimiterCalibrationPrompt?.ShowCalibratingState();

            _revLimitLabel.Text = Localization.T("rev.calibrating");

            await Task.Delay(4000);

            calibrationON = false;

            CalibratedMAXRPM = CalibratedMAXRPM - 50;
            _revLimitLabel.Text = Localization.T("rev.limit");
            _revLimiterNumeric.Value = CalibratedMAXRPM;

            _revLimiter.Enabled = wasEnabledBeforeCalibration;
            _revEnableSwitch.SetCheckedSilent(wasEnabledBeforeCalibration);
            _hud.ShowInGameAward(Localization.T("rev.limit") + CalibratedMAXRPM);
            _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            _revLimiterCalibrationPrompt?.ShowDoneState(CalibratedMAXRPM);
            await Task.Delay(1000);

            _hud.ShowInGameAward(Localization.T("rev.calibration_award.done"));
            await Task.Delay(1000);
            _hud.ShowInGameAward(Localization.T("rev.limit") + CalibratedMAXRPM);
            await Task.Delay(1000);
            _hud.ShowInGameAward(Localization.T("rev.calibration_award.done"));

            await Task.Delay(3000);
            _hud.ShowInGameAward($"");
            _revLimitLabel.Text = Localization.T("rev.limit");
            SaveSettings();

            _revLimiterCalibrationPrompt?.Close();
            _revLimiterCalibrationPrompt = null;
        }

        private enum GridSlot { Header, ConnectionInSim, ConnectionOutGauge, Score, RevLimiter, Speedometer, Hud, Indicators, Status }

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
                (GridSlot.Score,              0, 420, 586),
                (GridSlot.Speedometer,        0, 420, 450),

                (GridSlot.ConnectionInSim,    1, 420, 170),
                (GridSlot.ConnectionOutGauge, 1, 420, 100),
                (GridSlot.RevLimiter,         1, 420, 330),
                (GridSlot.Hud,                1, 420, 450),
                (GridSlot.Status,             1, 420, 135),

                (GridSlot.Indicators,        -1, 850, 460),
              
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
            string locKey = null)
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
                SaveSettings();
                AppendStatusMessage(string.Format(Localization.T("wheelconfig.configured"), _wheelInput.DeviceName, _wheelInput.SteeringAxis));
            }
        }

        private void BindKey(string keyName, Action<InputBinding> onBound)
        {

            using var dialog = new KeyBindingForm(this, keyName, _wheelInput);
            if (ShowModalPopup(dialog) == DialogResult.OK)
            {
                InputBinding result = dialog.Result;

                bool isValidKeyboard = result.Kind == InputKind.Keyboard && result.Key != Keys.Escape;
                bool isValidWheel = result.Kind == InputKind.WheelButton;
                bool isUnbind = result.Kind == InputKind.None;

                if (isValidKeyboard || isValidWheel || isUnbind)
                {
                    onBound(result);
                    AppendStatusMessage(string.Format(
                        Localization.T("status.keybound"),
                        keyName,
                        isUnbind ? Localization.T("rev.bindings.unbound") : result.ToString()));
                }
            }
        }

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

            private Label[] _axisBars;
            private Label _calibHint;
            private bool _calibrating = false;
            private readonly Dictionary<JoystickOffset, int> _axisMin = new();
            private readonly Dictionary<JoystickOffset, int> _axisMax = new();
            private System.Windows.Forms.Timer _calibTimer;

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
                    ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

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
                    Font = new Font("Segoe UI", 9.5f),
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
                    var btn = _owner.MakeButton(_content, d.Name, 0, y, _content.Width, 34);

                    var guid = d.Guid;
                    btn.Click += (s, e) =>
                    {
                        SelectedDeviceGuid = guid;
                        ShowAxisCalibration();
                    };

                    y += 40;
                }

                var cancelBtn = _owner.MakeButton(_content, Localization.T("wheelconfig.abort"),
                    0, _content.Height - 34, _content.Width, 32);
                cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            }

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
                    Font = new Font("Segoe UI", 9.5f),
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
                    Text = Localization.T("wheelconfig.detection"),
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

                int mx = 0, my = y + 50;
                foreach (var axis in AxisChoices)
                {
                    var b = _owner.MakeButton(_content, axis.ToString(), mx, my, 105, 26);

                    var ax = axis;
                    b.Click += (s, e) => Confirm(ax);

                    mx += 112;
                    if (mx > 220) { mx = 0; my += 30; }
                }

                var backBtn = _owner.MakeButton(_content, Localization.T("wheelconfig.otherdevice"),
                    0, _content.Height - 30, _content.Width, 28);
                backBtn.Click += (s, e) => { StopCalibration(); ShowDeviceList(); };

                _axisMin.Clear();
                _axisMax.Clear();
                _calibrating = true;
                _wheelInput.RawAxesChanged += OnRawAxes;

                _calibTimer = new System.Windows.Forms.Timer { Interval = 10000 };
                _calibTimer.Tick += (s, e) =>
                {
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

                    if (bestRange > 2000)
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
            private readonly SteeringWheelInput _wheelInput;

            public KeyBindingForm(MainForm owner, string keyName, SteeringWheelInput wheelInput = null)
            {
                _owner = owner;
                _wheelInput = wheelInput;

                Text = Localization.T("keybind.dialog.title");
                StartPosition = FormStartPosition.CenterParent;
                KeyPreview = true;

                var card = _owner.BuildPopupChrome(this, 360, 180, out _);

                _owner.MakeLabel(card, Localization.T("keybind.dialog.title"), 20, 15, 260, 24,
                    ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

                _instructionLabel = _owner.MakeLabel(card, string.Format(Localization.T("keybind.dialog.press"), keyName),
                    20, 55, Width - 40, 80, ApplePalette.Text, new Font("Segoe UI", 11f), ContentAlignment.MiddleCenter);

                if (_wheelInput != null)
                    _wheelInput.AnyButtonPressed += WheelInput_AnyButtonPressed;

                FormClosed += (s, e) =>
                {
                    if (_wheelInput != null)
                        _wheelInput.AnyButtonPressed -= WheelInput_AnyButtonPressed;
                };
            }

            private void WheelInput_AnyButtonPressed(int buttonIndex)
            {

                Result = InputBinding.FromWheelButton(buttonIndex);

                _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.bound"), $"Wheel Btn {buttonIndex}");
                _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                System.Threading.Thread.Sleep(300);
                DialogResult = DialogResult.OK;
                Close();
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (msg.Msg == 0x0100)
                {
                    Keys baseKey = keyData & Keys.KeyCode;

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

                        System.Threading.Thread.Sleep(300);
                        DialogResult = DialogResult.OK;
                        Close();
                        return true;
                    }

                    Result = InputBinding.FromKey(baseKey);
                    _instructionLabel.Text = string.Format(Localization.T("keybind.dialog.bound"), baseKey);
                    _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                    System.Threading.Thread.Sleep(300);
                    DialogResult = DialogResult.OK;
                    Close();
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
                    ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

                string bodyTemplate = firstTimeForCar
                    ? Localization.T("rev.calibration_prompt.body")
                    : Localization.T("rev.calibration_prompt.manual_body");
                string bodyText = string.Format(bodyTemplate, carName)
                    + (enterHintAvailable ? Localization.T("rev.calibration_prompt.enter_hint") : "");

                _messageLabel = _owner.MakeLabel(card, bodyText, 20, 55, Width - 40, 74,
                    ApplePalette.Text, new Font("Segoe UI", 9.5f), ContentAlignment.TopLeft);

                _liveRpmLabel = _owner.MakeLabel(card, "", 20, 55 + 74, Width - 40, 34,
                    ApplePalette.Blue, new Font("Segoe UI Semibold", 20f, FontStyle.Bold), ContentAlignment.MiddleCenter);
                _liveRpmLabel.Visible = false;

                _calibrateButton = _owner.MakeButton(card, Localization.T("rev.calibrate"),
                    20, Height - 56, Width - 40, 36);
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

        private void ShowHudColorsMenu()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 300, 310, out _);

            MakeLabel(card, Localization.T("hud.colors"), 20, 15, 200, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("hud.colors.subtitle"), 20, 40, 250, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f));

            int y = 66;
            void AddRow(string labelKey, Func<Color> getColor, Action<Color> setColor)
            {
                string label = Localization.T(labelKey);
                MakeLabel(card, label, 20, y + 6, 150, 20, ApplePalette.Text);

                var swatch = MakeButton(card, "", 195, y, 85, 30, getColor());
                swatch.Click += (s, e) => OpenCustomColorPicker(swatch, getColor(), label, setColor, includeAlpha: true);
                card.Controls.Add(swatch);

                y += 40;
            }

            AddRow("hud.color.redline", () => _speedoTachoRedlineColor, c => { _speedoTachoRedlineColor = c; _overlay.RedlineColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.text", () => _speedoTachoTextColor, c => { _speedoTachoTextColor = c; _overlay.SpeedoTachoTextColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.indicator", () => _speedoTachoIndicatorColor, c => { _speedoTachoIndicatorColor = c; _overlay.SpeedoTachoIndicatorColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.ticks", () => _speedoTachoTickColor, c => { _speedoTachoTickColor = c; _overlay.SpeedoTachoTickColor = c; RefreshSpeedoPreview(); });
            AddRow("hud.color.background", () => _speedoTachoBackgroundColor, c => { _speedoTachoBackgroundColor = c; _overlay.SpeedoTachoBackgroundColor = c; RefreshSpeedoPreview(); });

            ShowModalPopup(popup);
        }

        private void ShowScoreColorsMenu()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 380, 400, out _);

            MakeLabel(card, Localization.T("hud.scorecolors"), 20, 15, 300, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("hud.scorecolors.subtitle"), 20, 40, 320, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f));

            int y = 66;
            Button AddColorRow(string label, Color initialColor, int slot)
            {
                MakeLabel(card, label, 20, y + 6, 260, 20, ApplePalette.Text);

                var swatch = MakeButton(card, "", 290, y, 70, 30, initialColor);
                swatch.Click += (s, e) => HandleColorButtonClick(swatch, slot);

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

            try { ShowModalPopup(popup); }
            finally { _colorBtnIdle = _colorBtn1 = _colorBtn2 = _colorBtn3 = _colorBtn4 = _colorBtn5 = _colorBtn6 = null; }
        }

        private void ShowAngleLevelsMenu()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 340, 320, out _);

            MakeLabel(card, Localization.T("score.levels.title"), 20, 15, 290, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("score.levels.angle_subtitle"), 20, 40, 300, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.angle_header"), 20, y, 290, 18,
                ApplePalette.Text, new Font("Segoe UI Semibold", 9f));
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

            ShowModalPopup(popup);
        }

        private void ShowSpeedLevelsMenu()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 340, 320, out _);

            MakeLabel(card, Localization.T("score.levels.speed_title"), 20, 15, 290, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("score.levels.speed_subtitle"), 20, 40, 300, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.speed_header"), 20, y, 290, 18,
                ApplePalette.Text, new Font("Segoe UI Semibold", 9f));
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

            ShowModalPopup(popup);
        }

        private void ShowHitLevelsMenu()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 340, 320, out _);

            MakeLabel(card, Localization.T("derby.levels.title"), 20, 15, 290, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("derby.levels.subtitle"), 20, 40, 300, 32,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f), ContentAlignment.TopLeft);

            int y = 78;

            MakeLabel(card, Localization.T("score.levels.speed_header"), 20, y, 290, 18,
                ApplePalette.Text, new Font("Segoe UI Semibold", 9f));
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

            ShowModalPopup(popup);
        }

        private void OpenRevLimiterBindings()
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 300, 320, out _);

            MakeLabel(card, Localization.T("rev.bindings.title"), 20, 15, 220, 28,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

            int y = 55;

            void MakeRow(string labelKey, InputBinding current, Action<InputBinding> onBound)
            {
                MakeLabel(card, Localization.T(labelKey), 20, y + 4, 120, 22, ApplePalette.Text);

                var bindBtn = MakeButton(
                    card,
                    current.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : current.ToString(),
                    170, y, 110, 28, ApplePalette.Blue);

                bindBtn.Click += (s, e) =>
                {
                    BindKey(Localization.T(labelKey), b =>
                    {
                        onBound(b);
                        bindBtn.Text = b.Kind == InputKind.None ? Localization.T("rev.bindings.unbound") : b.ToString();
                        SaveSettings();
                    });
                };

                y += 42;
            }

            MakeRow("rev.bindings.toggle", _revToggleBinding,
                b => ApplyRevBinding(ref _revToggleBinding, b, ExecuteRevToggle));
            MakeRow("rev.bindings.calibrate", _revCalibrateBinding,
                b => ApplyRevBinding(ref _revCalibrateBinding, b, ExecuteRevCalibrate));
            MakeRow("rev.bindings.decrease", _revDecreaseBinding,
                b => ApplyRevBinding(ref _revDecreaseBinding, b, ExecuteRevDecrease));
            MakeRow("rev.bindings.increase", _revIncreaseBinding,
                b => ApplyRevBinding(ref _revIncreaseBinding, b, ExecuteRevIncrease));

            ShowModalPopup(popup);
        }

        private void OpenInSimPalette(Button targetBtn, Action<string> setColor)
        {
            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
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

            ShowModalPopup(popup);
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
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        private void ExecuteLightToggle()
        {
            if (!_insim.IsConnected) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IntPtr hwnd = FindLfsWindow();
                if (hwnd == IntPtr.Zero) return;

                const int VK_SHIFT = 0x10;
                const int VK_3 = 0x33;

                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT, (IntPtr)0x002A0001);
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_3, (IntPtr)0x00040001);

                System.Threading.Thread.Sleep(50);

                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_3, (IntPtr)0xC0040001);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_SHIFT, (IntPtr)0xC02A0001);
            });
        }

        private static IntPtr FindLfsWindow()
        {

            string[] titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };
            foreach (var t in titles)
            {
                IntPtr h = FindWindow(null, t);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
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

            var confirmTimer = new System.Windows.Forms.Timer { Interval = 300 };
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

        private void OnCarData(object sender, CarDataEventArgs e)
        {

            byte viewPlid = _insim.ViewPLID;

            if (viewPlid != 0)
                _lastKnownPlayerPLID = viewPlid;
            else if (_lastKnownPlayerPLID == 0)
                return;

            if (e.Car.PLID != _lastKnownPlayerPLID)
                return;

            double speed = e.Car.SpeedKmh;

            this.BeginInvoke((Action)(() =>
            {
                TryApplyInSimDriverName(_lastKnownPlayerPLID);

                if (!_showHudCheck.Checked)
                    _hud.ShowInGameAward("");

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

                if (_showHudCheck.Checked && _insim.IsConnected && _insim.IsRaceNow)
                    _hud.UpdateInGameHUD();

                if (!string.IsNullOrEmpty(_drift.LastAwardedText) && _drift.LastAwardedText != _lastOverlayBonusText)
                {
                    _overlay.ShowBonus(_drift.LastAwardedText);
                    _lastOverlayBonusText = _drift.LastAwardedText;
                }
            }));
        }

        private DriftLabelKind _driftLabelKind;

        private void OnDriftScored(long runPts, double combo, string label, string labelindr, string labelindl, DriftLabelKind labelKind)
        {
            this.BeginInvoke((Action)(() =>
            {
                _runPoints = runPts;
                _totalScore = _drift.TotalScore;
                _combo = combo;
                _driftLabel = label;
                _driftLabelKind = labelKind;
                _indicatorLabelR = labelindr;
                _indicatorLabelL = labelindl;

                _hud.DriftLabel = label;
                _hud.CurrentLabelKind = labelKind;
                _hud.IndicatorLabelRight = labelindr;
                _hud.IndicatorLabelLeft = labelindl;

                UpdateScoreLabels();

                if (_showHudCheck.Checked && _insim.IsConnected && _insim.IsRaceNow)
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
            }));
        }

        private void OnDriftStarted()
        {
            this.BeginInvoke((Action)(() =>
            {
                _runPoints = 0;
                UpdateScoreLabels();
            }));
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

                var speedingDelayTimer = new System.Windows.Forms.Timer { Interval = 1250 };
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

            var delayTimer = new System.Windows.Forms.Timer { Interval = 1250 };
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
            this.BeginInvoke((Action)(() =>
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
            }));
        }

        private static readonly Color StatusErrorColor = Color.FromArgb(220, 80, 80);
        private static readonly Color StatusSuccessColor = Color.FromArgb(100, 200, 120);

        // Appends one line to the status log instead of overwriting it, so the whole history of
        // messages stays visible and scrollable (see statusPanel) rather than only the latest one.
        private void AppendStatusMessage(string message, Color? color = null)
        {
            if (_statusLabel == null || string.IsNullOrEmpty(message)) return;
            if (_statusLabel.InvokeRequired)
            {
                _statusLabel.BeginInvoke((Action)(() => AppendStatusMessage(message, color)));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

            _statusLabel.SelectionStart = _statusLabel.TextLength;
            _statusLabel.SelectionLength = 0;
            _statusLabel.SelectionColor = color ?? ApplePalette.Text;
            _statusLabel.AppendText((_statusLabel.TextLength > 0 ? Environment.NewLine : "") + line);

            _statusLabel.SelectionStart = _statusLabel.TextLength;
            _statusLabel.ScrollToCaret();
        }

        private void OnStatus(object sender, StatusEventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                AppendStatusMessage(e.Message, e.IsError ? StatusErrorColor : StatusSuccessColor);
            }));
        }

        private DateTime _lastIndicatorCancelTime = DateTime.MinValue;
        private const int IndicatorCancelSuppressMs = 250;

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

            this.BeginInvoke((Action)(() =>
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
                        System.Threading.Thread.Sleep(50);
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
            }));
        }

        private void OnIndicatorLampStateChanged(bool isOn)
        {
            this.BeginInvoke((Action)(() =>
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
            }));
        }

        private void OnConnected(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(true);
                _connectionSwitch.Enabled = true;
                _resetBtn.Enabled = true;
                _revLimiter.Start();
                _connectionStateLabel.Text = Localization.T("status.connected");
                _connectionStateLabel.ForeColor = Color.FromArgb(60, 220, 100);

                _hud.InvalidateCache();

                if (_showHudCheck.Checked)
                    _hud.InitInGameHUD();

                if (_showOverlayCheck.Checked)
                {
                    IntPtr lfsHwnd = FindLfsWindow();
                    if (lfsHwnd != IntPtr.Zero)
                    {
                        _overlay.UpdateScore(_drift.TotalScore);
                        _overlay.UpdateAccentColor(OverlayColorIdle);
                        _overlay.UpdateMaxRpm(CalibratedMAXRPM);
                        _overlay.AttachTo(lfsHwnd);
                    }
                }

            }));
        }

        private void OnDisconnected(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _resetBtn.Enabled = false;
                _revLimiter.Stop();
                _connectionStateLabel.Text = Localization.T("status.disconnected");
                _connectionStateLabel.ForeColor = Color.FromArgb(130, 130, 165);
                _speedKmh = 0; _driftAngle = 0;

                _angleValueLabel.Text = "0°";
                _overlay.Detach();
                _overlay.ResetOutGaugeData();
                _outGaugeFreshness.Reset();
                _drift.SetOutGaugeDataFresh(false);
                _hud.InvalidateCache();
            }));
        }

        private void OnConnectFailed(object sender, string message)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _connectionSwitch.Enabled = true;
                _connectionStateLabel.Text = Localization.T("status.disconnected");
                _connectionStateLabel.ForeColor = Color.FromArgb(130, 130, 165);
            }));
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

        // Contact outside of a drift is purely informational (name + side, no scoring). Contact
        // WHILE drifting is checked again 2s later: if the drift is still going and roughly
        // unchanged, it was a stylish "kiss" (small reward); if the drift broke, spun, or changed
        // speed a lot, or the hit was simply too strong to begin with, it's a penalty. Either way
        // it only ever touches the EXISTING drift score (CurrentRunPoints/TotalScore) — this has
        // no scoring, combo, or run of its own.
        private const int CollisionTooStrongTier = 4;
        private const double CollisionKissAngleToleranceDeg = 15.0;
        private const double CollisionKissSpeedToleranceKmh = 15.0;
        private const long CollisionKissBonusPoints = 150;
        private const long CollisionPenaltyPoints = 250;

        private void OnCollisionDetected(int tier, string hitTypeKey)
        {
            string tierName = Localization.T($"derby.levels.tier{tier}");
            string hitTypeName = Localization.T(hitTypeKey);
            string hitDesc = $"{tierName} ({hitTypeName})";

            if (!_drift.IsDrifting)
            {
                _overlay.ShowBonus(hitDesc);
                return;
            }

            double angleAtContact = _drift.DriftAngleDeg;
            double speedAtContact = _drift.SpeedKmh;

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                ResolveDriftCollision(tier, hitDesc, angleAtContact, speedAtContact);
            };
            timer.Start();
        }

        private void ResolveDriftCollision(int tier, string hitDesc, double angleAtContact, double speedAtContact)
        {
            bool stillStable = _drift.IsDrifting
                && Math.Abs(_drift.DriftAngleDeg - angleAtContact) <= CollisionKissAngleToleranceDeg
                && Math.Abs(_drift.SpeedKmh - speedAtContact) <= CollisionKissSpeedToleranceKmh
                && tier < CollisionTooStrongTier;

            if (stillStable)
            {
                _drift.ApplyCollisionPoints(CollisionKissBonusPoints,
                    string.Format(Localization.T("collision.kiss"), hitDesc, CollisionKissBonusPoints));
            }
            else
            {
                _drift.ApplyCollisionPoints(-CollisionPenaltyPoints,
                    string.Format(Localization.T("collision.hit"), hitDesc, CollisionPenaltyPoints));
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
        string locKey = null)
        {
            var l = new Label
            {
                Text = locKey != null ? Localization.T(locKey) : text,
                Location = new Point(x, y),
                Size = new Size(w, h),

                BackColor = Color.Transparent,

                ForeColor = fore ?? (secondary ? ApplePalette.Secondary : ApplePalette.Text),

                Font = font ?? (secondary
                    ? new Font("Segoe UI", 8.5f, FontStyle.Regular)
                    : new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular)),

                TextAlign = align,

                AutoEllipsis = true,
                UseCompatibleTextRendering = true
            };

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

                Font = new Font("Segoe UI", 10f),

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
        string locKey = null)
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

                Font = new Font("Segoe UI Semibold", 10f),
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
                Color currentColor = (Color)btn.Tag;

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle full = new Rectangle(0, 0, btn.Width, btn.Height);
                Rectangle rect = Rectangle.Inflate(full, -margin, -margin);

                using (SolidBrush parentBrush = new SolidBrush(parent.BackColor))
                    g.FillRectangle(parentBrush, full);

                if (isHover)
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

                TextRenderer.DrawText(
                    g,
                    btn.Text,
                    btn.Font,
                    rect,
                    btn.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            SetButtonColor(_colorBtn2, useOverlayColors ? OverlayColor2 : InSimCodeToColor(InSimColor2));
            SetButtonColor(_colorBtn3, useOverlayColors ? OverlayColor3 : InSimCodeToColor(InSimColor3));
            SetButtonColor(_colorBtn4, useOverlayColors ? OverlayColor4 : InSimCodeToColor(InSimColor4));
            SetButtonColor(_colorBtn5, useOverlayColors ? OverlayColor5 : InSimCodeToColor(InSimColor5));
            if (_colorBtn6 != null)
                SetButtonColor(_colorBtn6, useOverlayColors ? OverlayColor6 : InSimCodeToColor(InSimColor6));
        }

        private void OpenCustomColorPicker(Button targetBtn, Color initialColor, string title, Action<Color> onApply, bool includeAlpha = false)
        {
            Color initial = initialColor;

            int popupHeight = includeAlpha ? 320 : 275;

            Form popup = new Form { StartPosition = FormStartPosition.CenterParent };
            var card = BuildPopupChrome(popup, 300, popupHeight, out _);

            MakeLabel(card, title, 20, 15, 240, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

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

            MacSlider sliderR = null, sliderG = null, sliderB = null;
            MacSlider? sliderA = null;
            Label valR = null, valG = null, valB = null;
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

            var applyBtn = MakeButton(card, Localization.T("common.ok"), 20, includeAlpha ? 252 : 218, 260, 36, ApplePalette.Blue);
            applyBtn.Click += (s, e) => { SaveSettings(); popup.Close(); };

            ShowModalPopup(popup);
        }

        private void HandleColorButtonClick(Button btn, int slot)
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
                });
            }
            else
            {
                OpenInSimPalette(btn, code =>
                {
                    SetInSimColorSlot(slot, code);
                    SaveSettings();
                    RefreshColorButtonSwatches();
                });
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
                using (var pen = new Pen(ApplePalette.Border))
                    e.Graphics.DrawLine(pen, 0, _titleBar.Height + 1, _titleBar.Width, _titleBar.Height + 1);
            };

            _titleBarLabel = new Label
            {
                Text = "Live For Speed - Drift Tools",
                Location = new Point(14, 0),
                Size = new Size(300, TitleBarHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ApplePalette.Title,
                Font = new Font("Segoe UI Semibold", 9.5f),
                BackColor = Color.Transparent
            };

            _btnMinimize = new CaptionButton(CaptionButton.Kind.Minimize);
            _btnClose = new CaptionButton(CaptionButton.Kind.Close);

            _btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _btnClose.Click += (s, e) => Close();

            void LayoutButtons()
            {
                int btnHeight = _titleBar.Height;

                _btnClose.Size = new Size(_btnClose.Width, btnHeight);
                _btnClose.Location = new Point(_titleBar.Width - _btnClose.Width, 0);

                _btnMinimize.Size = new Size(_btnMinimize.Width, btnHeight);
                _btnMinimize.Location = new Point(_btnClose.Left - _btnMinimize.Width, 0);
            }

            _titleBar.Resize += (s, e) => LayoutButtons();

            _titleBar.Controls.Add(_titleBarLabel);
            _titleBar.Controls.Add(_btnMinimize);
            _titleBar.Controls.Add(_btnClose);

            LayoutButtons();
            _titleBar.MouseDown += TitleBar_MouseDown;
            _titleBarLabel.MouseDown += TitleBar_MouseDown;

            Controls.Add(_titleBar);
            _titleBar.BringToFront();
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
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

        private const int WM_NCHITTEST = 0x0084;
        private const int HT_CLIENT = 0x1;
        private const int HT_BOTTOM = 0xF;
        private const int ResizeBorderThickness = 20;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HT_CLIENT)
                {
                    int x = unchecked((short)((long)m.LParam & 0xFFFF));
                    int y = unchecked((short)(((long)m.LParam >> 16) & 0xFFFF));
                    Point clientPt = PointToClient(new Point(x, y));
                    if (clientPt.Y >= ClientSize.Height - ResizeBorderThickness)
                        m.Result = (IntPtr)HT_BOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            _drift.FlushStats();
            _outGaugeFreshnessPoll.Stop();
            _outGaugeFreshnessPoll.Dispose();
            Localization.LanguageChanged -= ApplyLanguage;
            if (_insim.IsConnected) { _insim.DeleteAllButtons(); System.Threading.Thread.Sleep(120); }
            _insim.Dispose();
            _outGauge.Dispose();
            _revLimiter.Dispose();
            _globalHotkey.Dispose();
            _wheelInput?.Dispose();
            _overlay?.Dispose();
            _hud?.Dispose();
            _shadow?.Close();
            _revLimiterCalibrationPrompt?.Close();
            base.OnFormClosing(e);
        }

        private void InitializeComponent() { }
    }

    public class MacToggleSwitch : Control
    {
        private bool _checked = false;
        private float _knobProgress = 0f;
        private float _animFrom, _animTo;
        private DateTime _animStart;
        private readonly System.Windows.Forms.Timer _animTimer;
        private const int AnimDurationMs = 160;

        private bool _isPressed = false;

        public event EventHandler CheckedChanged;

        public Color OnColor { get; set; } = Color.FromArgb(52, 199, 89);
        public Color OffColor { get; set; } = Color.FromArgb(210, 210, 215);
        public Color KnobColor { get; set; } = Color.White;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                AnimateTo(value ? 1f : 0f);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public MacToggleSwitch()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Cursor = Cursors.Hand;
            Size = new Size(51, 30);

            _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _animTimer.Tick += AnimTimer_Tick;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetCheckedSilent(bool value)
        {
            if (_checked == value) return;
            _checked = value;
            AnimateTo(value ? 1f : 0f);

        }

        private void AnimateTo(float target)
        {
            _animFrom = _knobProgress;
            _animTo = target;
            _animStart = DateTime.Now;
            _animTimer.Start();
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            double t = Math.Min(1.0, elapsed / AnimDurationMs);

            double eased = 1 - Math.Pow(1 - t, 3);

            _knobProgress = (float)(_animFrom + (_animTo - _animFrom) * eased);
            Invalidate();

            if (t >= 1.0)
            {
                _knobProgress = _animTo;
                _animTimer.Stop();
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _isPressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            Rectangle trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = (Height - 1) / 2;

            Color trackColor = Blend(OffColor, OnColor, _knobProgress);

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (Pen border = new Pen(Color.FromArgb(25, 0, 0, 0), 1f))
            {
                g.DrawPath(border, trackPath);
            }

            int knobDiameter = Height - 5;
            int knobTravel = Width - knobDiameter - 5;
            int knobX = 2 + (int)(knobTravel * _knobProgress);
            int knobY = 2;

            Rectangle knobRect = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);

            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            Color knob = _isPressed ? DrawingHelpers.Darken(KnobColor, 0.05) : KnobColor;

            using (GraphicsPath knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knobRect);
                using (SolidBrush knobBrush = new SolidBrush(knob))
                    g.FillPath(knobBrush, knobPath);

                using (Pen knobBorder = new Pen(Color.FromArgb(20, 0, 0, 0), 1f))
                    g.DrawPath(knobBorder, knobPath);
            }

            if (!Enabled)
            {
                using GraphicsPath dimPath = DrawingHelpers.RoundedPath(trackRect, radius);
                using SolidBrush dimBrush = new SolidBrush(Color.FromArgb(120, Parent?.BackColor ?? BackColor));
                g.FillPath(dimBrush, dimPath);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

    }
    public class MacSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private bool _isDragging = false;
        private bool _isHoverKnob = false;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == clamped) return;
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Color TrackColor { get; set; } = Color.FromArgb(225, 225, 230);
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255);
        public Color KnobColor { get; set; } = Color.White;

        public MacSlider()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Cursor = Cursors.Hand;
            Height = 22;
        }

        public void SetValueSilent(int value)
        {
            int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
        }

        private int TrackY => Height / 2;
        private int KnobDiameter => Math.Min(18, Height - 2);
        private int TrackHeight => 6;

        private int ValueToX(int value)
        {
            int knobRadius = KnobDiameter / 2;
            float fraction = (_maximum > _minimum)
                ? (float)(value - _minimum) / (_maximum - _minimum)
                : 0f;
            int travel = Width - KnobDiameter;
            return knobRadius + (int)(travel * fraction);
        }

        private int XToValue(int x)
        {
            int knobRadius = KnobDiameter / 2;
            int travel = Width - KnobDiameter;
            if (travel <= 0) return _minimum;

            float fraction = (float)(x - knobRadius) / travel;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Value = XToValue(e.X);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int knobX = ValueToX(_value);
            bool overKnob = Math.Abs(e.X - knobX) <= KnobDiameter / 2 + 4;
            if (overKnob != _isHoverKnob)
            {
                _isHoverKnob = overKnob;
                Invalidate();
            }

            if (_isDragging)
            {
                Value = XToValue(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHoverKnob = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            int knobDiameter = KnobDiameter;
            int trackHeight = TrackHeight;
            int trackY = (Height - trackHeight) / 2;
            int knobRadius = knobDiameter / 2;

            Rectangle trackRect = new Rectangle(knobRadius, trackY, Width - knobDiameter, trackHeight);
            int radius = trackHeight / 2;

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            int knobX = ValueToX(_value);

            int fillWidth = Math.Max(0, knobX - knobRadius);
            if (fillWidth > 0)
            {
                Rectangle fillRect = new Rectangle(knobRadius, trackY, fillWidth, trackHeight);
                using (GraphicsPath fillPath = DrawingHelpers.RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           new Rectangle(knobRadius, trackY, Math.Max(fillWidth, 1), trackHeight),
                           DrawingHelpers.Lighten(FillColor, 0.10),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }

            Rectangle knobRect = new Rectangle(knobX - knobRadius, (Height - knobDiameter) / 2, knobDiameter, knobDiameter);
            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            Color knob = _isDragging ? DrawingHelpers.Darken(KnobColor, 0.05)
                       : _isHoverKnob ? DrawingHelpers.Lighten(KnobColor, 0.0)
                       : KnobColor;

            using (GraphicsPath knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knobRect);
                using (SolidBrush knobBrush = new SolidBrush(knob))
                    g.FillPath(knobBrush, knobPath);

                using (Pen knobBorder = new Pen(Color.FromArgb(35, 0, 0, 0), 1f))
                    g.DrawPath(knobBorder, knobPath);
            }

            if (_isHoverKnob || _isDragging)
            {
                using (GraphicsPath ringPath = new GraphicsPath())
                {
                    Rectangle ringRect = Rectangle.Inflate(knobRect, 3, 3);
                    ringPath.AddEllipse(ringRect);
                    using (Pen ringPen = new Pen(Color.FromArgb(60, FillColor), 2f))
                        g.DrawPath(ringPen, ringPath);
                }
            }
        }

    }

    public class MacProgressBar : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;

        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == clamped) return;
                _value = clamped;
                Invalidate();
            }
        }

        public Color TrackColor { get; set; } = Color.FromArgb(225, 225, 230);
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255);

        public MacProgressBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Height = 8;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            int radius = Height / 2;
            Rectangle trackRect = new Rectangle(0, 0, Width, Height);

            using (GraphicsPath trackPath = DrawingHelpers.RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            float percent = (_maximum > _minimum)
                ? (float)(_value - _minimum) / (_maximum - _minimum)
                : 0f;
            percent = Math.Max(0f, Math.Min(1f, percent));

            int fillWidth = (int)(Width * percent);

            if (fillWidth > 0)
            {

                fillWidth = Math.Max(fillWidth, Height);

                Rectangle fillRect = new Rectangle(0, 0, fillWidth, Height);

                using (GraphicsPath fillPath = DrawingHelpers.RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           fillRect,
                           DrawingHelpers.Lighten(FillColor, 0.10f),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }
        }

    }

    public class MacCheckBox : CheckBox
    {
        public Color CheckedColor { get; set; } = Color.FromArgb(0, 122, 255);

        public MacCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9.5f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? ApplePalette.Card))
                g.FillRectangle(parentBrush, ClientRectangle);

            const int boxSize = 18;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(0, boxY, boxSize, boxSize);

            using (GraphicsPath boxPath = DrawingHelpers.RoundedPath(boxRect, 5))
            {
                if (Checked)
                {
                    using (SolidBrush fill = new SolidBrush(CheckedColor))
                        g.FillPath(fill, boxPath);
                }
                else
                {

                    using (Pen border = new Pen(ApplePalette.Border, 1.5f))
                        g.DrawPath(border, boxPath);
                }
            }

            if (Checked)
            {
                using (Pen check = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    Point p1 = new Point(boxRect.X + 4, boxRect.Y + 9);
                    Point p2 = new Point(boxRect.X + 7, boxRect.Y + 13);
                    Point p3 = new Point(boxRect.X + 14, boxRect.Y + 5);
                    g.DrawLines(check, new[] { p1, p2, p3 });
                }
            }

            Rectangle textRect = new Rectangle(boxSize + 8, 0, Width - boxSize - 8, Height);

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                ApplePalette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

    }

    public static class ApplePalette
    {

        public static readonly Color Blue =
            Color.FromArgb(0, 122, 255);

        public static readonly Color Green =
            Color.FromArgb(52, 199, 89);

        public static readonly Color Orange =
            Color.FromArgb(255, 159, 10);

        public static readonly Color Red =
            Color.FromArgb(255, 69, 58);

        public static Color Background { get; private set; } = Color.FromArgb(245, 245, 247);
        public static Color Card { get; private set; } = Color.White;
        public static Color Border { get; private set; } = Color.FromArgb(224, 224, 229);
        public static Color Title { get; private set; } = Color.FromArgb(28, 28, 30);
        public static Color Text { get; private set; } = Color.FromArgb(58, 58, 60);
        public static Color Secondary { get; private set; } = Color.FromArgb(120, 120, 128);

        public static bool IsDark { get; private set; } = false;

        public static void SetDark(bool dark)
        {
            IsDark = dark;

            if (dark)
            {
                Background = Color.FromArgb(28, 28, 30);
                Card = Color.FromArgb(40, 40, 43);
                Border = Color.FromArgb(60, 60, 64);
                Title = Color.FromArgb(245, 245, 247);
                Text = Color.FromArgb(220, 220, 225);
                Secondary = Color.FromArgb(150, 150, 160);
            }
            else
            {
                Background = Color.FromArgb(245, 245, 247);
                Card = Color.White;
                Border = Color.FromArgb(224, 224, 229);
                Title = Color.FromArgb(28, 28, 30);
                Text = Color.FromArgb(58, 58, 60);
                Secondary = Color.FromArgb(120, 120, 128);
            }
        }
    }

    public class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 20;

        public RoundedPanel()
        {
            DoubleBuffered = true;

            BackColor = ApplePalette.Card;

            Padding = new Padding(15);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            e.Graphics.Clear(Parent.BackColor);

            Rectangle shadow = new Rectangle(
                4,
                6,
                Width - 8,
                Height - 8);

            using (GraphicsPath shadowPath = DrawingHelpers.RoundedPath(shadow, Radius))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
            {
                e.Graphics.FillPath(sb, shadowPath);
            }

            Rectangle rect = new Rectangle(
                0,
                0,
                Width - 1,
                Height - 1);

            using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, Radius))
            {
                using (SolidBrush b = new SolidBrush(BackColor))
                    e.Graphics.FillPath(b, path);

                using (Pen p = new Pen(ApplePalette.Border))
                    e.Graphics.DrawPath(p, path);
            }
        }

    }

    public class NoScrollBarPanel : Panel
    {
        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, [MarshalAs(UnmanagedType.Bool)] bool bShow);
        private const int SB_VERT = 1;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_SIZE = 0x0005;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCCALCSIZE || m.Msg == WM_NCPAINT || m.Msg == WM_SIZE)
                ShowScrollBar(Handle, SB_VERT, false);
        }
    }

    public class NoWheelRichTextBox : RichTextBox
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_MOUSEWHEEL = 0x020A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                Control target = Parent;
                while (target != null && target is not NoScrollBarPanel)
                    target = target.Parent;

                if (target != null)
                    SendMessage(target.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class MinimalScrollbar : Control
    {
        public const int TrackWidth = 10;
        private const int HoverTrackWidth = TrackWidth * 2;
        private const int ThumbWidth = 4;
        private const int HoverThumbWidth = ThumbWidth * 2;
        private const int MinThumbHeight = 24;

        private Panel _target;
        private bool _hover;
        private bool _widened;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartScroll;

        public MinimalScrollbar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = ApplePalette.Background;
            Width = TrackWidth;
            Cursor = Cursors.Default;

            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        }

        public void AttachTo(Panel target)
        {
            _target = target;
            target.Resize += (s, e) => { SyncBounds(); Invalidate(); };

            target.Scroll += (s, e) => Invalidate();
            target.MouseWheel += (s, e) => Invalidate();

            target.Layout += (s, e) => Invalidate();
            SyncBounds();
            Invalidate();
        }

        public void SyncToTarget() => Invalidate();

        public void ApplyThemeColor(Color background)
        {
            BackColor = background;
            Invalidate();
        }

        private void SetWidened(bool widened)
        {
            if (_widened == widened) return;
            _widened = widened;
            SyncBounds();
            Invalidate();
        }

        private void SyncBounds()
        {
            if (_target?.Parent == null) return;
            int width = _widened ? HoverTrackWidth : TrackWidth;
            int rightEdge = _target.Right + TrackWidth;
            Location = new Point(rightEdge - width, _target.Top);
            Size = new Size(width, _target.Height);
        }

        private (int top, int height) ComputeThumb()
        {
            if (_target == null) return (0, Height);
            int trackH = Height;
            int max = Math.Max(1, _target.VerticalScroll.Maximum);
            int large = Math.Max(1, _target.VerticalScroll.LargeChange);
            if (max <= large) return (0, trackH);

            int thumbH = Math.Max(MinThumbHeight, (int)((long)trackH * large / max));
            int scrollableRange = Math.Max(1, max - large);
            int value = Math.Clamp(_target.VerticalScroll.Value, 0, scrollableRange);
            int thumbY = (int)((long)(trackH - thumbH) * value / scrollableRange);
            return (thumbY, thumbH);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var (top, height) = ComputeThumb();

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color thumbColor = _hover || _dragging
                ? Color.FromArgb(220, 150, 150, 150)
                : Color.FromArgb(130, 150, 150, 150);

            int thumbWidth = _widened ? HoverThumbWidth : ThumbWidth;
            int thumbX = (Width - thumbWidth) / 2;
            Rectangle rect = new Rectangle(thumbX, top, thumbWidth, height);
            using var path = DrawingHelpers.RoundedPath(rect, thumbWidth / 2);
            using var brush = new SolidBrush(thumbColor);
            e.Graphics.FillPath(brush, path);
        }

        private bool CanScroll => _target != null
            && _target.VerticalScroll.Maximum > Math.Max(1, _target.VerticalScroll.LargeChange);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!CanScroll) return;
            var (top, height) = ComputeThumb();
            if (e.Y < top || e.Y > top + height) return;

            _dragging = true;
            _dragStartY = e.Y;
            _dragStartScroll = _target.VerticalScroll.Value;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var (top, height) = ComputeThumb();
            bool nowHover = CanScroll && e.Y >= top && e.Y <= top + height;
            if (nowHover != _hover) { _hover = nowHover; Invalidate(); }

            if (!_dragging || _target == null) return;

            int max = _target.VerticalScroll.Maximum;
            int large = Math.Max(1, _target.VerticalScroll.LargeChange);
            int scrollableRange = Math.Max(1, max - large);
            int trackRange = Math.Max(1, Height - height);

            int deltaPixels = e.Y - _dragStartY;
            int deltaValue = (int)((long)deltaPixels * scrollableRange / trackRange);
            int newValue = Math.Clamp(_dragStartScroll + deltaValue, 0, scrollableRange);

            _target.AutoScrollPosition = new Point(0, newValue);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetWidened(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            _hover = false;
            SetWidened(false);
            Invalidate();
        }
    }

    public class MinimalNumericUpDown : Panel
    {
        public decimal Minimum { get; set; }
        public decimal Maximum { get; set; }
        public decimal Increment { get; set; } = 1;

        private int _decimalPlaces = 0;
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set { _decimalPlaces = value; UpdateText(); }
        }

        private decimal _value;
        public decimal Value
        {
            get => _value;
            set => SetValue(value, raiseEvent: true);
        }

        public event Action<decimal>? ValueChanged;

        private const int StepBtnW = 14;
        private const int Pad = 3;

        private readonly TextBox _text;
        private readonly Button _minusBtn;
        private readonly Button _plusBtn;

        public MinimalNumericUpDown(HorizontalAlignment textAlign = HorizontalAlignment.Right)
        {
            BackColor = ApplePalette.Card;
            Tag = "theme:card";

            _minusBtn = MakeStepButton("−");
            _plusBtn = MakeStepButton("+");
            _minusBtn.Click += (s, e) => Step(-1);
            _plusBtn.Click += (s, e) => Step(1);

            _text = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,
                Font = new Font("Segoe UI", 10f),
                TextAlign = textAlign,
            };
            _text.KeyPress += Text_KeyPress;
            _text.Leave += (s, e) => CommitTextInput();
            _text.KeyDown += Text_KeyDown;

            _text.GotFocus += (s, e) =>
            {
                BackColor = DrawingHelpers.Lighten(ApplePalette.Card, 0.02);
                Invalidate();
            };
            _text.LostFocus += (s, e) =>
            {
                BackColor = ApplePalette.Card;
                Invalidate();
            };

            Controls.Add(_text);
            Controls.Add(_minusBtn);
            Controls.Add(_plusBtn);

            Resize += (s, e) => LayoutChildren();

            Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = DrawingHelpers.RoundedPath(rect, 10))
                using (Pen border = new Pen(ApplePalette.Border))
                    e.Graphics.DrawPath(border, path);
            };
        }

        private Button MakeStepButton(string glyph)
        {
            var b = new Button
            {

                Text = "",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ApplePalette.Secondary,
                Font = new Font("Segoe UI Semibold", 9f),
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 240);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 220, 225);
            b.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                SizeF size = e.Graphics.MeasureString(glyph, b.Font);
                float x = (b.Width - size.Width) / 2f;
                float y = (b.Height - size.Height) / 2f;
                using var brush = new SolidBrush(b.ForeColor);
                e.Graphics.DrawString(glyph, b.Font, brush, x, y);
            };
            return b;
        }

        private void LayoutChildren()
        {
            int btnH = Math.Max(1, Height - 12);
            _minusBtn.SetBounds(Pad, 6, StepBtnW, btnH);
            _plusBtn.SetBounds(Width - Pad - StepBtnW, 6, StepBtnW, btnH);

            int textX = Pad + StepBtnW + Pad;
            int textW = Math.Max(1, Width - 2 * textX);
            _text.SetBounds(textX, 6, textW, btnH);
        }

        private void Step(int direction) => SetValue(_value + Increment * direction, raiseEvent: true);

        private void SetValue(decimal v, bool raiseEvent)
        {
            v = Math.Clamp(v, Minimum, Maximum);
            bool changed = v != _value;
            _value = v;
            UpdateText();
            if (changed && raiseEvent)
                ValueChanged?.Invoke(_value);
        }

        private void UpdateText()
        {
            _text.Text = _decimalPlaces > 0 ? _value.ToString("F" + _decimalPlaces) : _value.ToString("0");
        }

        private void Text_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (char.IsDigit(e.KeyChar)) return;
            if (e.KeyChar == '-' && _text.SelectionStart == 0 && Minimum < 0 && !_text.Text.Contains('-')) return;
            if (_decimalPlaces > 0 && (e.KeyChar == '.' || e.KeyChar == ',') && !_text.Text.Contains('.') && !_text.Text.Contains(','))
                return;
            e.Handled = true;
        }

        private void Text_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { CommitTextInput(); e.SuppressKeyPress = true; }
        }

        private void CommitTextInput()
        {
            string normalized = _text.Text.Replace(',', '.');
            if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                SetValue(v, raiseEvent: true);
            else
                UpdateText();
        }
    }

    public class SpeedoTachoPreviewControl : Control
    {
        public Color BackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20);
        public Color TextColor { get; set; } = Color.White;
        public Color IndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230);
        public Color TickColor { get; set; } = Color.FromArgb(255, 215, 215, 218);
        public Color RedlineColor { get; set; } = Color.Red;
        public bool UseMph { get; set; } = false;

        private const float DemoCalibratedMaxRpm = 8000f;
        private const string DemoGear = "3";
        private const int DemoSpeedKmh = 118;
        private const double KmhToMph = 0.621371;

        // Cycles through a few RPM/rev-cut states (mirrors ForzaHudPreviewControl) so the color
        // pickers below (Redline, in particular) are actually visible in the live preview instead
        // of a single fixed frame that could never reach the redline branch at all.
        private static readonly (float rpm, bool revCut)[] _tachoTiers =
        {
            (5600f, false),
            (7600f, false),
            (8000f, false),
            (8000f, true),
        };
        private int _tachoTierIndex = 0;
        private float DemoRpm => _tachoTiers[_tachoTierIndex].rpm;
        private bool DemoRevCutActive => _tachoTiers[_tachoTierIndex].revCut;
        private readonly System.Windows.Forms.Timer _tachoCycleTimer;

        private const float Cx = 140f, Cy = 118f, Radius = 102f;

        private static readonly System.Drawing.Text.PrivateFontCollection _fontCollection = new();
        private static readonly FontFamily? _activeFontFamily = LoadActiveFontFamily();

        private static FontFamily? LoadActiveFontFamily()
        {
            try
            {
                string fontPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Fonts", "active.otf");
                if (File.Exists(fontPath))
                {
                    _fontCollection.AddFontFile(fontPath);
                    return _fontCollection.Families[0];
                }
            }
            catch { }
            return null;
        }

        private static Font MakeFont(float size) =>
            _activeFontFamily != null ? new Font(_activeFontFamily, size, FontStyle.Bold) : new Font("Segoe UI", size, FontStyle.Bold);

        public SpeedoTachoPreviewControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;

            _tachoCycleTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _tachoCycleTimer.Tick += (s, e) =>
            {
                _tachoTierIndex = (_tachoTierIndex + 1) % _tachoTiers.Length;
                Invalidate();
            };
            _tachoCycleTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tachoCycleTimer.Dispose();
            base.Dispose(disposing);
        }

        private static float ComputeGaugeMaxRpm(float calibratedMaxRpm)
        {
            float withMargin = calibratedMaxRpm * 1.15f;
            float rounded = (float)(Math.Ceiling(withMargin / 1000.0) * 1000.0);
            return Math.Max(rounded, calibratedMaxRpm + 1000f);
        }

        private Color GetTachoStateColor()
        {
            if (DemoRevCutActive || DemoRpm >= DemoCalibratedMaxRpm) return RedlineColor;

            float lower = DemoCalibratedMaxRpm - 1000;
            float upper = DemoCalibratedMaxRpm - 100;
            if (DemoRpm >= lower && DemoRpm < upper)
                return Color.FromArgb(255, 150, 230, 170);

            return IndicatorColor;
        }

        private static Color WithAlpha(Color c, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            return Color.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);
        }

        private static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var shadowBrush = new SolidBrush(shadow))
                g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
            using (var fillBrush = new SolidBrush(fill))
                g.DrawString(text, font, fillBrush, x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            const float contentSize = Radius * 2f;
            float scale = Math.Min(Width, Height) / contentSize;
            GraphicsState savedState = g.Save();
            g.TranslateTransform(
                (Width - contentSize * scale) / 2f - (Cx - Radius) * scale,
                (Height - contentSize * scale) / 2f - (Cy - Radius) * scale);
            g.ScaleTransform(scale, scale);

            const float cx = Cx, cy = Cy, radius = Radius;
            const float startAngle = 135f, sweepAngle = 270f;

            float gaugeMaxRpm = ComputeGaugeMaxRpm(DemoCalibratedMaxRpm);
            int majorTicks = Math.Max(1, (int)Math.Round(gaugeMaxRpm / 1000.0));
            Color stateColor = GetTachoStateColor();

            var dialRect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            using (var bgBrush = new SolidBrush(BackgroundColor))
                g.FillEllipse(bgBrush, dialRect);

            if (DemoCalibratedMaxRpm > 0 && DemoCalibratedMaxRpm < gaugeMaxRpm)
            {
                float redlineStartFrac = DemoCalibratedMaxRpm / gaugeMaxRpm;
                float redlineStartAngle = startAngle + redlineStartFrac * sweepAngle;
                float redlineSweep = sweepAngle - redlineStartFrac * sweepAngle;

                using var redlinePen = new Pen(WithAlpha(RedlineColor, 0.9), 6f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(redlinePen, dialRect, redlineStartAngle, redlineSweep);
            }

            using Font tickFont = MakeFont(12f);
            for (int i = 0; i <= majorTicks; i++)
            {
                float frac = (float)i / majorTicks;
                float angleDeg = startAngle + frac * sweepAngle;
                double angleRad = angleDeg * Math.PI / 180.0;

                bool inRedline = DemoCalibratedMaxRpm > 0 && (i * 1000f) >= DemoCalibratedMaxRpm;
                Color tickColor = inRedline ? RedlineColor : TickColor;

                float outerR = radius;
                float innerR = radius - 14;
                float x1 = cx + (float)Math.Cos(angleRad) * outerR;
                float y1 = cy + (float)Math.Sin(angleRad) * outerR;
                float x2 = cx + (float)Math.Cos(angleRad) * innerR;
                float y2 = cy + (float)Math.Sin(angleRad) * innerR;

                using (var tickPen = new Pen(tickColor, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(tickPen, x1, y1, x2, y2);

                string label = i.ToString();
                var labelSize = g.MeasureString(label, tickFont);
                float labelR = innerR - 15;
                float lx = cx + (float)Math.Cos(angleRad) * labelR - labelSize.Width / 2f;
                float ly = cy + (float)Math.Sin(angleRad) * labelR - labelSize.Height / 2f;
                using (var labelBrush = new SolidBrush(WithAlpha(TextColor, (double)TextColor.A / 255.0 * 0.85)))
                    g.DrawString(label, tickFont, labelBrush, lx, ly);

                if (i < majorTicks)
                {
                    float midFrac = (i + 0.5f) / majorTicks;
                    float midAngleDeg = startAngle + midFrac * sweepAngle;
                    double midAngleRad = midAngleDeg * Math.PI / 180.0;
                    float mInnerR = radius - 8;
                    float mx1 = cx + (float)Math.Cos(midAngleRad) * outerR;
                    float my1 = cy + (float)Math.Sin(midAngleRad) * outerR;
                    float mx2 = cx + (float)Math.Cos(midAngleRad) * mInnerR;
                    float my2 = cy + (float)Math.Sin(midAngleRad) * mInnerR;
                    using var minorPen = new Pen(WithAlpha(TickColor, 0.65), 1.4f);
                    g.DrawLine(minorPen, mx1, my1, mx2, my2);
                }
            }

            float rpmFrac = Math.Clamp(DemoRpm / gaugeMaxRpm, 0f, 1f);
            float needleAngleDeg = startAngle + rpmFrac * sweepAngle;
            double needleAngleRad = needleAngleDeg * Math.PI / 180.0;
            float needleLen = radius - 5;
            float nx = cx + (float)Math.Cos(needleAngleRad) * needleLen;
            float ny = cy + (float)Math.Sin(needleAngleRad) * needleLen;

            using (var needlePen = new Pen(stateColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(needlePen, cx, cy, nx, ny);

            const float gearRadius = 32f;
            Color hubColor = DemoRevCutActive
                ? WithAlpha(RedlineColor, BackgroundColor.A / 255.0)
                : BackgroundColor;
            using (var hubBrush = new SolidBrush(hubColor))
                g.FillEllipse(hubBrush, cx - gearRadius + 3, cy - gearRadius + 3,
                    (gearRadius - 3) * 2, (gearRadius - 3) * 2);

            using (Font gearFont = MakeFont(26f))
            {
                var gearSize = g.MeasureString(DemoGear, gearFont);
                DrawShadowedText(g, DemoGear, gearFont, (cx - 2) - gearSize.Width / 2f, cy - gearSize.Height / 2f,
                    TextColor, Color.FromArgb(200, 0, 0, 0));
            }

            using Font speedFont = MakeFont(40f);
            using Font unitFont = MakeFont(11f);

            double displaySpeed = UseMph ? DemoSpeedKmh * KmhToMph : DemoSpeedKmh;
            string speedText = ((int)Math.Round(displaySpeed)).ToString("D3");
            var speedSize = g.MeasureString(speedText, speedFont);
            float speedX = cx - speedSize.Width / 2f;
            float speedY = cy + gearRadius + 10;

            DrawShadowedText(g, speedText, speedFont, speedX, speedY,
                TextColor, Color.FromArgb(200, 0, 0, 0));

            string unitText = UseMph ? "MPH" : Localization.T("speedometer.unit").ToUpperInvariant();
            var unitSize = g.MeasureString(unitText, unitFont);
            DrawShadowedText(g, unitText, unitFont,
                cx + speedSize.Width / 2.5f - unitSize.Width, speedY,
                WithAlpha(TextColor, (double)TextColor.A / 255.0 * 0.8), Color.FromArgb(180, 0, 0, 0));

            g.Restore(savedState);
        }
    }

    public class ForzaHudPreviewControl : Control
    {

        public Color IdleColor { get; set; } = Color.White;
        public Color Color1 { get; set; } = Color.FromArgb(255, 255, 243, 0);
        public Color Color2 { get; set; } = Color.FromArgb(255, 255, 147, 0);
        public Color Color3 { get; set; } = Color.FromArgb(255, 255, 74, 0);
        public Color Color4 { get; set; } = Color.FromArgb(255, 255, 0, 0);
        public Color Color5 { get; set; } = Color.FromArgb(255, 255, 0, 118);

        public Color Color6 { get; set; } = Color.FromArgb(255, 211, 0, 255);

        private static readonly Color LabelTextColor = Color.White;

        private readonly (string labelKey, long score, long run, double combo, double angle)[] _tiers =
        {
            (null, 12480, 0, 1.0, 0),
            ("drift.label.generic", 12480, 320, 1.0, 18),
            ("drift.label.angle_good", 13950, 860, 2.0, 32),
            ("drift.label.angle_high", 16200, 1850, 3.0, 58),
            ("drift.label.angle_extreme", 19800, 5200, 4.0, 78),
            ("drift.label.angle_ultraextreme", 24500, 8600, 5.0, 95),
            ("drift.label.angle_backward", 15600, 1200, 2.5, 110),
        };

        private int _tierIndex = 0;
        private readonly System.Windows.Forms.Timer _cycleTimer;

        private const float DesignWidth = 620f, DesignHeight = 150f, CenterX = DesignWidth / 2f;

        private static readonly System.Drawing.Text.PrivateFontCollection _fontCollection = new();
        private static readonly FontFamily? _activeFontFamily = LoadActiveFontFamily();

        private static FontFamily? LoadActiveFontFamily()
        {
            try
            {
                string fontPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Fonts", "active.otf");
                if (File.Exists(fontPath))
                {
                    _fontCollection.AddFontFile(fontPath);
                    return _fontCollection.Families[0];
                }
            }
            catch { }
            return null;
        }

        private static Font MakeFont(float size) =>
            _activeFontFamily != null ? new Font(_activeFontFamily, size, FontStyle.Bold) : new Font("Segoe UI", size, FontStyle.Bold);

        public ForzaHudPreviewControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;

            _cycleTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _cycleTimer.Tick += (s, e) =>
            {
                _tierIndex = (_tierIndex + 1) % _tiers.Length;
                Invalidate();
            };
            _cycleTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cycleTimer.Dispose();
            base.Dispose(disposing);
        }

        private Color AccentFor(int tierIndex) => tierIndex switch
        {
            0 => IdleColor,
            1 => Color1,
            2 => Color2,
            3 => Color3,
            4 => Color4,
            5 => Color5,
            _ => Color6,
        };

        private static Color Darken(Color c, double amount) => Color.FromArgb(
            c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

        private static void DrawShadowedText(Graphics g, string text, Font font, float x, float y, Color fill, Color shadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var shadowBrush = new SolidBrush(shadow))
                g.DrawString(text, font, shadowBrush, x + 1.5f, y + 1.5f);
            using (var fillBrush = new SolidBrush(fill))
                g.DrawString(text, font, fillBrush, x, y);
        }

        private static GraphicsPath RoundedLeftRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddLine(rect.Right, rect.Y, rect.Right, rect.Bottom);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRightRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddLine(rect.X, rect.Y, rect.Right - d, rect.Y);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom - radius);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddLine(rect.Right - d, rect.Bottom, rect.X, rect.Bottom);
            path.CloseFigure();
            return path;
        }

        private void DrawAngleArrows(Graphics g, float scoreX, float scoreY, float scoreW, float scoreH, double angle, Color accent)
        {
            int count = angle > 90 ? 6 : angle > 65 ? 5 : angle > 55 ? 4 : angle > 45 ? 3 : angle > 25 ? 2 : angle > 15 ? 1 : 0;
            if (count == 0) return;

            float chevW = 14, chevH = 16, spacing = 4;
            float midY = scoreY + scoreH / 2f;
            float startX = scoreX + scoreW + 18;

            using var pen = new Pen(accent, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            for (int i = 0; i < count; i++)
            {
                float px = startX + i * (chevW + spacing);
                g.DrawLines(pen, new[]
                {
                    new PointF(px, midY - chevH / 2),
                    new PointF(px + chevW / 2, midY),
                    new PointF(px, midY + chevH / 2)
                });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float scale = Math.Min(Width / DesignWidth, Height / DesignHeight);
            GraphicsState savedState = g.Save();
            g.TranslateTransform(
                (Width - DesignWidth * scale) / 2f,
                (Height - DesignHeight * scale) / 2f);
            g.ScaleTransform(scale, scale);

            var (labelKey, score, run, combo, angle) = _tiers[_tierIndex];
            Color accent = AccentFor(_tierIndex);
            Color accentBack = Color.FromArgb(accent.A, (int)(accent.R * 0.35), (int)(accent.G * 0.35), (int)(accent.B * 0.35));

            using Font scoreFont = MakeFont(32f);
            string scoreText = score.ToString("N0");
            var scoreSize = g.MeasureString(scoreText, scoreFont);
            float scoreX = CenterX - scoreSize.Width / 2f;
            DrawShadowedText(g, scoreText, scoreFont, scoreX, 5, accent, accentBack);

            if (labelKey == null)
            {

                g.Restore(savedState);
                return;
            }

            using Font comboFont = MakeFont(24f);
            using Font labelFont = MakeFont(22f);
            using Font runFont = MakeFont(22f);
            using Font angleFont = MakeFont(24f);

            string comboText = $"x{combo:0.0}";
            string labelText = Localization.T(labelKey);
            string runText = run.ToString("N0");

            var labelSize = g.MeasureString(labelText, labelFont);
            var runSize = g.MeasureString(runText, runFont);
            var comboSize = g.MeasureString(comboText, comboFont);

            float padH = 7, padV = 4;
            float labelBoxW = labelSize.Width + padH * 2;
            float runBoxW = runSize.Width + padH * 2;
            float barH = Math.Max(labelSize.Height, runSize.Height) + padV * 2 - 1;
            float barW = labelBoxW + runBoxW;
            float barX = CenterX - barW / 2f;
            float barY = 68;

            using (var labelPath = RoundedLeftRect(new RectangleF(barX, barY, labelBoxW + 1, barH + 1), 6))
            using (var labelBrush = new SolidBrush(accent))
                g.FillPath(labelBrush, labelPath);

            using (var runPath = RoundedRightRect(new RectangleF(barX + labelBoxW, barY, runBoxW, barH), 6))
            using (var runBrush = new SolidBrush(Darken(accent, 0.15)))
                g.FillPath(runBrush, runPath);

            DrawShadowedText(g, comboText, comboFont, barX + labelBoxW + runBoxW + 5,
                barY + (barH / 2) - (comboSize.Height / 2), accent, accentBack);

            string angleText = ((long)Math.Round(angle)).ToString("N0") + "°";
            var angleSize = g.MeasureString(angleText, angleFont);
            DrawShadowedText(g, angleText, angleFont, barX - angleSize.Width - 2,
                barY + (barH / 2) - (angleSize.Height / 2), accent, accentBack);

            DrawShadowedText(g, labelText, labelFont, barX + padH, barY + padV, LabelTextColor, Color.Black);
            DrawShadowedText(g, runText, runFont, barX + labelBoxW + padH - 2, barY + padV, LabelTextColor, Color.Black);

            DrawAngleArrows(g, scoreX, 5, scoreSize.Width, scoreSize.Height, angle, accent);

            g.Restore(savedState);
        }
    }

    public class VehicleRevSettings
    {
        public int MaxRpm { get; set; }
        public int CutMs { get; set; }
    }

    public class AppSettings
    {
        public string Language { get; set; } = "English";

        public bool OutGaugeConnectionEnabled { get; set; } = true;

        public bool DarkTheme { get; set; } = true;

        public InputBinding IndicatorLeftBinding { get; set; } = InputBinding.FromKey(Keys.D7);
        public InputBinding IndicatorRightBinding { get; set; } = InputBinding.FromKey(Keys.D8);
        public InputBinding IndicatorHazardBinding { get; set; } = InputBinding.FromKey(Keys.D9);
        public InputBinding LightToggleBinding { get; set; } = InputBinding.None;

        public string SteeringWheelDeviceGuid { get; set; } = "";
        public string SteeringWheelAxis { get; set; } = "X";

        public bool IndicatorSoundsEnabled { get; set; } = true;
        public int IndicatorSoundsVolume { get; set; } = 100;
        public bool IndicatorAutoCancelOnCenter { get; set; } = true;
        public int IndicatorArmThresholdPct { get; set; } = 25;
        public int IndicatorCenterThresholdPct { get; set; } = 5;

        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public bool SpeedoTachoUseMph { get; set; } = false;

        public bool AdvancedOutGaugeEnabled { get; set; } = true;

        public bool ShowRPMHudEnabled { get; set; } = false;

        public bool IndicatorsMasterEnabled { get; set; } = true;

        public double[] AngleLevelThresholds { get; set; } = { 25.0, 40.0, 50.0, 60.0, 70.0 };
        public double[] SpeedLevelThresholds { get; set; } = { 100.0, 130.0, 160.0, 190.0, 220.0 };

        public double MinDriftSpeedKmh { get; set; } = 20.0;
        public double MaxBurnoutSpeedKmh { get; set; } = 25.0;

        public bool CollisionDetectionEnabled { get; set; } = false;
        public double[] HitLevelThresholds { get; set; } = { 20.0, 40.0, 65.0, 95.0, 130.0 };
        public bool ObjectCollisionDetectionEnabled { get; set; } = true;
    }

    public class ColorSettings
    {
        public string InSimColor1 { get; set; } = "^7";
        public string InSimColor2 { get; set; } = "^6";
        public string InSimColor3 { get; set; } = "^3";
        public string InSimColor4 { get; set; } = "^5";
        public string InSimColor5 { get; set; } = "^5";
        public string InSimColor6 { get; set; } = "^2";
        public string InSimColorIdle { get; set; } = "^7";

        public int OverlayColor1 { get; set; } = -1;
        public int OverlayColor2 { get; set; } = -1;
        public int OverlayColor3 { get; set; } = -1;
        public int OverlayColor4 { get; set; } = -1;
        public int OverlayColor5 { get; set; } = -1;
        public int OverlayColor6 { get; set; } = -1;
        public int OverlayColorIdle { get; set; } = -1;

        public int SpeedoTachoRedlineColor { get; set; } = Color.Red.ToArgb();
        public int SpeedoTachoTextColor { get; set; } = Color.White.ToArgb();
        public int SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230).ToArgb();
        public int SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218).ToArgb();
        public int SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20).ToArgb();
    }

    public class RevLimiterConfig
    {
        public int CalibratedMAXRPM { get; set; } = 7600;

        public int SavedMSCUT { get; set; } = 40;

        public Dictionary<string, VehicleRevSettings> VehicleRevLimiterSettings { get; set; } = new();

        public InputBinding RevToggleBinding { get; set; } = InputBinding.None;
        public InputBinding RevCalibrateBinding { get; set; } = InputBinding.None;
        public InputBinding RevDecreaseBinding { get; set; } = InputBinding.None;
        public InputBinding RevIncreaseBinding { get; set; } = InputBinding.None;

        public bool AutoCalibrateNewCar { get; set; } = true;

        public bool RevLimiterEnabled { get; set; } = true;
    }

}
public class WindowShadow : Form
{
    private const int ShadowMargin = 200;
    private const int CornerRadius = 30;
    private readonly Form _owner;
    private Size _lastRenderedSize = Size.Empty;

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int ULW_ALPHA = 2;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint SWP_NOACTIVATE = 0x0010;

    public WindowShadow(Form owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public void Reposition()
    {
        if (!_owner.IsHandleCreated || _owner.WindowState == FormWindowState.Minimized || !_owner.Visible)
        {
            if (Visible) Hide();
            return;
        }

        var newBounds = new Rectangle(
            _owner.Left - ShadowMargin,
            _owner.Top - ShadowMargin + 12,
            _owner.Width + ShadowMargin * 2,
            _owner.Height + ShadowMargin * 2);

        bool sizeChanged = newBounds.Size != _lastRenderedSize;

        if (!Visible)
            Show();

        if (sizeChanged)
        {
            Bounds = newBounds;
            Render(newBounds);
            _lastRenderedSize = newBounds.Size;
        }
        else
        {

            MoveOnly(newBounds.Location);
        }

        SetWindowPos(Handle, _owner.Handle, 0, 0, 0, 0,
            0x0001 | 0x0002 | SWP_NOACTIVATE);
    }

    private void MoveOnly(Point location)
    {
        Left = location.X;
        Top = location.Y;
    }

    private void Render(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var windowRect = new Rectangle(ShadowMargin, ShadowMargin - 12, _owner.Width, _owner.Height);

            for (int i = ShadowMargin; i >= 1; i--)
            {
                int alpha = (int)(55 * Math.Pow(0.35 - (double)i / ShadowMargin, 2.2));
                if (alpha <= 0) continue;

                var layerRect = Rectangle.Inflate(windowRect, i, i);
                using var path = DrawingHelpers.RoundedPath(layerRect, CornerRadius + i);
                using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(brush, path);
            }
        }

        DrawBitmap(bmp, bounds.Location);
    }

    private void DrawBitmap(Bitmap bmp, Point location)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr oldBitmap = SelectObject(memDc, hBitmap);

        var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
        var pptSrc = new POINT { X = 0, Y = 0 };
        var pptDst = new POINT { X = location.X, Y = location.Y };
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };

        UpdateLayeredWindow(Handle, screenDc, ref pptDst, ref size, memDc, ref pptSrc, 0, ref blend, ULW_ALPHA);

        SelectObject(memDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

}

internal static class DrawingHelpers
{
    public static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
            return path;

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintSoftShadow(Graphics g, int width, int height)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 30; i >= 1; i--)
        {
            int alpha = (int)(22 * (1.0 - i / 30.0));
            Rectangle shadowRect = new Rectangle(12 - i, 12 - i, width - 24 + i * 2, height - 24 + i * 2);
            using (GraphicsPath p = RoundedPath(shadowRect, 20 + i))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                g.FillPath(b, p);
        }
    }

    public static Color Lighten(Color c, double amount)
    {
        return Color.FromArgb(
            c.A,
            Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
            Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
            Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
    }

    public static Color Darken(Color c, double amount)
    {
        return Color.FromArgb(
            c.A,
            (int)(c.R * (1 - amount)),
            (int)(c.G * (1 - amount)),
            (int)(c.B * (1 - amount)));
    }
}