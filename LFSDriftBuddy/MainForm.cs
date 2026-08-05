using LFSDriftBuddy.InSim;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static LFSDriftBuddy.DriftEngine;
using static LFSDriftBuddy.IndicatorManager;
using static System.Net.Mime.MediaTypeNames;
using SharpDX.DirectInput;
using System.Reflection;

namespace LFSDriftBuddy
{
    public partial class MainForm : Form
    {
        

        private readonly string SettingsFile =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "settings.json");

        private readonly SoundPlayer _indicatorClick =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click.wav"));

        private readonly SoundPlayer _indicatorCancel =
            new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_cancel.wav"));

        private OverlayForm _overlay;

        // ── InSim + Drift ─────────────────────────────────────
        private InSimConnection _insim;
        private DriftEngine _drift = new DriftEngine();
        private IndicatorManager _indicators = new IndicatorManager();
        private byte _playerPLID = 0;  // ← NOWE POLE
        private byte _lastKnownPlayerPLID = 0;  // fallback, gdy IS_STA jeszcze nie doszedł

        // ── Current data ──────────────────────────────────────
        private double _speedKmh = 0;
        private double _driftAngle = 0;
        private long _runPoints = 0;
        private long _totalScore = 0;
        private double _combo = 1;
        private bool _isDrifting = false;
        private bool _isSpeeding = false;
        private string _driftLabel = "";
        private string _indicatorLabelR = "";
        private string _indicatorLabelL = "";
        private string _lastAward = "";

        public int CalibratedMAXRPM = 7600;
        public int SavedMSCUT = 40;
        public bool calibrationON = false;

        

        private const int TitleBarHeight = 40;
        private Panel _titleBar;
        private Label _titleBarLabel;
        private CaptionButton _btnMinimize;
        private CaptionButton _btnClose;

        private Label _rpmLabel = null!;
        private Label _revCutLabel = null!;
        private Label _revLimitLabel = null!;
        private NumericUpDown _revLimiterNumeric = null!;
        private NumericUpDown _revCutMS = null!;

        private MacProgressBar _rpmBar = null!;

        // ── LFS button IDs ────────────────────────────────────
        private const byte BTN_SCORE = 1;   // total score top-center
        private const byte BTN_RUN = 2;   // current run points
        private const byte BTN_COMBO = 3;   // combo multiplier
        private const byte BTN_LABEL = 4;   // drift label text
        private const byte BTN_AWARD = 5;   // flash on drift endu

        private const byte BTN_RIGHTIND = 6;   // flash on drift end
        private const byte BTN_LEFTIND = 7;   // flash on drift end
        private const byte BTN_RPMLIMIT = 8;   // revlimitter value
        private const byte BTN_RPMLIMITSTATUS = 9;   // revlimitter status
        private const byte BTN_RPMLIMITINFO = 10;   // revlimitter info

        // ── Timer for award flash ─────────────────────────────
        private System.Windows.Forms.Timer _awardTimer;
        private int _awardTick = 0;

        // ── UI Controls ───────────────────────────────────────
        private SpeedometerControl _speedometer;
        private Label _speedLabel, _speedUnitLabel;
        private Label _angleLabel, _angleValueLabel;
        private Label _comboLabel, _comboValueLabel;
        private Label _scoreLabel, _scoreValueLabel;
        private Label _runLabel, _runValueLabel;
        private Label _statusLabel;
        private TextBox _hostBox, _adminBox;
        private NumericUpDown _portBox;
        private Button _resetBtn;
        private MacToggleSwitch _revEnableSwitch;
        private MacToggleSwitch _connectionSwitch;
        private MacToggleSwitch _themeSwitch;
        private Label _connectionStateLabel;
        private MacCheckBox _showHudCheck;
        private MacCheckBox _showRPMHudCheck;
        private MacCheckBox _showOverlayCheck;


        // ── UI Colors ─────────────────────────────────────────

        private string InSimColor1 = "^7";
        private string InSimColor2 = "^6";
        private string InSimColor3 = "^3";
        private string InSimColor4 = "^5";
        private string InSimColor5 = "^1";

        private string colorCurrentMain = "^7";

        private Button _colorBtn1;
        private Button _colorBtn2;
        private Button _colorBtn3;
        private Button _colorBtn4;
        private Button _colorBtn5;
        private Button _calibrateBtn1;
        private Button revBindingsBtn;
        private Button _langBtn;

        // ── Kierunkowskazy ────────────────────────────────────
        private Label _indicatorStatusLabel;
       
        private Button _bindLeftBtn, _bindRightBtn, _bindHazardBtn, _bindLightBtn;
        private CheckBox _autoReturnCheck;
        
        private Label _tireStatusLabel;
        private Button _tirePatchBtn;
        private NumericUpDown _steeringThresholdBox;
        private Label _indicatorDisplayLabel;

        private Label _wheelAngleLabel;

        #region UI

        private RoundedPanel headerPanel;

        private RoundedPanel speedometerPanel;
        private RoundedPanel telemetryPanel;

        private RoundedPanel scorePanel;

        private RoundedPanel connectionPanel;
        private RoundedPanel hudPanel;

        private RoundedPanel revLimiterPanel;
        private RoundedPanel indicatorPanel;

        private RoundedPanel statusPanel;
        #endregion


        private WindowShadow _shadow;

        // np. na końcu BuildUI() albo w konstruktorze po InitializeComponent()


       

    public MainForm()
        {

            _insim = new InSimConnection();

            _insim.CarDataReceived += OnCarData;
            _insim.StatusChanged += OnStatus;
            _insim.Connected += OnConnected;
            _insim.Disconnected += OnDisconnected;
            _insim.RaceStateChanged += (s, inRace) => BeginInvoke((Action)(() =>
            {
                if (inRace)
                    InitInGameHUD();      // pojawiają się przyciski
                else
                    _insim.DeleteAllButtons(); // znikają w menu/powtórce
            }));

            _insim.LapCompleted += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.OnLapCompleted();
                    _overlay.UpdateBestLapScore(_drift.BestLapScore);
                    _overlay.SetLapBoxVisible(true);   // pierwsze zaliczone okrążenie = pokaż ramkę
                }
            }));

            _insim.TrackChanged += (s, track) => BeginInvoke((Action)(() =>
            {
                _drift.SetTrack(track);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();   // ← NOWE
            }));

            _insim.LayoutChanged += (s, layout) => BeginInvoke((Action)(() =>
            {
                _drift.SetLayout(layout);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();   // ← NOWE
            }));
            _insim.CarReset += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    _drift.ResetLapScore();
                    _overlay.UpdateLapScore(_drift.LapScore);
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
                }
            }));

            _insim.RaceRestarted += (s, e) => BeginInvoke((Action)(() =>
            {
                _drift.OnRaceRestarted();
                _overlay.UpdateLapScore(_drift.LapScore);
                _overlay.SetLapBoxVisible(false);
                //_overlay.SetLapBoxVisible(_drift.HasActiveLapContext);   // schowaj do 1. okrążenia nowego wyścigu
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

            _drift.SetDriver(Environment.UserName);
            _revLimiter.DataReceived += OnRevData;

            //_revLimiter.CutStarted += () => BeginInvoke((Action)(() => _revLimitLabel.Text = _revLimiter.RpmLimit.ToString()));
            _revLimiter.CutStarted += () => BeginInvoke((Action)(() =>
            {
                _revCutLabel.Text = "⚡ CUT";
                _overlay.SetRevCut(true);
            }));
            _revLimiter.CutEnded += () => BeginInvoke((Action)(() =>
            {
                _revCutLabel.Text = "";
                _overlay.SetRevCut(false);
            }));
            _revLimiter.Error += msg => BeginInvoke((Action)(() => _statusLabel.Text = msg));

            // ── Tire Temperature Limiter ──────────────────────────────
           // _tireTemperatureLimiter.Log += msg => BeginInvoke((Action)(() => _statusLabel.Text = msg));
           // _tireTemperatureLimiter.PatchStatusChanged += status =>
           //     BeginInvoke((Action)(() => UpdateTireLimiterUI(status)));
            try
            {
                _indicatorClick.LoadAsync();
                _indicatorCancel.LoadAsync();
            }
            catch { }

            _drift.DriftScored += OnDriftScored;
            _drift.DriftStarted += OnDriftStarted;
            _drift.DriftEnded += OnDriftEnded;

            _indicators.StateChanged += OnIndicatorStateChanged;

            _awardTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _awardTimer.Tick += AwardTimer_Tick;

            InitializeComponent();

            SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
       
            _shadow = new WindowShadow(this); // <-- no Owner = this

            this.Load += (s, e) => _shadow.Reposition();
            this.Move += (s, e) => _shadow.Reposition();
            this.Resize += (s, e) => _shadow.Reposition();
            this.Activated += (s, e) => _shadow.Reposition();   // re-pin z-order whenever we regain focus
            this.VisibleChanged += (s, e) => { if (Visible) _shadow.Reposition(); else _shadow.Hide(); };
            this.Resize += (s, e) =>
            {
                if (Width > 0 && Height > 0)
                {
                    //EnableLayeredWindowMode();
                    //this.Region = CreateSmoothRoundedRegion(Width, Height, 20);
                }
                    
            };
            //this.Region = CreateSmoothRoundedRegion(this.Width, this.Height, 20);
            _overlay = new OverlayForm();
            _overlay.ComboTimeoutSec = DriftEngine.COMBO_TIMEOUT_SEC;
            Localization.LanguageChanged += ApplyLanguage;

            BuildUI();

            LoadSettings();
            
            
        }






        private SteeringWheelInput _wheelInput;
        private GlobalHotkey _globalHotkey;
        private RevLimiter _revLimiter = new RevLimiter();
        private TireTemperatureLimiter _tireTemperatureLimiter = new TireTemperatureLimiter();


        private void ApplyLanguage()
        {
            foreach (var (ctrl, key) in _localizedControls)
            {
                ctrl.Text = Localization.T(key);
            }

            // teksty ustawiane ręcznie / dynamicznie poza rejestrem:
            _connectionStateLabel.Text = _insim.IsConnected
                ? Localization.T("status.connected")
                : Localization.T("status.disconnected");
            _langBtn.Text = Localization.LanguageDisplayName(Localization.CurrentLanguage);
            _statusLabel.Text = Localization.T("status.hint");
        }

        // Buduje Region z antyaliasowanej maski (supersampling), dzięki czemu krawędzie
        // wyglądają gładko mimo że Region i tak jest binarny (piksel wewnątrz/na zewnątrz).
        // Zamiast pojedynczej linii łuku, próbkujemy w wyższej rozdzielczości i uśredniamy,
        // przez co próg "wewnątrz/na zewnątrz" przebiega bliżej rzeczywistego okręgu
        // na poziomie sub-piksela, a widoczne "schodki" są dużo drobniejsze.
        private Region CreateSmoothRoundedRegion(int width, int height, int radius)
        {
            const int scale = 4; // supersampling 4x

            int sw = width * scale;
            int sh = height * scale;
            int sr = radius * scale;

            using var mask = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(mask))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; // liczy się gęstość próbek, nie AA
                g.Clear(Color.Transparent);
                using var path = RoundedPath(new Rectangle(0, 0, sw - 1, sh - 1), sr);
                using var brush = new SolidBrush(Color.Black);
                g.FillPath(brush, path);
            }

            var region = new Region(Rectangle.Empty); // start: puste

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

                    // kopiujemy tylko jeden potrzebny wiersz próbkowanej bitmapy
                    IntPtr rowPtr = bits.Scan0 + sampleY * stride;
                    Marshal.Copy(rowPtr, rowBuffer, 0, stride);

                    int runStart = -1;
                    for (int x = 0; x <= width; x++)
                    {
                        bool filled = false;
                        if (x < width)
                        {
                            int sampleX = Math.Min(x * scale + scale / 2, sw - 1);
                            byte alpha = rowBuffer[sampleX * 4 + 3]; // kanał A w BGRA
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
        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    SetButtonColor(_colorBtn1, Color.White);
                    SetButtonColor(_colorBtn2, Color.Cyan);
                    SetButtonColor(_colorBtn3, Color.Yellow);
                    SetButtonColor(_colorBtn4, Color.Magenta);
                    SetButtonColor(_colorBtn5, Color.Red);
                    ApplyTheme(false);
                    return;
                }



                string json = File.ReadAllText(SettingsFile);

                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(json);

                if (settings == null)
                    return;

                CalibratedMAXRPM = settings.CalibratedMAXRPM;
                SavedMSCUT = settings.SavedMSCUT;
                _revCutMS.Value = settings.SavedMSCUT;
                _revLimiter.RpmLimit = settings.CalibratedMAXRPM;
                _revLimiterNumeric.Value = CalibratedMAXRPM;

                InSimColor1 = settings.InSimColor1;
                InSimColor2 = settings.InSimColor2;
                InSimColor3 = settings.InSimColor3;
                InSimColor4 = settings.InSimColor4;
                InSimColor5 = settings.InSimColor5;

                

                SetButtonColor(_colorBtn1, InSimCodeToColor(InSimColor1));
                SetButtonColor(_colorBtn2, InSimCodeToColor(InSimColor2));
                SetButtonColor(_colorBtn3, InSimCodeToColor(InSimColor3));
                SetButtonColor(_colorBtn4, InSimCodeToColor(InSimColor4));
                SetButtonColor(_colorBtn5, InSimCodeToColor(InSimColor5));

                ApplyTheme(settings.DarkTheme);
                if (_themeSwitch != null)
                    _themeSwitch.SetCheckedSilent(settings.DarkTheme);

                Localization.SetLanguage(
                    Enum.Parse<AppLanguage>(settings.Language));

                // after
                ApplyRevBinding(ref _revToggleBinding, settings.RevToggleBinding, ExecuteRevToggle);
                ApplyRevBinding(ref _revCalibrateBinding, settings.RevCalibrateBinding, ExecuteRevCalibrate);
                ApplyRevBinding(ref _revDecreaseBinding, settings.RevDecreaseBinding, ExecuteRevDecrease);
                ApplyRevBinding(ref _revIncreaseBinding, settings.RevIncreaseBinding, ExecuteRevIncrease);
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

                if (Guid.TryParse(settings.SteeringWheelDeviceGuid, out var savedGuid) && savedGuid != Guid.Empty)
                {
                    if (Enum.TryParse<JoystickOffset>(settings.SteeringWheelAxis, out var savedAxis))
                    {
                        _savedWheelGuid = savedGuid;
                        _savedWheelAxis = savedAxis;
                        _wheelInput.ConnectToDevice(savedGuid, savedAxis);
                    }
                }
            }
            catch
            {
            }
        }


        private void SaveSettings()
        {
            try
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value;

                SavedMSCUT = (int)_revCutMS.Value;



                AppSettings settings = new AppSettings()
                {
                    CalibratedMAXRPM = CalibratedMAXRPM,
                    SavedMSCUT = SavedMSCUT,

                    Language = Localization.CurrentLanguage.ToString(),

                    DarkTheme = _isDarkTheme,

                    InSimColor1 = InSimColor1,
                    InSimColor2 = InSimColor2,
                    InSimColor3 = InSimColor3,
                    InSimColor4 = InSimColor4,
                    InSimColor5 = InSimColor5,

                    RevToggleBinding = _revToggleBinding,
                    LightToggleBinding = _lightToggleBinding,
                    RevCalibrateBinding = _revCalibrateBinding,
                    RevDecreaseBinding = _revDecreaseBinding,
                    RevIncreaseBinding = _revIncreaseBinding,
                    IndicatorLeftBinding = GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton),
                    IndicatorRightBinding = GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton),
                    IndicatorHazardBinding = GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton),


                    SteeringWheelDeviceGuid = _savedWheelGuid.ToString(),
                    SteeringWheelAxis = _savedWheelAxis.ToString(),

                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions()
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
            }
        }

        private void OnRevData(OutGaugeData data)
        {
            BeginInvoke((Action)(() =>
            {
                _drift.SetHandbrakeActive(data.HandbrakeOn);
                _rpmLabel.Text = ((int)data.RPM).ToString("N0");
                _overlay.UpdateRpm((int)data.RPM);
                _rpmBar.Value = (int)Math.Min(data.RPM, _rpmBar.Maximum);
                

                // Podświetl czerwono gdy blisko limitu
                _rpmLabel.ForeColor = data.RPM >= _revLimiter.RpmLimit * 0.95
                    ? Color.FromArgb(255, 60, 60)
                    : Color.FromArgb(80, 220, 120);

                if (calibrationON == true)
                {
                    if (data.RPM > CalibratedMAXRPM) { CalibratedMAXRPM = (int)data.RPM; } else { }
                }
                //_revLimiterNumeric.Value = CalibratedMAXRPM;
                ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());



            }));
        }

        private bool _isDarkTheme = false;

        private void ApplyTheme(bool dark)
        {
            _isDarkTheme = dark;
            ApplePalette.SetDark(dark);

            this.BackColor = ApplePalette.Background;
           

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

                    case Panel p when p.Tag as string == "theme:border":
                        p.BackColor = ApplePalette.Border;
                        break;

                    case Panel p when p.Tag as string == "theme:card":
                        p.BackColor = ApplePalette.Card;
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
            // zapamiętaj oryginalny (jasny motyw) kolor tylko raz, przy pierwszym przełączeniu
            if (lbl.Tag == null)
                lbl.Tag = lbl.ForeColor;

            if (!(lbl.Tag is Color original))
                return;

            if (!_isDarkTheme)
            {
                lbl.ForeColor = original;
                return;
            }

            // ciemne, "tekstowe" szarości (zaprojektowane pod biały motyw) rozjaśniamy,
            // żeby były czytelne na ciemnym tle; jasne kolory akcentów (RPM, score...) zostają bez zmian
            double luma = (0.299 * original.R + 0.587 * original.G + 0.114 * original.B) / 255.0;
            lbl.ForeColor = luma < 0.55 ? Lighten(original, 0.65) : original;
        }


        public bool indClickONOFF = false;
        // ─────────────────────────────────────────────────────
        //  UI
        // ─────────────────────────────────────────────────────

        // Kolor "niemożliwy" do przypadkowego wystąpienia w UI (brak magenty w ApplePalette)
        private static readonly Color WindowKeyColor = Color.FromArgb(255, 255, 0, 254);

        // wywołaj raz, np. w BuildUI() przed ustawieniem Region
        private void EnableLayeredWindowMode()
        {
            this.BackColor = WindowKeyColor;
            this.TransparencyKey = WindowKeyColor;
        }

        private Color ButtonColor => _isDarkTheme
    ? Color.FromArgb(70, 70, 90)     // ciemny motyw — bez zmian
    : Color.FromArgb(205, 205, 220); // jasny motyw — jasny szaro-fiolet
        private void BuildUI()
        {

            var version = Assembly
                      .GetExecutingAssembly()
                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                      ?.InformationalVersion;

            string appVersion = $"v.{version?.Split('+')[0] ?? "Unknown"}.alpha";

        int marginTop = 40;


            SuspendLayout();

            Text = "LFS Drift Tools";

            StartPosition = FormStartPosition.CenterScreen;

            Size = new Size(890, 600 + TitleBarHeight);

            MinimumSize = new Size(890, 600 + TitleBarHeight);

            MaximumSize = Size;

            DoubleBuffered = true;

            BackColor = Color.Black;

            Font = new Font("Segoe UI", 10f);

            //Region = CreateSmoothRoundedRegion(Width, Height, 20);

      


            _wheelInput = new SteeringWheelInput(
                this.Handle,
                (0, () => BeginInvoke((Action)(() => { ShowInGameAward("0"); }))),
                (1, () => BeginInvoke((Action)(() => { ShowInGameAward("1"); }))),
                (2, () => BeginInvoke((Action)(() => { ShowInGameAward("2"); }))),
                (3, () => BeginInvoke((Action)(() => { ShowInGameAward("3"); }))),
                (4, () => BeginInvoke((Action)(() => { ShowInGameAward("4"); }))),
                (5, () => BeginInvoke((Action)(() => { ShowInGameAward("5"); }))),
                (6, () => BeginInvoke((Action)(() => { ShowInGameAward("6"); }))),
                (7, () => BeginInvoke((Action)(() => { ShowInGameAward("7"); }))),
                (8, () => BeginInvoke((Action)(() => { ShowInGameAward("8"); }))),
                (9, () => BeginInvoke((Action)(() => { ShowInGameAward("9"); }))),
                (10, () => BeginInvoke((Action)(() => 
                {
                    if (!_connectionSwitch.Checked)
                    {
                        _insim.Connect(_hostBox.Text.Trim(), (int)_portBox.Value, _adminBox.Text);
                    }

                }))),
                (11, () => BeginInvoke((Action)(() => { ShowInGameAward("11"); }))),
                (12, () => BeginInvoke((Action)(() => { ShowInGameAward("12"); }))),
                (13, () => BeginInvoke((Action)(() => { ShowInGameAward("13"); }))),
                (14, () => BeginInvoke((Action)(() => { ShowInGameAward("14"); }))),
                (15, () => BeginInvoke((Action)(() => { ShowInGameAward("15"); }))),
                (16, () => BeginInvoke((Action)(() => { ShowInGameAward("16"); })))

            );

            _wheelInput.WheelNotFound += devices =>
            BeginInvoke((Action)(() => ShowWheelSetupDialog(devices)));

            headerPanel = CreateCard(
                "",
                20,
                20 + TitleBarHeight,
                420,
                110, locKey: "header.title");


            MakeLabel(headerPanel, appVersion, 21, 78, 200, 18,
                      Color.FromArgb(140, 140, 170));
            _connectionStateLabel = MakeLabel(headerPanel, "", 20, 60, 200, 16, ApplePalette.Text, new Font("Segoe UI", 8f), locKey: "status.disconnected");

            _langBtn = MakeButton(headerPanel, Localization.LanguageDisplayName(Localization.CurrentLanguage),
            310, 70, 100, 30);
            _langBtn.Click += (s, e) => OpenLanguagePicker(_langBtn);

            //_localizedControls.Add((_langBtn, null)); // patrz uwaga niżej — obsłużymy ręcznie

            MakeLabel(headerPanel, "COLORS", 300, 14, 55, 22,
            Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "header.theme");

            
            _themeSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
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
                450,
                20 + TitleBarHeight,
                420,
                110, locKey: "connection.title");


            var connectionLabel = MakeLabel(connectionPanel, "STATUS", 300, 14, 55, 22,
            Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

            _connectionSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = false // start zawsze OFF
            };

            _connectionSwitch.CheckedChanged += (s, e) =>
            {
                if (_connectionSwitch.Checked)
                {
                    _insim.Connect(_hostBox.Text.Trim(), (int)_portBox.Value, _adminBox.Text);
                }
                else
                {
                    // blokada przełącznika na czas rozłączania, żeby użytkownik
                    // nie zmienił stanu w trakcie sekwencji
                    _connectionSwitch.Enabled = false;

                    Task.Run(() =>
                    {
                        _insim.DeleteAllButtons();
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



            MakeLabel(connectionPanel, "Host:", 24, 55, 45, 15, ApplePalette.Text, locKey: "connection.host");
            _hostBox = MakeTextBox(connectionPanel, "127.0.0.1", 24, 70, 100, 30);

            MakeLabel(connectionPanel, "Port:", 140, 55, 38, 15, ApplePalette.Text, locKey: "connection.port");
            _portBox = MakeNumericUpDown(
                connectionPanel,
                140,
                70,
                78,
                30,
                1,
                65535,
                29999);

            MakeLabel(connectionPanel, "Admin password:", 235, 55, 200, 15, ApplePalette.Text, locKey: "connection.adminpass");
            _adminBox = MakeTextBox(connectionPanel, "", 235, 70, 140, 30);


            speedometerPanel = CreateCard(
                "Speedometer",
                20,
                330 + TitleBarHeight,
                240,
                180, locKey: "speedometer.title");


            // ── Speedometer ───────────────────────────────────
            _speedometer = new SpeedometerControl
            {
                Location = new Point(140, 55),
                Size = new Size(280, 130),
                BackColor = Color.White
            };
            // speedometerPanel.Controls.Add(_speedometer);

            _speedLabel = MakeLabel(speedometerPanel, "0", 20, 85, 70, 25, Color.FromArgb(255, 200, 50), new Font("Segoe UI", 24f, FontStyle.Bold));
            _speedUnitLabel = MakeLabel(speedometerPanel, "km/h", 20, 120, 50, 20, ApplePalette.Text);
            MakeLabel(speedometerPanel, "SPEED", 20, 60, 50, 20, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), locKey: "speedometer.speed");

            var marginSide = 10;


            scorePanel = CreateCard(
                "Drift Score",
                20,
                140 + TitleBarHeight,
                420,
                180, locKey: "score.title");


            MakeLabel(scorePanel, "TOTAL SCORE", 10 + marginSide, marginTop + 20, 110, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.total");
            _scoreValueLabel = MakeLabel(scorePanel, "0", 10 + marginSide, marginTop + 40, 140, 20, Color.FromArgb(255, 200, 50), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "RUN SCORE", 10 + marginSide, marginTop + 70, 100, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.run");
            _runValueLabel = MakeLabel(scorePanel, "0", 10 + marginSide, marginTop + 90, 140, 20, Color.FromArgb(80, 220, 120), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "COMBO", 160 + marginSide, marginTop + 20, 100, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.combo");
            _comboValueLabel = MakeLabel(scorePanel, "x1", 160 + marginSide, marginTop + 40, 100, 20, Color.FromArgb(255, 120, 40), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "DRIFT ANGLE", 160 + marginSide, marginTop + 70, 100, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.angle");
            _angleValueLabel = MakeLabel(scorePanel, "0°", 160 + marginSide, marginTop + 90, 50, 20, ApplePalette.Text, new Font("Segoe UI", 14f, FontStyle.Bold));


            _resetBtn = MakeButton(scorePanel, "RESET SCORE", 240 + marginSide, 140, 160, 30, Color.FromArgb(200, 20, 20), locKey: "score.reset");

            _resetBtn.Enabled = false;


            _resetBtn.Click += (s, e) => { _drift.ResetTotal(); UpdateScoreLabels(); };


            hudPanel = CreateCard(
                "HUD Settings",
                270,
                330 + TitleBarHeight,
                280,
                180, locKey: "hud.title");


            _colorBtn1 = MakeButton(hudPanel, "", 15, 55, 45, 45, Color.White);
            _colorBtn2 = MakeButton(hudPanel, "", 65, 55, 45, 45, Color.Cyan);
            _colorBtn3 = MakeButton(hudPanel, "", 115, 55, 45, 45, Color.Yellow);
            _colorBtn4 = MakeButton(hudPanel, "", 165, 55, 45, 45, Color.Magenta);
            _colorBtn5 = MakeButton(hudPanel, "", 215, 55, 45, 45, Color.Red);


            _colorBtn1.Click += (s, e) =>
             OpenInSimPalette(_colorBtn1, c =>
             {
                 InSimColor1 = c;
                 SaveSettings();
             });
            _colorBtn2.Click += (s, e) =>
             OpenInSimPalette(_colorBtn2, c =>
             {
                 InSimColor2 = c;
                 SaveSettings();
             });
            _colorBtn3.Click += (s, e) =>
             OpenInSimPalette(_colorBtn3, c =>
             {
                 InSimColor3 = c;
                 SaveSettings();
             });
            _colorBtn4.Click += (s, e) =>
             OpenInSimPalette(_colorBtn4, c =>
             {
                 InSimColor4 = c;
                 SaveSettings();
             });
            _colorBtn5.Click += (s, e) =>
             OpenInSimPalette(_colorBtn5, c =>
             {
                 InSimColor5 = c;
                 SaveSettings();
             });


            _showOverlayCheck = new MacCheckBox
            {
                Text = "Forza-style overlay",
                Location = new Point(16, 155),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = true
            };
            hudPanel.Controls.Add(_showOverlayCheck);

            _showHudCheck = new MacCheckBox
            {
                Text = "Show ingame HUD (IS_BTN)",

                Location = new Point(16, 105),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = false

            };
            _showHudCheck.CheckedChanged += (s, e) =>
            {
                if (!_showHudCheck.Checked)
                {
                    _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                    _insim.ShowButton(BTN_AWARD, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                    _insim.ShowButton(BTN_COMBO, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                    _insim.ShowButton(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                    _insim.ShowButton(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                    _insim.ShowButton(BTN_RIGHTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                    _insim.ShowButton(BTN_LEFTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                }
            };

            _showRPMHudCheck = new MacCheckBox
            {
                Text = "Show ingame REV Limitter HUD",

                Location = new Point(16, 130),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = true

            };
            hudPanel.Controls.Add(_showHudCheck);
            hudPanel.Controls.Add(_showRPMHudCheck);
            _localizedControls.Add((_showHudCheck, "hud.show"));
            _localizedControls.Add((_showRPMHudCheck, "hud.REVLimitter"));

            revLimiterPanel = CreateCard(
                "Rev Limiter",
                450,
                140 + TitleBarHeight,
                420,
                180, locKey: "rev.title");

            

            revBindingsBtn = MakeButton(revLimiterPanel, "⚙", 255, 5, 40, 40);
            revBindingsBtn.Click += (s, e) => OpenRevLimiterBindings();
            MakeLabel(revLimiterPanel, "Bind functions using the gear icon.", 15, 55, 270, 18,
                      Color.FromArgb(140, 140, 170), null, ContentAlignment.MiddleLeft, locKey: "rev.calibratehint");

            

            _calibrateBtn1 = MakeButton(revLimiterPanel, "CALIBRATE", 300, 70, 110, 30, locKey: "rev.calibrate");

            _calibrateBtn1.Click += async (s, e) => await RPMLimitterCalibrate();

            _globalHotkey = new GlobalHotkey(); // domyślnie brak bindowań

            MakeLabel(revLimiterPanel, "RPM:", 15, marginTop + 40, 40, 22, ApplePalette.Text, null, ContentAlignment.MiddleLeft, locKey: "rev.rpm");
            _rpmLabel = MakeLabel(revLimiterPanel, "0", 58, marginTop + 35, 80, 30, null, new Font("Segoe UI", 18f, FontStyle.Bold), ContentAlignment.MiddleLeft);
            _revCutLabel = MakeLabel(revLimiterPanel, "", 140, marginTop + 35, 80, 30,
                ApplePalette.Text, new Font("Segoe UI", 14f, FontStyle.Bold));

            // RPM bar
            _rpmBar = new MacProgressBar
            {
                Location = new Point(15, marginTop + 70),
                Size = new Size(390, 18), // macOS lubi cienkie paski (~6-8px)
                Minimum = 0,
                Maximum = CalibratedMAXRPM,
                Value = 0
            };
            revLimiterPanel.Controls.Add(_rpmBar);

            // Ustawienia: próg RPM

            _revLimitLabel = MakeLabel(revLimiterPanel, "RPM LIMIT: ", 15, marginTop + 105, 70, 22, ApplePalette.Text, locKey: "rev.limit");

            //MakeLabel(revLimiterPanel, $"RPM limit: {_revLimiter.RpmLimit}", 50, marginTop + 0, 270, 22, Color.FromArgb(130, 130, 165));



            _revLimiterNumeric = MakeNumericUpDown(
                revLimiterPanel,
                85,
                marginTop + 100,
                80,
                30,
                500,
                20000,
                CalibratedMAXRPM,
                100,
                v => _revLimiter.RpmLimit = (int)v
            );
            //ShowInGameRPMLimitter((_revLimiterNumeric.Value).ToString());
            MakeLabel(revLimiterPanel, "Cut time [ms]:", 195, marginTop + 105, 80, 22, ApplePalette.Text, locKey: "rev.cutms");
            _revCutMS = MakeNumericUpDown(
                revLimiterPanel,
                280,
                marginTop + 100,
                80,
                30,
                25,
                500,
                SavedMSCUT,
                5,
                v => _revLimiter.CutMs = (int)v
            );

            var revEnableLabel = MakeLabel(revLimiterPanel, "STATUS", 300, 14, 55, 22,
            Color.FromArgb(60, 60, 60), null, ContentAlignment.MiddleRight, locKey: "connection.status");

            

            _revEnableSwitch = new MacToggleSwitch
            {
                Location = new Point(360, 10),
                Checked = _revLimiter.Enabled
            };

            _revEnableSwitch.CheckedChanged += (s, e) =>
            {
                _revLimiter.Enabled = _revEnableSwitch.Checked;

                if (!_revLimiter.IsRunning)
                    _revLimiter.Start();
            };

            revLimiterPanel.Controls.Add(_revEnableSwitch);



            indicatorPanel = CreateCard(
               "Turn Signals",
               560,
               330 + TitleBarHeight,
               310,
               180, locKey: "indicators.title");

            var wheelSetupBtn = MakeButton(indicatorPanel, "🎮", 260, 5, 40, 40);
            wheelSetupBtn.Click += (s, e) => ShowWheelSetupDialog(_wheelInput.GetAvailableDevices());


            _wheelInput.Log += msg => BeginInvoke((Action)(() => _statusLabel.Text = msg));
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

                if (indClickONOFF == true && pct < 5 && newState == IndicatorState.Right || indClickONOFF == true && pct > -5 && newState == IndicatorState.Left)
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

                if (indClickONOFF == false && pct >= 25 && newState == IndicatorState.Right || indClickONOFF == false && pct <= -25 && newState == IndicatorState.Left) 
                {
                    indClickONOFF = true;
                }
                

            }));


            // Wyświetlanie stanu kierunkowskazów
            _indicatorDisplayLabel = MakeLabel(indicatorPanel, "---", 15, 50, 150, 30,
                Color.FromArgb(100, 200, 255), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(indicatorPanel, "STEERING", 175, 55, 100, 14, ApplePalette.Text,
                new Font("Segoe UI", 7.5f, FontStyle.Bold), locKey: "indicators.steering");
            _wheelAngleLabel = MakeLabel(indicatorPanel, "0%", 175, 72, 100, 24,
                Color.FromArgb(255, 200, 50), new Font("Segoe UI", 14f, FontStyle.Bold));

            // Bindowanie klawiszy
            MakeLabel(indicatorPanel, "Key bind:", 15, 80, 140, 22, ApplePalette.Text, locKey: "indicators.keybind");
            _bindLeftBtn = MakeButton(indicatorPanel, "LEFT", 10, 140, 90, 26, locKey: "indicators.left");
            _bindRightBtn = MakeButton(indicatorPanel, "RIGHT", 105, 140, 90, 26, locKey: "indicators.right");
            _bindHazardBtn = MakeButton(indicatorPanel, "HAZARD", 200, 140, 100, 26, locKey: "indicators.hazard");
            _bindLightBtn = MakeButton(indicatorPanel, "LIGHTS", 200, 110, 100, 26, locKey: "indicators.lights");

            // after
            _bindLeftBtn.Click += (s, e) => BindKey("LEFT TURN SIGNAL", b =>
            {

                
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.LeftKeyCode = code,
                    wb => _indicators.LeftWheelButton = wb,
                    _indicators.LeftWheelButton,
                    () => _indicators.ToggleLeft());
                SaveSettings();
            });

            _bindRightBtn.Click += (s, e) => BindKey("RIGHT TURN SIGNAL", b =>
            {
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.RightKeyCode = code,
                    wb => _indicators.RightWheelButton = wb,
                    _indicators.RightWheelButton,
                    () => _indicators.ToggleRight());
                SaveSettings();
            });

            _bindHazardBtn.Click += (s, e) => BindKey("HAZARD SIGNALS", b =>
            {
                ApplyIndicatorBinding(
                    b,
                    code => _indicators.HazardKeyCode = code,
                    wb => _indicators.HazardWheelButton = wb,
                    _indicators.HazardWheelButton,
                    () => _indicators.ToggleHazard());
                SaveSettings();
            });
            _indicatorStatusLabel = MakeLabel(indicatorPanel, "", 15, 80, 270, 80,
               Color.FromArgb(150, 200, 100), new Font("Segoe UI", 9f));

            _bindLightBtn.Click += (s, e) => BindKey("LIGHTS TOGGLE", b =>
            {
                ApplyRevBinding(ref _lightToggleBinding, b, ExecuteLightToggle);
                SaveSettings();
            });

            statusPanel = CreateCard(
                "",
                20,
                520 + TitleBarHeight,
                850,
                60);

            _statusLabel = MakeLabel(statusPanel, "Type /insim 29999 w LFS, and click CONNECT.", 20, 25, 620, 18, ApplePalette.Text, new Font("Segoe UI", 8f), locKey: "status.hint");

            
            ResumeLayout();

            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;

            BuildTitleBar();

           
            this.Paint += MainForm_Paint;
            /*
            // ────────────────────────────────────────────────────────
            // Tire Temperature Limiter Section
            // ────────────────────────────────────────────────────────
            var tirePanel = MakePanel(100, 390, 450, 100, Color.FromArgb(22, 22, 35));
            tirePanel.Paint += BorderPaint(Color.FromArgb(60, 120, 180));

            _tireStatusLabel = MakeLabel(tirePanel, "❌ Not Patched", 10, 10, 280, 20,
                Color.FromArgb(150, 150, 150), new Font("Segoe UI", 9, FontStyle.Bold));

            _tirePatchBtn = MakeButton(tirePanel, "APPLY PATCH", 300, 10, 130, 25,
                Color.FromArgb(180, 60, 60));

            _tirePatchBtn.Click += (s, e) =>
            {
                try
                {
                    if (!_tireTemperatureLimiter.IsConnected)
                    {
                        _statusLabel.Text = "🔄 Connecting to LFS...";
                        if (!_tireTemperatureLimiter.Connect())
                        {
                            _statusLabel.Text = "❌ Failed to connect to LFS - check if it's running and you have admin rights";
                            return;
                        }
                    }

                    if (_tireTemperatureLimiter.IsPatched)
                    {
                        _tireTemperatureLimiter.Unpatch();
                    }
                    else
                    {
                        _tireTemperatureLimiter.Patch();
                    }
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = $"❌ Error: {ex.Message}";
                }
            };

            MakeLabel(tirePanel, "🌡️ Tire Temperature Limiting (LFS 0.8C)",
                10, 40, 420, 15, Color.FromArgb(100, 200, 255),
                new Font("Segoe UI", 8, FontStyle.Bold));

            MakeLabel(tirePanel, "Wyłącza wzrost temperatury opon dla lepszego driftingu",
                10, 58, 420, 15, Color.FromArgb(100, 100, 100),
                new Font("Segoe UI", 8));

            */
            //this.Region = CreateSmoothRoundedRegion(this.Width, this.Height, 20);
        }


        private void soundTest_Click(object sender, EventArgs e)
        {
            var path = Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click.wav");

            MessageBox.Show(File.Exists(path).ToString());

            var player = new SoundPlayer(path);
            player.PlaySync();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            
        }
        private void MainForm_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;

            // 1) Wypełnij całe tło BEZ AA — Region już przycina to do zaokrąglonego kształtu,
            //    więc nie trzeba tu drugi raz "zaokrąglać" ścieżką.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bgBrush = new SolidBrush(this.BackColor))
                g.FillRectangle(bgBrush, this.ClientRectangle);

            // 2) Dopiero teraz obwódka z AA, rysowana na świeżo wypełnionym tle
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedPath(rect, 2))
            using (Pen border = new Pen(ApplePalette.Border, 1.6f))
            {
                g.DrawPath(border, path);
            }
        }
        private void OpenLanguagePicker(Button anchorBtn)
        {

            Form overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,

                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
            };

            Form popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,

                ShowInTaskbar = false,
                Size = new Size(240, 320),
                BackColor = ApplePalette.Background
            };

            // Zaokrąglenie okna
            popup.Shown += (s, e) =>
            {
                popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 20);
            };


            var closeButton = new Button
            {
                Text = "x",
                Size = new Size(28, 28),
                Location = new Point(popup.Width - 38, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,

                FlatStyle = FlatStyle.Flat,
                FlatAppearance =
            {
                BorderSize = 0,
                MouseOverBackColor = Color.FromArgb(235, 235, 240),
                MouseDownBackColor = Color.FromArgb(220, 220, 225)
            },

                BackColor = Color.Transparent,
                ForeColor = ApplePalette.Secondary,
                Font = new Font("Segoe UI Semibold", 12f),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            closeButton.Click += (s, e) => popup.Close();


            // Główny panel (card)
            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill
            };

            popup.Controls.Add(card);

            // Tytuł
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

            closeButton.Region = CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);
           


            popup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // miękki cień
                for (int i = 30; i >= 1; i--)
                {
                    int alpha = (int)(22 * (1.0 - i / 30.0));

                    Rectangle shadowRect = new Rectangle(
                        12 - i,
                        12 - i,
                        popup.Width - 24 + i * 2,
                        popup.Height - 24 + i * 2);

                    using (GraphicsPath p = RoundedPath(shadowRect, 20 + i))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    {
                        e.Graphics.FillPath(b, p);
                    }
                }
            };
            // Pokazuje przyciemnienie
            overlay.Show();

            // Upewnij się, że popup jest nad overlayem
            popup.Owner = overlay;

            card.Controls.Add(closeButton);
            try
            {
                popup.ShowDialog(overlay);
            }
            finally
            {
                overlay.Close();
                overlay.Dispose();
            }
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
            BeginInvoke((Action)(() => { _revEnableSwitch.Checked = !_revEnableSwitch.Checked; }));
        }

        private void ExecuteRevCalibrate()
        {
            BeginInvoke((Action)(() =>
            {
                if (!calibrationON)
                    _ = RPMLimitterCalibrate();
            }));
        }

        private void ExecuteRevDecrease()
        {
            BeginInvoke((Action)(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value - 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            }));
        }

        private void ExecuteRevIncrease()
        {
            BeginInvoke((Action)(() =>
            {
                CalibratedMAXRPM = (int)_revLimiterNumeric.Value + 100;
                _revLimiterNumeric.Value = CalibratedMAXRPM;
                ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            }));
        }

        private InputBinding GetIndicatorBinding(int keyCode, int? wheelButton)
        {
            if (wheelButton.HasValue)
                return InputBinding.FromWheelButton(wheelButton.Value);

            return InputBinding.FromKey((Keys)keyCode);
        }

        // stosuje bindowanie kierunkowskazu: aktualizuje IndicatorManager + rejestruje w _wheelInput,
        // usuwając poprzednie bindowanie kierownicy jeśli istniało
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
                _wheelInput.SetBinding(newBinding.WheelButton, wheelAction);
            }
        }
        // usuwa poprzednie bindowanie (klawiatura lub kierownica) i rejestruje nowe
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
            CalibratedMAXRPM = 1200;

            calibrationON = true;

            bool wasEnabledBeforeCalibration = _revLimiter.Enabled;
            _revLimiter.Enabled = false;
            _revEnableSwitch.SetCheckedSilent(false);

            ShowInGameAward("REV LIMITTER CALIBRATION - SELECT NEUTRAL AND HOLD FULL THROTTLE!!!");

            _revLimitLabel.Text = "CALIBRATION... ";
            //PressWKey(true);
            //_revLimitLabel.ForeColor = Color.FromArgb(255, 60, 60);

            await Task.Delay(4000);   // nie blokuje UI

            calibrationON = false;

            CalibratedMAXRPM = CalibratedMAXRPM - 50;
            _revLimitLabel.Text = CalibratedMAXRPM.ToString();
            _revLimitLabel.Text = "RPM LIMIT: ";
            //_revLimitLabel.ForeColor = Color.FromArgb(60, 60, 60);
            _revLimiterNumeric.Value = CalibratedMAXRPM;

            _revLimiter.Enabled = wasEnabledBeforeCalibration;
            _revEnableSwitch.SetCheckedSilent(wasEnabledBeforeCalibration);
            ShowInGameAward($"RPM LIMIT: {CalibratedMAXRPM}");
            ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            await Task.Delay(1000);

            ShowInGameAward("REV LIMITTER CALIBRATION DONE!!!");
            await Task.Delay(1000);
            ShowInGameAward($"RPM LIMIT: {CalibratedMAXRPM}");
            await Task.Delay(1000);
            ShowInGameAward("REV LIMITTER CALIBRATION DONE!!!");


            //PressWKey(false);
            await Task.Delay(3000);
            ShowInGameAward($"");
            _revLimitLabel.Text = "RPM LIMIT: ";
            SaveSettings();
        }

        private RoundedPanel CreateCard(
            string title,
            int x,
            int y,
            int width,
            int height,
            string locKey = null)
        {
            RoundedPanel panel = new RoundedPanel();

            panel.Location = new Point(x, y);
            panel.Size = new Size(width, height);

            Controls.Add(panel);

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
                separator.Tag = "theme:border"; // pozwala rozpoznać separator przy zmianie motywu

                panel.Controls.Add(separator);
            }

            return panel;
        }

        private Guid _savedWheelGuid = Guid.Empty;
        private JoystickOffset _savedWheelAxis = JoystickOffset.X;
        private void ShowWheelSetupDialog(List<(Guid Guid, string Name)> devices)
        {
            using var dlg = new WheelSetupForm(_wheelInput, devices);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _savedWheelGuid = dlg.SelectedDeviceGuid;
                _savedWheelAxis = dlg.SelectedAxis;
                SaveSettings();
                _statusLabel.Text = $"Kierownica skonfigurowana: {_wheelInput.DeviceName} (oś: {_wheelInput.SteeringAxis})";
            }
        }

        // Pomocna metoda do bindowania klawiszy
        // after
        private void BindKey(string keyName, Action<InputBinding> onBound)
        {
            // Niestandardowa forma do bindowania — obsługuje klawiaturę i przyciski kierownicy
            var dialog = new KeyBindingForm(keyName, _wheelInput);

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                InputBinding result = dialog.Result;

                bool isValidKeyboard = result.Kind == InputKind.Keyboard && result.Key != Keys.Escape;
                bool isValidWheel = result.Kind == InputKind.WheelButton;

                if (isValidKeyboard || isValidWheel)
                {
                    onBound(result);
                    _statusLabel.Text = string.Format(
                        Localization.T("status.keybound"),
                        keyName,
                        result.ToString());
                }
            }
        }
        // ────────────────────────────────────────────────────────
        // Dialog konfiguracji kierownicy — wybór urządzenia + kalibracja osi skrętu
        // ────────────────────────────────────────────────────────
        public class WheelSetupForm : Form
        {
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

            public WheelSetupForm(SteeringWheelInput wheelInput, List<(Guid Guid, string Name)> devices)
            {
                _wheelInput = wheelInput;

                _devices = devices;

                Text = Localization.T("wheelconfig.title");
                Size = new Size(380, 430);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                BackColor = ApplePalette.Background;

                ShowDeviceList();
            }

            private void ShowDeviceList()
            {
                Controls.Clear();

                var title = new Label
                {
                    Text = Localization.T("wheelconfig.nodetectauto"),
                    Location = new Point(20, 15),
                    Size = new Size(330, 40),
                    ForeColor = ApplePalette.Title,
                    Font = new Font("Segoe UI Semibold", 10f)
                };
                Controls.Add(title);

                int y = 65;

                if (_devices.Count == 0)
                {
                    Controls.Add(new Label
                    {
                        Text = Localization.T("wheelconfig.nodetect"),
                        Location = new Point(20, y),
                        Size = new Size(330, 40),
                        ForeColor = ApplePalette.Secondary
                    });
                }

                foreach (var d in _devices)
                {
                    var btn = new Button
                    {
                        Text = d.Name,
                        Location = new Point(20, y),
                        Size = new Size(330, 34),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = ApplePalette.Blue,
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI", 9.5f),
                        Cursor = Cursors.Hand
                    };
                    btn.FlatAppearance.BorderSize = 0;

                    var guid = d.Guid; // capture
                    btn.Click += (s, e) =>
                    {
                        SelectedDeviceGuid = guid;
                        ShowAxisCalibration();
                    };

                    Controls.Add(btn);
                    y += 42;
                }

                var cancelBtn = new Button
                {
                    Text = Localization.T("wheelconfig.abort"),
                    Location = new Point(20, 340),
                    Size = new Size(330, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(90, 90, 100),
                    ForeColor = Color.White
                };
                cancelBtn.FlatAppearance.BorderSize = 0;
                cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancelBtn);
            }

            private void ShowAxisCalibration()
            {
                Controls.Clear();

                // podłącz od razu z osią domyślną X — użytkownik zaraz ją potwierdzi lub zmieni
                _wheelInput.ConnectToDevice(SelectedDeviceGuid, JoystickOffset.X);

                var title = new Label
                {
                    Text = Localization.T("wheelconfig.calibrateinfo"),
                    Location = new Point(20, 15),
                    Size = new Size(330, 40),
                    ForeColor = ApplePalette.Title,
                    Font = new Font("Segoe UI Semibold", 10f)
                };
                Controls.Add(title);

                _axisBars = new Label[AxisChoices.Length];
                int y = 65;
                foreach (var axis in AxisChoices)
                {
                    int idx = Array.IndexOf(AxisChoices, axis);
                    var lbl = new Label
                    {
                        Text = $"{axis}: 0",
                        Location = new Point(20, y),
                        Size = new Size(330, 20),
                        ForeColor = ApplePalette.Text,
                        Font = new Font("Consolas", 9.5f)
                    };
                    Controls.Add(lbl);
                    _axisBars[idx] = lbl;
                    y += 24;
                }

                _calibHint = new Label
                {
                    Text = Localization.T("wheelconfig.detection"),
                    Location = new Point(20, y + 8),
                    Size = new Size(330, 20),
                    ForeColor = ApplePalette.Secondary
                };
                Controls.Add(_calibHint);

                var manualLabel = new Label
                {
                    Text = Localization.T("wheelconfig.manualset"),
                    Location = new Point(20, y + 34),
                    Size = new Size(250, 18),
                    ForeColor = ApplePalette.Secondary
                };
                Controls.Add(manualLabel);

                int mx = 20, my = y + 58;
                foreach (var axis in AxisChoices)
                {
                    var b = new Button
                    {
                        Text = axis.ToString(),
                        Location = new Point(mx, my),
                        Size = new Size(105, 28),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = ApplePalette.Card,
                        ForeColor = ApplePalette.Text,
                        Cursor = Cursors.Hand
                    };
                    b.FlatAppearance.BorderColor = ApplePalette.Border;
                    b.FlatAppearance.BorderSize = 1;
                    var ax = axis;
                    b.Click += (s, e) => Confirm(ax);
                    Controls.Add(b);

                    mx += 112;
                    if (mx > 260) { mx = 20; my += 34; }
                }

                var backBtn = new Button
                {
                    Text = Localization.T("wheelconfig.otherdevice"),
                    Location = new Point(20, 370),
                    Size = new Size(330, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(90, 90, 100),
                    ForeColor = Color.White
                };
                backBtn.FlatAppearance.BorderSize = 0;
                backBtn.Click += (s, e) => { StopCalibration(); ShowDeviceList(); };
                Controls.Add(backBtn);

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

                    // oś z największym zakresem ruchu podczas kalibracji = najprawdopodobniej skręt
                    JoystickOffset best = JoystickOffset.X;
                    int bestRange = -1;
                    foreach (var axis in AxisChoices)
                    {
                        int range = (_axisMax.TryGetValue(axis, out var mx2) ? mx2 : 0)
                                  - (_axisMin.TryGetValue(axis, out var mn) ? mn : 0);
                        if (range > bestRange) { bestRange = range; best = axis; }
                    }

                    if (bestRange > 2000) // realny ruch, nie szum na spoczynkowej osi
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

        // ────────────────────────────────────────────────────────
        // Niestandardowy dialog do bindowania klawiszy
        // ────────────────────────────────────────────────────────
       
        public class KeyBindingForm : Form
        {
            public InputBinding Result { get; private set; } = InputBinding.None;
            private Label _instructionLabel;
            private readonly SteeringWheelInput _wheelInput;
            
            public KeyBindingForm(string keyName, SteeringWheelInput wheelInput = null)
            {
                _wheelInput = wheelInput;

                Text = Localization.T("keybind.dialog.title");
                Size = new Size(350, 150);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                BackColor = Color.FromArgb(20, 20, 30);
                KeyPreview = true;  // ← WAŻNE! Pozwala formie przechwycić wszystkie klawisze

                _instructionLabel = new Label
                {
                    Text = string.Format(Localization.T("keybind.dialog.press"), keyName),
                    Dock = DockStyle.Fill,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 11f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false
                };
                Controls.Add(_instructionLabel);

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
                // event dochodzi z Timer.Tick, czyli już na wątku UI — bez BeginInvoke
                Result = InputBinding.FromWheelButton(buttonIndex);

                _instructionLabel.Text = $"Bindowano: Wheel Btn {buttonIndex}\n\nKlikaj by wrócić...";
                _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                System.Threading.Thread.Sleep(300);
                DialogResult = DialogResult.OK;
                Close();
            }

            // Override ProcessCmdKey - przechwytuje wszystkie klawisze
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (msg.Msg == 0x0100)  // WM_KEYDOWN
                {
                    Keys baseKey = keyData & Keys.KeyCode;

                    if (baseKey == Keys.Escape)
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                        return true;
                    }

                    Result = InputBinding.FromKey(baseKey);
                    _instructionLabel.Text = $"Bindowano: {baseKey}\n\nKlikaj by wrócić...";
                    _instructionLabel.ForeColor = Color.FromArgb(100, 200, 100);

                    System.Threading.Thread.Sleep(300);
                    DialogResult = DialogResult.OK;
                    Close();
                    return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
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

        private void OpenRevLimiterBindings()
        {
            Form overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
            };

            Form popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                Size = new Size(300, 320),
                BackColor = ApplePalette.Background
            };

            popup.Shown += (s, e) =>
            {
                popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 18);
            };

            var card = new RoundedPanel { Dock = DockStyle.Fill };
            popup.Controls.Add(card);

            var closeButton = new Button
            {
                Text = "×",
                Size = new Size(28, 28),
                Location = new Point(popup.Width - 38, 10),
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
            closeButton.Click += (s, e) => popup.Close();
            closeButton.Region = CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);

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
                        bindBtn.Text = b.ToString();
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

            popup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 30; i >= 1; i--)
                {
                    int alpha = (int)(22 * (1.0 - i / 30.0));
                    Rectangle shadowRect = new Rectangle(12 - i, 12 - i, popup.Width - 24 + i * 2, popup.Height - 24 + i * 2);
                    using (GraphicsPath p = RoundedPath(shadowRect, 20 + i))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                        e.Graphics.FillPath(b, p);
                }
            };

            card.Controls.Add(closeButton);

            overlay.Show();
            popup.Owner = overlay;

            try { popup.ShowDialog(overlay); }
            finally { overlay.Close(); overlay.Dispose(); }
        }

        private void OpenInSimPalette(Button targetBtn, Action<string> setColor)
        {
            Form overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
            };


            Form popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                Size = new Size(240, 270),
                BackColor = ApplePalette.Background
            };


            popup.Shown += (s, e) =>
            {
                popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 20);
            };


            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill
            };

            popup.Controls.Add(card);

            // Close button

            var closeButton = new Button
            {
                Text = "×",
                Size = new Size(28, 28),
                Location = new Point(popup.Width - 38, 10),

                FlatStyle = FlatStyle.Flat,

                BackColor = Color.Transparent,
                ForeColor = ApplePalette.Secondary,

                Font = new Font(
                    "Segoe UI Semibold",
                    12f),

                Cursor = Cursors.Hand,
                TabStop = false
            };


            closeButton.FlatAppearance.BorderSize = 0;

            closeButton.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(235, 235, 240);

            closeButton.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(220, 220, 225);


            closeButton.Click += (s, e) =>
            {
                popup.Close();
            };


            closeButton.Region = CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);


            card.Controls.Add(closeButton);
            // Title

            MakeLabel(
                card,
                "InSim Color Picker",
                20,
                15,
                180,
                28,
                ApplePalette.Title,
                new Font(
                    "Segoe UI Semibold",
                    11f));

            // Color buttons

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


                // okrągły kolor

                b.Region = CreateSmoothRoundedRegion(b.Width, b.Height, 20);



                b.Click += (s, e) =>
                {
                    SetButtonColor(targetBtn, item.color);
                    setColor(item.code);

                    popup.Close();
                };


                // delikatny hover

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

            // shadow popup

            popup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode =
                    SmoothingMode.AntiAlias;


                for (int i = 30; i >= 1; i--)
                {
                    int alpha =
                        (int)(22 *
                        (1.0 - i / 30.0));


                    Rectangle shadowRect =
                        new Rectangle(
                            12 - i,
                            12 - i,
                            popup.Width - 24 + i * 2,
                            popup.Height - 24 + i * 2);



                    using (GraphicsPath p =
                        RoundedPath(
                            shadowRect,
                            20 + i))

                    using (SolidBrush b =
                        new SolidBrush(
                            Color.FromArgb(
                                alpha,
                                0,
                                0,
                                0)))
                    {
                        e.Graphics.FillPath(
                            b,
                            p);
                    }
                }
            };

            overlay.Show();
            popup.Owner = overlay;


            try
            {
                popup.ShowDialog(overlay);
            }
            finally
            {
                overlay.Close();
                overlay.Dispose();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        private void ExecuteLightToggle()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IntPtr hwnd = FindLfsWindow();
                if (hwnd == IntPtr.Zero) return;

                const int VK_SHIFT = 0x10;
                const int VK_3 = 0x33;

                // scan code Shift = 0x2A, scan code '3' = 0x04 (US layout)
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT, (IntPtr)0x002A0001);
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_3, (IntPtr)0x00040001);

                System.Threading.Thread.Sleep(50);

                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_3, (IntPtr)0xC0040001);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_SHIFT, (IntPtr)0xC02A0001);
            });
        }
       
       
        private static IntPtr FindLfsWindow()
        {
            // LFS używa różnych tytułów zależnie od wersji i trybu
            string[] titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };
            foreach (var t in titles)
            {
                IntPtr h = FindWindow(null, t);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }


        // ─────────────────────────────────────────────────────
        //  InSim callbacks
        // ─────────────────────────────────────────────────────
        private void OnCarData(object sender, CarDataEventArgs e)
        {

            byte viewPlid = _insim.ViewPLID;
            // Ustaw PLID gracza (pierwszy samochód)
            if (_playerPLID == 0)
                _playerPLID = e.Car.PLID;

            if (viewPlid != 0)
                _lastKnownPlayerPLID = viewPlid;
            else if (_lastKnownPlayerPLID == 0)
                return;  // jeszcze nie wiadomo, które auto jest nasze
            if (!_showHudCheck.Checked) 
            
            {
                ShowInGameAward($"");

            }
            
            // Liczenie TYLKO dla gracza!
            if (e.Car.PLID != _lastKnownPlayerPLID)
                return;  // ← WYJDŹ jeśli to nie gracz

            _drift.Update(e.Car);  // ← Teraz bezpieczne!
            double speed = e.Car.SpeedKmh;

            // Konwersja Heading z LFS (0-65535) na stopnie (0-360)
            double headingDeg = (e.Car.Heading / 65535.0) * 360.0;
            _indicators.Update(headingDeg);

            this.BeginInvoke((Action)(() =>
            {
                _speedKmh = speed;
                _driftAngle = _drift.DriftAngleDeg;
                _isDrifting = _drift.IsDrifting;
                _isSpeeding = _drift.IsSpeeding;
                _overlay.SetDriftAngle(_driftAngle, _isDrifting, _drift.DriftSideRight);
                _overlay.SetActive(_isDrifting || _isSpeeding);
                _overlay.UpdateLapScore(_drift.LapScore);

                if (!_isDrifting && !_isSpeeding)
                    _overlay.UpdateAccentColor(InSimCodeToColor(InSimColor1));
                _speedLabel.Text = ((int)speed).ToString();
                _angleValueLabel.Text = ((int)_driftAngle).ToString() + "°";
                _angleValueLabel.ForeColor = _isDrifting
                    ? Color.FromArgb(255, 80, 80) : Color.FromArgb(60, 180, 255);

                _speedometer.Speed = speed;
                _speedometer.DriftAngle = _driftAngle;
                _speedometer.IsDrifting = _isDrifting;
                _speedometer.Invalidate();
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

                UpdateScoreLabels();
                if (_showHudCheck.Checked && _insim.IsConnected && _insim.IsRaceNow)
                { UpdateInGameHUD(); }
                else
                {
                    _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                }

                string angleColCode = _driftLabelKind switch
                {
                    DriftLabelKind.AngleHigh => InSimColor3,
                    DriftLabelKind.AngleExtreme => InSimColor4,
                    DriftLabelKind.AngleBackward => InSimColor3,
                    DriftLabelKind.AngleUltraExtreme => InSimColor5,
                    DriftLabelKind.AngleHighE => InSimColor3,
                    DriftLabelKind.AngleExtremeE => InSimColor4,
                    DriftLabelKind.AngleBackwardE => InSimColor3,
                    DriftLabelKind.AngleUltraExtremeE => InSimColor5,
                    DriftLabelKind.Fast2 => InSimColor3,
                    DriftLabelKind.Fast3 => InSimColor4,
                    DriftLabelKind.AngleGood => InSimColor2,
                    DriftLabelKind.AngleGoodE => InSimColor2,
                    DriftLabelKind.Fast1 => InSimColor2,
                    _ => InSimColor1,
                };

                _overlay.UpdateScore(_totalScore);
                _overlay.UpdateRun(_runPoints);
                _overlay.UpdateCombo(_combo);
                _overlay.UpdateLabel(label);
                _overlay.UpdateAccentColor(InSimCodeToColor(angleColCode));

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
        private void OnDriftEnded(long runPts)
        {
            this.BeginInvoke((Action)(() =>
            {
                _lastAward = _drift.LastAwardedText;
                UpdateScoreLabels();
                if (_showHudCheck.Checked && _insim.IsConnected)
                {
                    // Flash award text in game
                    if(_showHudCheck.Checked) ShowInGameAward(_lastAward);
                    _awardTick = 0;
                    _awardTimer.Start();
                    
                }
                else
                {

                    _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                }
            }));
        }

        private void OnStatus(object sender, StatusEventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _statusLabel.Text = e.Message;
                _statusLabel.ForeColor = e.IsError ? Color.FromArgb(220, 80, 80) : Color.FromArgb(100, 200, 120);
            }));
        }
        private IndicatorManager.IndicatorState _previousIndicatorState = IndicatorManager.IndicatorState.Off;
        private void OnIndicatorStateChanged(IndicatorManager.IndicatorState newState)
        {

            this.BeginInvoke((Action)(() =>
            {

                // Aktualizuj UI
                _indicatorDisplayLabel.Text = _indicators.GetIndicatorText();

                // Zmień kolor w zależności od stanu
                _indicatorDisplayLabel.ForeColor = newState switch
                {
                    IndicatorManager.IndicatorState.Left => Color.FromArgb(100, 200, 255),
                    IndicatorManager.IndicatorState.Right => Color.FromArgb(100, 200, 255),
                    IndicatorManager.IndicatorState.Hazard => Color.FromArgb(255, 180, 0),
                    _ => Color.FromArgb(100, 100, 100)
                };

                // Wyślij klawisz (7/8/9/0) do LFS w oddzielnym wątku
                System.Threading.ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(50);
                        _indicators.SendKeyToLFS();
                    }
                    catch { }


                });

                try
                {
                    if (_previousIndicatorState != newState)
                    {
                        // automatyczne wyłączenie po skręcie
                        if ((_previousIndicatorState == IndicatorState.Left ||
                             _previousIndicatorState == IndicatorState.Right) &&
                             newState == IndicatorState.Off &&
                             !indClickONOFF)
                        {
                            
                            _indicatorClick.Stop();
                            _indicatorCancel.Play();
                        }
                        else if (newState == IndicatorState.Off &&
                             !indClickONOFF)
                        {
                            _indicatorClick.Stop();
                            _indicatorCancel.Play();
                        }
                        else
                        {
                            
                            
                            _indicatorClick.PlayLooping();
                        }
                        

                            _previousIndicatorState = newState;
                    }
                }
                catch { }

                // Zaktualizuj label statusu
                _indicatorStatusLabel.Text = $"Current status:\n{newState}";
            }));
        }

        private void OnConnected(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(true);
                _resetBtn.Enabled = true;
                _revLimiter.Start();
                _connectionStateLabel.Text = "⬤  Connected with LFS";
                _connectionStateLabel.ForeColor = Color.FromArgb(60, 220, 100);
                if (_showHudCheck.Checked)
                    InitInGameHUD();


                if (_showOverlayCheck.Checked)
                {
                    IntPtr lfsHwnd = FindLfsWindow();
                    if (lfsHwnd != IntPtr.Zero)
                    {
                        _overlay.UpdateScore(_drift.TotalScore);
                        _overlay.UpdateAccentColor(InSimCodeToColor(InSimColor1));
                        _overlay.AttachTo(lfsHwnd);
                    }
                }

            }));
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                _tireTemperatureLimiter.Connect();
            });
        }

        private void OnDisconnected(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _resetBtn.Enabled = false;
                _revLimiter.Stop();
                _connectionStateLabel.Text = "⬤  Rozłączono";
                _connectionStateLabel.ForeColor = Color.FromArgb(130, 130, 165);
                _speedKmh = 0; _driftAngle = 0;
                _speedLabel.Text = "0"; _angleValueLabel.Text = "0°";
                _speedometer.Speed = 0; _speedometer.Invalidate();
                _overlay.Detach();
            }));

            _tireTemperatureLimiter.Disconnect();
        }

        // ─────────────────────────────────────────────────────
        //  Score labels
        // ─────────────────────────────────────────────────────
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
            _runValueLabel.Text = _drift.IsDrifting
                ? _drift.CurrentRunPoints.ToString("N0") : "—";
            _runValueLabel.Text = _drift.IsSpeeding
                ? _drift.CurrentRunPoints.ToString("N0") : "—";
            _comboValueLabel.Text = $"x{_drift.ComboMultiplier}";
            _comboValueLabel.ForeColor = Color.FromArgb(255, 60, 60);
            
        }

        // ─────────────────────────────────────────────────────
        //  In-game HUD via IS_BTN
        //
        //  Layout (all in 0-200 coord space, recommended area L 0-110, T 30-170):
        //
        //   [BTN_SCORE ]  total score       — top centre, wide
        //   [BTN_RUN   ]  current run pts   — below score
        //   [BTN_COMBO ]  combo x N         — right of run
        //   [BTN_LABEL ]  "GREAT DRIFT" etc — below run, fades
        //   [BTN_AWARD ]  "EPIC! 5000 pts"  — flashes on drift end
        // ─────────────────────────────────────────────────────
        static byte xPos = 108;
        static byte xPosSec = 80;
        static byte xPosT = 120;
        private void InitInGameHUD()
        {
            // Static header button — "DRIFT BUDDY" title (always visible)
            if (_showHudCheck.Checked)
            {
                _insim.ShowButton(BTN_SCORE, InSimColor1 + _drift.TotalScore.ToString("N0") + InSimColor1,
                l: 70, t: 3, w: 60, h: 15, bStyle: 5);
            }
            ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());
        }

        private void UpdateInGameHUD()
        {
            if (!_insim.IsConnected) return;
            

            // ── Total score (top, persistent) ────────────────
            ShowInGameRPMLimitter((_revLimiterNumeric.Value).ToString());

            // ── Current run ───────────────────────────────────
            if (_drift.IsDrifting || _drift.IsSpeeding)
            {

                string angleCol = _driftLabelKind switch
                {
                    DriftLabelKind.AngleHigh => InSimColor3,
                    DriftLabelKind.AngleExtreme => InSimColor4,
                    DriftLabelKind.AngleBackward => InSimColor3,
                    DriftLabelKind.AngleUltraExtreme => InSimColor5,
                    DriftLabelKind.AngleHighE => InSimColor3,
                    DriftLabelKind.AngleExtremeE => InSimColor4,
                    DriftLabelKind.AngleBackwardE => InSimColor3,
                    DriftLabelKind.AngleUltraExtremeE => InSimColor5,
                    DriftLabelKind.Fast2 => InSimColor3,
                    DriftLabelKind.Fast3 => InSimColor4,
                    DriftLabelKind.AngleGood => InSimColor2,
                    DriftLabelKind.AngleGoodE => InSimColor2,
                    DriftLabelKind.Fast1 => InSimColor2,
                    _ => InSimColor1,
                };

                colorCurrentMain = angleCol;

                string scoreText = _drift.IsSpeeding
                 ? $"{angleCol}{_drift.TotalScore:N0}"
                : $"{angleCol}{_drift.TotalScore:N0}{angleCol}";

                scoreText = _drift.IsDrifting
                 ? $"{angleCol}{_drift.TotalScore:N0}"
                : $"{angleCol}{_drift.TotalScore:N0}{angleCol}";

                if (_showHudCheck.Checked)
                {
                    _insim.ShowButton(BTN_SCORE, scoreText,
                    l: 70, t: 3, w: 60, h: 15, bStyle: 5);
                }
                if (_showHudCheck.Checked) ShowInGameAward(_drift.LastAwardedText);

                // ── Drift label ───────────────────────────────

                _insim.ShowButton(BTN_LABEL, angleCol + _driftLabel,
                            l: 90, t: 15, w: 20, h: 7, bStyle: 5);



                string spaceIND = _totalScore >= 10000 ? $" " : $"";
                spaceIND = _totalScore >= 100000 ? $"  " : $" ";
                spaceIND = _totalScore >= 1000000 ? $"   " : $"  ";
                string IND = $"{angleCol}{spaceIND}{_indicatorLabelR}";

                _insim.ShowButton(BTN_RIGHTIND, IND,
                l: 100, t: 3, w: 30, h: 15, bStyle: 5);

                IND = $"{angleCol}{_indicatorLabelL}{spaceIND}";

                _insim.ShowButton(BTN_LEFTIND, IND,
                l: 70, t: 3, w: 30, h: 15, bStyle: 5);

                // ── Combo ─────────────────────────────────────
                string comboCol = _drift.ComboMultiplier >= 5 ? InSimColor4
                                : _drift.ComboMultiplier >= 3 ? InSimColor3
                                : _drift.ComboMultiplier >= 2 ? InSimColor2 : InSimColor1;

                if (_drift.IsDrifting)
                {
                    _insim.ShowButton(BTN_COMBO, $"{angleCol} {_angleValueLabel.Text} {comboCol}x{_drift.ComboMultiplier}",
                    l: xPosSec, t: 15, w: 10, h: 7, bStyle: 5);
                }
                else
                {
                    _insim.ShowButton(BTN_COMBO, $"  {comboCol}x{_drift.ComboMultiplier}",
                        l: xPosSec, t: 15, w: 10, h: 7, bStyle: 5);
                }


                string runCol = _drift.CurrentRunPoints >= 5000 ? InSimColor4
                                : _drift.CurrentRunPoints >= 1500 ? InSimColor3
                                : _drift.CurrentRunPoints >= 500 ? InSimColor2 : InSimColor1;

                string runText = $"{runCol}{_drift.CurrentRunPoints:N0}";
                _insim.ShowButton(BTN_RUN, runText,
                    l: xPos, t: 15, w: 10, h: 7, bStyle: 5);

            }
            else
            {
                // Clear run/combo/label when not drifting
                _insim.ShowButton(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_COMBO, "", l: xPosSec, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                if (_showHudCheck.Checked) { _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2); }

            }

            if (!_showHudCheck.Checked)
            {
                _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_AWARD, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_COMBO, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_RIGHTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_LEFTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
            }


        }

        private void BuildTitleBar()
        {
            _titleBar = new Panel
            {
                Location = new Point(1, 1),
                Size = new Size(ClientSize.Width-2, TitleBarHeight),
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
                Text = "LFS Drift Tools",
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
            //_titleBar.Region = CreateSmoothRoundedRegion(_titleBar.Width + 16, _titleBar.Height, 20);
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
        private void ShowInGameAward(string text)
        {

            if (!_showHudCheck.Checked)
            {
                _insim.ShowButton(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_AWARD, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_COMBO, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                _insim.ShowButton(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
                return;
            }
            
                if (_drift.LastAwardedText != "") 
            {
                _insim.ShowButton(BTN_AWARD, colorCurrentMain + _drift.LastAwardedText,
                l: 60, t: 22, w: 80, h: 6, bStyle: 5);

            } else 
            {
                _insim.ShowButton(BTN_AWARD, "^5" + text,
                l: 60, t: 22, w: 80, h: 6, bStyle: 5);

            }
            //UpdateInGameHUD();

        }


        private void ShowInGameRPMLimitter(string text)
        {

            if (_showRPMHudCheck.Checked && _revLimiter.Enabled)
            {

                if (!_insim.IsConnected) return;
                _insim.ShowButton(BTN_RPMLIMIT, "^6" + "RPM LIMIT: " + text,
                    l: 0, t: 196, w: 25, h: 5, bStyle: 65);
                
            }
            else
            {
                if (!_insim.IsConnected) return;
                _insim.ShowButton(BTN_RPMLIMITSTATUS, "",
                    l: 0, t: 180, w: 35, h: 4, bStyle: 65);
                if (!_insim.IsConnected) return;
                _insim.ShowButton(BTN_RPMLIMIT, "",
                    l: 0, t: 185, w: 35, h: 4, bStyle: 65);
                _insim.ShowButton(BTN_RPMLIMITINFO, "",
                   l: 0, t: 190, w: 35, h: 3, bStyle: 65);
                //_insim.DeleteAllButtons();
            }
        }

        private void AwardTimer_Tick(object? sender, EventArgs e)
        {
            _awardTick++;
            if (_awardTick >= 30)
            {
                _awardTimer.Stop();
                if (_insim.IsConnected)
                    _insim.ShowButton(BTN_AWARD, "", l: xPosT, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_COMBO, "", l: xPosSec, t: 0, w: 1, h: 1, bStyle: 2);
                _insim.ShowButton(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);

                string scoreText = _drift.IsSpeeding
                 ? $"{InSimColor2}{_drift.TotalScore:N0}"
                : $"{InSimColor1}{_drift.TotalScore:N0}{InSimColor1}";

                scoreText = _drift.IsDrifting
                 ? $"{InSimColor2}{_drift.TotalScore:N0}"
                : $"{InSimColor1}{_drift.TotalScore:N0}{InSimColor1}";
                _insim.ShowButton(BTN_SCORE, scoreText,
                    l: 70, t: 3, w: 60, h: 15, bStyle: 5);

                string IND = _drift.IsDrifting
                    ? $"{InSimColor2}{_indicatorLabelR}"
                    : $"";


                _insim.ShowButton(BTN_RIGHTIND, IND,
                l: 100, t: 3, w: 30, h: 15, bStyle: 5);

                IND = _drift.IsDrifting
                    ? $"{InSimColor2}{_indicatorLabelL}"
                    : $"";

                _insim.ShowButton(BTN_LEFTIND, IND,
                l: 70, t: 3, w: 30, h: 15, bStyle: 5);

            }
        }

        // ─────────────────────────────────────────────────────
        //  Helper builders
        // ─────────────────────────────────────────────────────

        private NumericUpDown MakeNumericUpDown(
        Control parent,
        int x,
        int y,
        int w,
        int h,
        decimal min,
        decimal max,
        decimal value,
        decimal increment = 1,
        Action<decimal>? onValueChanged = null)
        {
            var wrapper = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ApplePalette.Card,
                Tag = "theme:card" // pozwala rozpoznać wrapper przy zmianie motywu
            };

            var nud = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Increment = increment,

                BorderStyle = BorderStyle.None,

                BackColor = ApplePalette.Card,
                ForeColor = ApplePalette.Text,

                Font = new Font("Segoe UI", 10f),

                TextAlign = HorizontalAlignment.Center
            };

            nud.SetBounds(8, 6, w - 16, h - 12);

            wrapper.Controls.Add(nud);
            parent.Controls.Add(wrapper);


            // 🍏 Apple rounded border
            wrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(0, 0, wrapper.Width - 1, wrapper.Height - 1);

                using (GraphicsPath path = RoundedPath(rect, 10))
                using (Pen border = new Pen(ApplePalette.Border))
                {
                    e.Graphics.DrawPath(border, path);
                }
            };

            // 🔵 focus effect
            nud.GotFocus += (s, e) =>
            {
                wrapper.BackColor = Lighten(ApplePalette.Card, 0.02);
                wrapper.Invalidate();
            };

            nud.LostFocus += (s, e) =>
            {
                wrapper.BackColor = ApplePalette.Card;
                wrapper.Invalidate();
            };

            // 🔁 value changed hook
            nud.ValueChanged += (s, e) =>
            {

                onValueChanged?.Invoke(nud.Value);
            };

            return nud;
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
                    ? new Font("Segoe UI", 7.5f, FontStyle.Regular)
                    : new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular)),

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
        int h)
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

                Padding = new Padding(10)
            };

            // 🔥 Apple-style wrapper (rounded container effect)
            var wrapper = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ApplePalette.Card,
                Tag = "theme:card" // pozwala rozpoznać wrapper przy zmianie motywu
            };

            parent.Controls.Add(wrapper);
            wrapper.Controls.Add(tb);

            tb.Location = new Point(10, 7);
            tb.Width = w - 20;
            tb.Height = h - 14;

            // 🎯 focus effect (iOS-like blue ring)
            wrapper.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(0, 0, wrapper.Width - 1, wrapper.Height - 1);

                using (GraphicsPath path = RoundedPath(rect, 10))
                using (Pen border = new Pen(ApplePalette.Border))
                {
                    e.Graphics.DrawPath(border, path);
                }
            };

            tb.GotFocus += (s, e) =>
            {
                wrapper.Invalidate();
                wrapper.BackColor = Lighten(ApplePalette.Card, 0.02);
            };

            tb.LostFocus += (s, e) =>
            {
                wrapper.Invalidate();
                wrapper.BackColor = ApplePalette.Card;
            };

            parent.Controls.Add(wrapper);

            return tb;
        }
        private Color Lighten(Color c, double factor)
        {
            return Color.FromArgb(
                c.A,
                Math.Min(255, (int)(c.R + (255 - c.R) * factor)),
                Math.Min(255, (int)(c.G + (255 - c.G) * factor)),
                Math.Min(255, (int)(c.B + (255 - c.B) * factor)));
        }

        private Color Darken(Color c, double factor)
        {
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - factor)),
                (int)(c.G * (1 - factor)),
                (int)(c.B * (1 - factor)));
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
                    ? Color.FromArgb(232, 17, 35) // czerwień jak w Windows 11 na hover
                    : (ApplePalette.IsDark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0));

                if (_isHover)
                {
                    Color fill = _isDown ? Darken(hoverFill, 0.15) : hoverFill;
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

            private static Color Darken(Color c, double amount)
            {
                return Color.FromArgb(c.A,
                    (int)(c.R * (1 - amount)),
                    (int)(c.G * (1 - amount)),
                    (int)(c.B * (1 - amount)));
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

                // 🔑 aktualny bazowy kolor trzymany w Tag, żeby dało się go
                // podmienić z zewnątrz (np. po wyborze koloru z palety) i żeby
                // Paint zawsze rysował AKTUALNY kolor, nie ten sprzed utworzenia
                Tag = baseColor
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;

            // ❌ bez Region — to ono robiło twarde, ścięte krawędzie

            bool isHover = false;
            bool isDown = false;
            const int margin = 3;   // miejsce wewnątrz kontrolki na miękką poświatę
            const int radius = 10;

            btn.Paint += (s, e) =>
            {
                Color currentColor = (Color)btn.Tag;

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle full = new Rectangle(0, 0, btn.Width, btn.Height);
                Rectangle rect = Rectangle.Inflate(full, -margin, -margin);

                // "wymazanie" narożników kolorem tła rodzica (imitacja przezroczystości)
                using (SolidBrush parentBrush = new SolidBrush(parent.BackColor))
                    g.FillRectangle(parentBrush, full);

                // miękka poświata przy najechaniu (kilka półprzezroczystych warstw)
                if (isHover)
                {
                    for (int i = 5; i >= 1; i--)
                    {
                        using (GraphicsPath glowPath = RoundedPath(Rectangle.Inflate(rect, i, i), radius + i))
                        using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(14, currentColor)))
                        {
                            g.FillPath(glowBrush, glowPath);
                        }
                    }
                }

                Color fill = isDown ? Darken(currentColor, 0.10)
                            : isHover ? Lighten(currentColor, 0.10)
                            : currentColor;

                using (GraphicsPath path = RoundedPath(rect, radius))
                {
                    using (var gradient = new LinearGradientBrush(
                               rect, Lighten(fill, 0.06), Darken(fill, 0.04), 90f))
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

        // Pomocnicza metoda: podmienia bazowy kolor przycisku i wymusza przerysowanie
        private void SetButtonColor(Button btn, Color color)
        {
            btn.Tag = color;
            btn.Invalidate();
        }

        private GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();
            return path;
        }

        private void UpdateTireLimiterUI(bool isPatched)
        {
            if (_tireStatusLabel != null)
            {
                _tireStatusLabel.Text = isPatched
                    ? "✅ ACTIVE - Patched"
                    : "❌ Not Patched";

                _tireStatusLabel.ForeColor = isPatched
                    ? Color.FromArgb(80, 255, 100)
                    : Color.FromArgb(255, 100, 100);
            }

            if (_tirePatchBtn != null)
            {
                if (isPatched)
                {
                    _tirePatchBtn.Text = "REMOVE PATCH";
                    _tirePatchBtn.BackColor = Color.FromArgb(60, 150, 60);
                }
                else
                {
                    _tirePatchBtn.Text = "APPLY PATCH";
                    _tirePatchBtn.BackColor = Color.FromArgb(180, 60, 60);
                }
            }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            Localization.LanguageChanged -= ApplyLanguage;
            if (_insim.IsConnected) { _insim.DeleteAllButtons(); System.Threading.Thread.Sleep(120); }
            try
            {
                _tireTemperatureLimiter.Disconnect();
            }
            catch { /* ignore */ }
            _insim.Dispose();
            _revLimiter.Dispose();
            _globalHotkey.Dispose();
            _overlay?.Dispose();
            _shadow?.Close();
            base.OnFormClosing(e);
        }

        private void InitializeComponent() { }
    }


    public class MacToggleSwitch : Control
    {
        private bool _checked = false;
        private float _knobProgress = 0f;      // 0 = wyłączony, 1 = włączony
        private float _animFrom, _animTo;
        private DateTime _animStart;
        private readonly System.Windows.Forms.Timer _animTimer;
        private const int AnimDurationMs = 160;

        private bool _isPressed = false;

        public event EventHandler CheckedChanged;

        public Color OnColor { get; set; } = Color.FromArgb(52, 199, 89);   // Apple green
        public Color OffColor { get; set; } = Color.FromArgb(210, 210, 215); // jasny szary
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

            // ease-out
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

            // tło rodzica pod spodem, żeby nie było prostokątnych naroży
            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            Rectangle trackRect = new Rectangle(0, 0, Width-1, Height-1);
            int radius = (Height - 1) / 2;

            Color trackColor = Blend(OffColor, OnColor, _knobProgress);

            using (GraphicsPath trackPath = RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // delikatna ramka
            using (GraphicsPath trackPath = RoundedPath(trackRect, radius))
            using (Pen border = new Pen(Color.FromArgb(25, 0, 0, 0), 1f))
            {
                g.DrawPath(border, trackPath);
            }

            // pozycja kropki
            int knobDiameter = Height - 5;
            int knobTravel = Width - knobDiameter - 5;
            int knobX = 2 + (int)(knobTravel * _knobProgress);
            int knobY = 2;

            Rectangle knobRect = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);

            // cień pod kropką (delikatna głębia)
            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            Color knob = _isPressed ? Darken(KnobColor, 0.05) : KnobColor;

            using (GraphicsPath knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knobRect);
                using (SolidBrush knobBrush = new SolidBrush(knob))
                    g.FillPath(knobBrush, knobPath);

                using (Pen knobBorder = new Pen(Color.FromArgb(20, 0, 0, 0), 1f))
                    g.DrawPath(knobBorder, knobPath);
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

        private static Color Darken(Color c, double amount)
        {
            int r = (int)(c.R * (1 - amount));
            int g = (int)(c.G * (1 - amount));
            int b = (int)(c.B * (1 - amount));
            return Color.FromArgb(c.A, r, g, b);
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();
            return path;
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
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255); // Apple blue

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

            // tło rodzica pod spodem — imitacja przezroczystości bez artefaktów
            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? BackColor))
                g.FillRectangle(parentBrush, ClientRectangle);

            int radius = Height / 2;
            Rectangle trackRect = new Rectangle(0, 0, Width, Height);

            using (GraphicsPath trackPath = RoundedPath(trackRect, radius))
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
                // minimalna szerokość, żeby zaokrąglone końce nie "znikały" przy małych wartościach
                fillWidth = Math.Max(fillWidth, Height);

                Rectangle fillRect = new Rectangle(0, 0, fillWidth, Height);

                using (GraphicsPath fillPath = RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           fillRect,
                           Lighten(FillColor, 0.10f),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }
        }

        private static Color Lighten(Color c, float amount)
        {
            int r = c.R + (int)((255 - c.R) * amount);
            int g = c.G + (int)((255 - c.G) * amount);
            int b = c.B + (int)((255 - c.B) * amount);
            return Color.FromArgb(c.A, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int d = radius * 2;
            d = Math.Min(d, Math.Min(rect.Width, rect.Height));

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    public class MacCheckBox : CheckBox
    {
        public Color CheckedColor { get; set; } = Color.FromArgb(0, 122, 255); // Apple blue

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

            // tło rodzica pod spodem — imitacja przezroczystości bez artefaktów
            using (var parentBrush = new SolidBrush(Parent?.BackColor ?? ApplePalette.Card))
                g.FillRectangle(parentBrush, ClientRectangle);

            const int boxSize = 18;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(0, boxY, boxSize, boxSize);

            using (GraphicsPath boxPath = RoundedPath(boxRect, 5))
            {
                if (Checked)
                {
                    using (SolidBrush fill = new SolidBrush(CheckedColor))
                        g.FillPath(fill, boxPath);
                }
                else
                {
                    // czyta AKTUALNY kolor z palety — reaguje na zmianę motywu
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

            // czyta AKTUALNY kolor tekstu z palety — reaguje na zmianę motywu
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                ApplePalette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
    // =========================================================
    //  Analog Speedometer + Drift Angle indicator
    // =========================================================
    public class SpeedometerControl : Control
    {
        public double Speed { get; set; } = 0;
        public double DriftAngle { get; set; } = 0;
        public bool IsDrifting { get; set; } = false;
        public bool IsSpeeding { get; set; } = false;

        private const double MaxSpeed = 300.0;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int cx = Width / 2;
            int cy = Height - 16;
            int radius = Math.Min(Width / 2, Height) - 18;

            // Background arc fill
            using var bgBrush = new SolidBrush(Color.FromArgb(22, 22, 34));
            g.FillPie(bgBrush, cx - radius, cy - radius, radius * 2, radius * 2, 180, 180);

            // Outer ring
            using var outerPen = new Pen(Color.FromArgb(45, 45, 65), 2);
            g.DrawArc(outerPen, cx - radius, cy - radius, radius * 2, radius * 2, 180, 180);

            // Speed arc (red if drifting, else normal red)
            double fraction = Math.Min(Speed / MaxSpeed, 1.0);
            float sweep = (float)(fraction * 180.0);
            if (sweep > 0.5f)
            {
                Color arcColor = IsDrifting ? Color.FromArgb(255, 60, 60) : Color.FromArgb(180, 30, 30);
                using var arcPen = new Pen(arcColor, 7);
                g.DrawArc(arcPen,
                    cx - radius + 10, cy - radius + 10,
                    (radius - 10) * 2, (radius - 10) * 2,
                    180, sweep);
            }

            // Drift angle arc (blue, inner ring)
            if (DriftAngle > 1.0)
            {
                double maxAngle = 75.0;
                float driftSweep = (float)(Math.Min(DriftAngle / maxAngle, 1.0) * 180.0);
                Color dc = IsDrifting ? Color.FromArgb(60, 200, 255) : Color.FromArgb(40, 80, 120);
                using var driftPen = new Pen(dc, 4);
                g.DrawArc(driftPen,
                    cx - radius + 22, cy - radius + 22,
                    (radius - 22) * 2, (radius - 22) * 2,
                    180, driftSweep);
            }

            // Tick marks
            for (int i = 0; i <= 12; i++)
            {
                double angle = Math.PI + (i / 12.0) * Math.PI;
                bool major = (i % 2 == 0);
                int tickOuter = radius - 3;
                int tickInner = major ? radius - 18 : radius - 11;

                int x1 = (int)(cx + Math.Cos(angle) * tickOuter);
                int y1 = (int)(cy + Math.Sin(angle) * tickOuter);
                int x2 = (int)(cx + Math.Cos(angle) * tickInner);
                int y2 = (int)(cy + Math.Sin(angle) * tickInner);

                using var tickPen = new Pen(
                    major ? Color.FromArgb(160, 160, 190) : Color.FromArgb(60, 60, 80),
                    major ? 2f : 1f);
                g.DrawLine(tickPen, x1, y1, x2, y2);

                if (major)
                {
                    int spd = (int)(i / 12.0 * MaxSpeed);
                    int lx = (int)(cx + Math.Cos(angle) * (tickInner - 14)) - 14;
                    int ly = (int)(cy + Math.Sin(angle) * (tickInner - 14)) - 7;
                    using var tf = new Font("Segoe UI", 7f);
                    g.DrawString(spd.ToString(), tf, Brushes.Gray, lx, ly);
                }
            }

            // Needle
            double needleAngle = Math.PI + fraction * Math.PI;
            int needleLen = radius - 24;
            int nx = (int)(cx + Math.Cos(needleAngle) * needleLen);
            int ny = (int)(cy + Math.Sin(needleAngle) * needleLen);
            using var needlePen = new Pen(IsDrifting ? Color.FromArgb(255, 80, 80) : Color.FromArgb(220, 40, 40), 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.ArrowAnchor
            };
            g.DrawLine(needlePen, cx, cy, nx, ny);

            // Center hub
            using var hubBrush = new SolidBrush(Color.FromArgb(220, 40, 40));
            g.FillEllipse(hubBrush, cx - 7, cy - 7, 14, 14);
            using var hubRing = new Pen(Color.FromArgb(50, 50, 70), 2);
            g.DrawEllipse(hubRing, cx - 7, cy - 7, 14, 14);

            // Drift angle label inside arc
            if (DriftAngle > 5)
            {
                using var af = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                string atext = $"{(int)DriftAngle}°";
                Color ac = IsDrifting ? Color.FromArgb(80, 210, 255) : Color.FromArgb(80, 120, 180);
                var sz = g.MeasureString(atext, af);
                g.DrawString(atext, af, new SolidBrush(ac), cx - sz.Width / 2, cy - radius / 2 - sz.Height / 2);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(13, 13, 20));
        }
    }

    public static class ApplePalette
    {
        // Akcenty zostają identyczne w obu motywach — dobrze widoczne na jasnym i ciemnym tle
        public static readonly Color Blue =
            Color.FromArgb(0, 122, 255);

        public static readonly Color Green =
            Color.FromArgb(52, 199, 89);

        public static readonly Color Orange =
            Color.FromArgb(255, 159, 10);

        public static readonly Color Red =
            Color.FromArgb(255, 69, 58);

        // ── Kolory strukturalne — zmieniane przy przełączaniu motywu ──
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

            using (GraphicsPath shadowPath = GetPath(shadow, Radius))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
            {
                e.Graphics.FillPath(sb, shadowPath);
            }

            Rectangle rect = new Rectangle(
                0,
                0,
                Width - 1,
                Height - 1);

            using (GraphicsPath path = GetPath(rect, Radius))
            {
                using (SolidBrush b = new SolidBrush(BackColor))
                    e.Graphics.FillPath(b, path);

                using (Pen p = new Pen(ApplePalette.Border))
                    e.Graphics.DrawPath(p, path);
            }
        }

        private GraphicsPath GetPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);

            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);

            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);

            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();

            return path;
        }
    }

    public class AppSettings
    {
        public int CalibratedMAXRPM { get; set; } = 7600;

        public int SavedMSCUT { get; set; } = 40;

        public string Language { get; set; } = "English";

        public bool DarkTheme { get; set; } = false;

        public string InSimColor1 { get; set; } = "^7";
        public string InSimColor2 { get; set; } = "^6";
        public string InSimColor3 { get; set; } = "^3";
        public string InSimColor4 { get; set; } = "^5";
        public string InSimColor5 { get; set; } = "^5";

        // after
        public InputBinding RevToggleBinding { get; set; } = InputBinding.None;
        public InputBinding RevCalibrateBinding { get; set; } = InputBinding.None;
        public InputBinding RevDecreaseBinding { get; set; } = InputBinding.None;
        public InputBinding RevIncreaseBinding { get; set; } = InputBinding.None;

        public InputBinding IndicatorLeftBinding { get; set; } = InputBinding.FromKey(Keys.D7);
        public InputBinding IndicatorRightBinding { get; set; } = InputBinding.FromKey(Keys.D8);
        public InputBinding IndicatorHazardBinding { get; set; } = InputBinding.FromKey(Keys.D9);
        public InputBinding LightToggleBinding { get; set; } = InputBinding.None;

        public string SteeringWheelDeviceGuid { get; set; } = "";
        public string SteeringWheelAxis { get; set; } = "X";



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
        // NOTE: no Owner assignment — that's what was forcing us above the main form.
    }

    // Prevents Show() from stealing focus/activation, which is what triggered
    // the owner-above-owned reordering in the first place.
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
            Show(); // safe now: ShowWithoutActivation = true means owner keeps focus

        if (sizeChanged)
        {
            Bounds = newBounds;
            Render(newBounds);
            _lastRenderedSize = newBounds.Size;
        }
        else
        {
            // Move without touching size/z-order via UpdateLayeredWindow directly
            MoveOnly(newBounds.Location);
        }

        // Always re-pin directly behind the main window, regardless of any
        // z-order shuffling caused by focus changes elsewhere.
        SetWindowPos(Handle, _owner.Handle, 0, 0, 0, 0,
            0x0001 /*SWP_NOSIZE*/ | 0x0002 /*SWP_NOMOVE*/ | SWP_NOACTIVATE);
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
                using var path = RoundedPath(layerRect, CornerRadius + i);
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

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}