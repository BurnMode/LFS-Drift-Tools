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
using System.Reflection;

namespace LFSDriftBuddy
{
    public partial class MainForm : Form
    {


        private readonly string SettingsFile =
        Path.Combine(System.Windows.Forms.Application.StartupPath, "settings.json");

        private readonly SoundPlayer _indicatorClickOn =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click_on.wav"));

        private readonly SoundPlayer _indicatorClickOff =
        new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_click_off.wav"));

        private readonly SoundPlayer _indicatorCancel =
            new SoundPlayer(Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds", "indicator_cancel.wav"));

        // Ustawiane w konstruktorze (przed BuildUI, gdy _statusLabel jeszcze nie istnieje),
        // wyświetlane w statusie zaraz po zbudowaniu UI — patrz koniec konstruktora.
        private string _missingIndicatorSoundsWarning = null;

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
        private bool _isBurnout = false;
        private string _driftLabel = "";
        private string _indicatorLabelR = "";
        private string _indicatorLabelL = "";
        private string _lastAward = "";

        public int CalibratedMAXRPM = 7600;
        public int SavedMSCUT = 40;
        public bool calibrationON = false;

        // ── Ustawienia rev limitera per pojazd ──────────────────────────────────
        // Klucz = krótki kod auta z OutGauge (np. "XFG"). Wczytywane/zapisywane razem
        // z resztą AppSettings — patrz LoadSettings/SaveSettings, LoadVehicleRevSettings,
        // SaveVehicleRevSettings.
        private Dictionary<string, VehicleRevSettings> _vehicleRevSettings = new();
        private string _currentCarName = "";

        // Okno-podpowiedź kalibracji rev limitera (patrz ShowRevLimiterCalibrationPromptIfNeeded/
        // RevLimiterCalibrationPromptForm) — pokazywane RAZ na auto bez zapisanego presetu w
        // ramach tego uruchomienia aplikacji, żeby nie nagabywać przy każdym IS_CRS/wyjeździe
        // z pit lane tym samym, niekalibrowanym autem.
        private RevLimiterCalibrationPromptForm? _revLimiterCalibrationPrompt;
        private readonly HashSet<string> _revLimiterPromptShownForCars = new();

        // Świeżość danych OutGauge (RPM/gaz/bieg) — osobna instancja od tej w
        // OverlayForm (patrz DataFreshnessGate), bo służy do innego celu: blokowania
        // wykrywania drift/speeding/burnoutu w DriftEngine (patrz OnCarData), a nie
        // widoczności HUD-u. Pingowana z tego samego miejsca co overlay (OnRevData).
        private readonly DataFreshnessGate _outGaugeFreshness = new(TimeSpan.FromMilliseconds(1500));

        // Polls _outGaugeFreshness on its own clock instead of relying on OnCarData (MCI can
        // keep flowing even when OutGauge alone goes quiet, but if BOTH stop — e.g. menu without
        // MCI — nothing would ever notice the fresh->stale edge otherwise). On that edge: cancels
        // the drift engine run, closes the lap box, stops lap counting, resets the HUD to its
        // idle color — same reaction the game already gets on leaving to the menu.
        private readonly System.Windows.Forms.Timer _outGaugeFreshnessPoll = new() { Interval = 250 };
        private bool _wasOutGaugeFresh = true;

        private void OnOutGaugeFreshnessPollTick(object sender, EventArgs e)
        {
            bool fresh = _outGaugeFreshness.IsFresh;
            if (fresh == _wasOutGaugeFresh) return;
            _wasOutGaugeFresh = fresh;
            if (fresh) return;   // odzyskanie danych — nic wymuszać, naturalnie wróci na kolejnym okrążeniu/ticku

            _drift.SetOutGaugeDataFresh(false);
            _drift.DeactivateLapCounting();
            _overlay.SetLapBoxVisible(false);
            _overlay.UpdateAccentColor(OverlayColor1);
        }


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

        // In-game IS_BTN HUD (score/run/combo/labels/rpm readout) — moved to InGameHudManager.
        private InGameHudManager _hud;

        // ── UI Controls ───────────────────────────────────────
        private SpeedometerControl _speedometer;
        private Label _speedLabel, _speedUnitLabel;
        private Label _angleLabel, _angleValueLabel;
        private Label _comboLabel, _comboValueLabel;
        private Label _scoreLabel, _scoreValueLabel;
        private Label _runLabel, _runValueLabel;
        private Label _bestRunValueLabel;
        private Label _bestDriftValueLabel;
        private Label _bestDeepDriftValueLabel;
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

        // ── Speedometer + Tachometer HUD (Forza-style, overlay) ────────────────
        private MacToggleSwitch _speedoTachoEnabledCheck;
        private NumericUpDown _speedoTachoOffsetXNumeric;
        private NumericUpDown _speedoTachoOffsetYNumeric;
        private NumericUpDown _speedoTachoScaleNumeric;
        private MacToggleSwitch _speedUnitToggle;
        private Label _speedUnitToggleLabel;
        private bool _useMph = false;

        private Color _speedoTachoRedlineColor = Color.Red;
        private Color _speedoTachoTextColor = Color.White;
        private Color _speedoTachoIndicatorColor = Color.FromArgb(255, 225, 225, 230);
        private Color _speedoTachoTickColor = Color.FromArgb(255, 215, 215, 218);
        private Color _speedoTachoBackgroundColor = Color.FromArgb(50, 15, 15, 20);


        // ── UI Colors ─────────────────────────────────────────

        private string InSimColor1 = "^7";
        private string InSimColor2 = "^6";
        private string InSimColor3 = "^3";
        private string InSimColor4 = "^5";
        private string InSimColor5 = "^1";

        private Color OverlayColor1 = Color.White;
        private Color OverlayColor2 = Color.Cyan;
        private Color OverlayColor3 = Color.Yellow;
        private Color OverlayColor4 = Color.Magenta;
        private Color OverlayColor5 = Color.Red;

        private Button _colorBtn1;
        private Button _colorBtn2;
        private Button _colorBtn3;
        private Button _colorBtn4;
        private Button _colorBtn5;
        private Button _calibrateBtn1;
        private Button revBindingsBtn;
        private Button _langBtn;
        private Label _indicatorStatusLabel;
        private MacCheckBox _indicatorSoundsCheck;
        private MacCheckBox _indicatorAutoCancelCheck;
        private MacSlider _indicatorVolumeSlider;
        private Label _indicatorVolumeValueLabel;
        private int _indicatorSoundsVolume = 100;

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
            _insim.ConnectFailed += OnConnectFailed;
            _insim.RaceStateChanged += (s, inRace) => BeginInvoke((Action)(() =>
            {
                if (inRace)
                {
                    _overlay.SetMenuMode(false);   // wraca do gry/powtórki — odblokuj ramkę okrążenia
                    _hud.InitInGameHUD();      // pojawiają się przyciski
                }
                else
                {
                    _insim.DeleteAllButtons(); // znikają w menu/powtórce
                    _hud.InvalidateCache();
                    _drift.DeactivateLapCounting();     // ← NOWE: koniec okrążenia przy wyjściu do menu
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetMenuMode(true);   // natychmiast (bez animacji) chowa ramkę i blokuje jej powrót
                    _overlay.SetLapBoxVisible(false);   // ← NOWE: chowa ramkę HUD okrążenia
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
                //_overlay.UpdateLastLapScore(_drift.LastLapScore);   // ← NOWE
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            }));

            _insim.LayoutChanged += (s, layout) => BeginInvoke((Action)(() =>
            {
                _drift.SetLayout(layout);
                _overlay.UpdateBestLapScore(_drift.BestLapScore);
                // _overlay.UpdateLastLapScore(_drift.LastLapScore);   // ← NOWE
                _overlay.SetLapBoxVisible(_drift.HasActiveLapContext);
                UpdateLapContextLabel();
            }));
            _insim.CarReset += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID == _lastKnownPlayerPLID)
                {
                    // UWAGA: świadomie NIE wołamy tu _drift.ResetLapScore(). IS_CRS to
                    // "gracz wcisnął przycisk reset auta" — najczęstsza akcja podczas
                    // praktyki driftu (spin → reset), NIE koniec okrążenia. TotalScore
                    // nigdy się przy tym nie zeruje, więc zerowanie LapScore w tym miejscu
                    // powodowało narastający rozjazd między obiema liczbami: punkty zdobyte
                    // (także z kolizji z obiektami — ApplyPostPoints dolicza je symetrycznie
                    // do Total i Lap) zostawały w TotalScore, ale znikały z LapScore przy
                    // każdym resecie, co wyglądało jak nierówne przydzielanie punktów przy
                    // kolizji, choć problemem był ten reset, nie samo liczenie punktów.
                    // Prawdziwe granice okrążenia (koniec okrążenia / wjazd i wyjazd z pit
                    // lane / restart wyścigu / zejście z trasy) już mają własne, właściwe
                    // resety LapScore gdzie indziej — ten tutaj był nadmiarowy.
                    LoadVehicleRevSettings(_currentCarName);   // reset samochodu — przeładuj zapisane ustawienia
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
                    LoadVehicleRevSettings(_currentCarName);   // auto "przywrócone" na tor — przeładuj ustawienia
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

            _insim.CheckpointCrossed += (s, e) => BeginInvoke((Action)(() =>
            {
                if (e.PLID != _lastKnownPlayerPLID) return;
                if (e.CheckpointIndex != 1) return;   // tylko "Pierwszy punkt kontrolny"

                if (_drift.ActivateLapCounting())
                {
                    _overlay.UpdateLapScore(_drift.LapScore);
                    _overlay.SetLapBoxVisible(true);
                    UpdateLapContextLabel();
                }
                // jeśli liczenie/HUD już aktywne — przejazd przez 1. punkt kontrolny jest ignorowany
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
                if (e.PLID != _lastKnownPlayerPLID) return;
                HandleObjectHit(e.ObjectName);
            }));
            /*
            _insim.RawObjectHitDebug += (s, packet) => BeginInvoke((Action)(() =>
            {
                MessageBox.Show(
                    this,
                    $"IS_OBH odebrany! ({packet.Length} bajtów)\n\n" +
                    BitConverter.ToString(packet) + "\n\n" +
                    $"Index (offset 26) = {(packet.Length > 22 ? packet[22].ToString() : "?")}\n" +
                    $"OBHFlags (offset 27) = {(packet.Length > 23 ? packet[23].ToString() : "?")}",
                    "DEBUG: RAW IS_OBH",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }));
            */

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
                _indicatorClickOn.LoadAsync();
                _indicatorClickOff.LoadAsync();
                _indicatorCancel.LoadAsync();

                // Weryfikacja obecności plików — bez tego brakujący plik po prostu "milczy"
                // (SoundPlayer.Play/PlayLooping łyka wyjątek w naszym własnym try/catch niżej),
                // więc użytkownik nie ma jak się dowiedzieć CO konkretnie jest nie tak.
                string soundsDir = Path.Combine(System.Windows.Forms.Application.StartupPath, "Sounds");
                var missingSoundFiles = new List<string>();
                foreach (var fileName in new[] { "indicator_click_on.wav", "indicator_click_off.wav", "indicator_cancel.wav" })
                {
                    if (!File.Exists(Path.Combine(soundsDir, fileName)))
                        missingSoundFiles.Add(fileName);
                }

                if (missingSoundFiles.Count > 0)
                    _missingIndicatorSoundsWarning = "Brak plików dźwiękowych w folderze Sounds: " + string.Join(", ", missingSoundFiles);
            }
            catch { }

            _drift.DriftScored += OnDriftScored;
            _drift.DriftStarted += OnDriftStarted;
            _drift.DriftEnded += OnDriftEnded;

            _indicators.StateChanged += OnIndicatorStateChanged;
            // Dźwięki kierunkowskazów są sterowane REALNYM stanem kontrolki z OutGauge
            // (ShowLights), nie naszą wewnętrzną intencją CurrentState — patrz OnIndicatorLampStateChanged.
            _indicators.LampStateChanged += OnIndicatorLampStateChanged;

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

            _outGaugeFreshnessPoll.Tick += OnOutGaugeFreshnessPollTick;
            _outGaugeFreshnessPoll.Start();

            BuildUI();

            _hud = new InGameHudManager(
                _insim, _drift, _revLimiter,
                _showHudCheck, _showRPMHudCheck,
                _revLimiterNumeric, _angleValueLabel);

            LoadSettings();

            UpdateBestStatsLabels();

            // Dopiero teraz _statusLabel istnieje (utworzony w BuildUI) — pokaż ostrzeżenie
            // o brakujących plikach dźwiękowych kierunkowskazów, jeśli wykryto je wcześniej.
            if (!string.IsNullOrEmpty(_missingIndicatorSoundsWarning))
                _statusLabel.Text = _missingIndicatorSoundsWarning;

            // Pokaż diagnostykę OutGauge od razu ("BRAK DANYCH" dopóki nie przyjdzie
            // pierwszy pakiet) — bez tego panel Kierunkowskazów byłby pusty do momentu
            // połączenia z LFS i pierwszej ramki OutGauge.
            UpdateIndicatorDiagnosticsLabel();

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

            // "MPH" celowo NIE jest tłumaczone (uniwersalny skrót) — dlatego etykiety jednostki
            // prędkości nie są w generycznym rejestrze _localizedControls powyżej (inaczej zmiana
            // języka nadpisywałaby wybrane MPH z powrotem na przetłumaczone "km/h"). Odświeżamy
            // je ręcznie, respektując aktualny stan przełącznika _useMph.
            UpdateSpeedUnitLabels();
        }

        /// <summary>
        /// Odświeża etykiety jednostki prędkości (mały tekst pod cyfrą w panelu Speedometer +
        /// etykieta obok przełącznika km/h↔mph) zgodnie z aktualnym stanem _useMph. "MPH" jest
        /// uniwersalne (bez tłumaczenia), "km/h" korzysta z lokalizowanego klucza speedometer.unit.
        /// Wołane przy starcie, przy zmianie przełącznika i przy zmianie języka aplikacji.
        /// </summary>
        private void UpdateSpeedUnitLabels()
        {
            string unitText = _useMph ? "MPH" : Localization.T("speedometer.unit");

            if (_speedUnitLabel != null) _speedUnitLabel.Text = unitText;
            if (_speedUnitToggleLabel != null) _speedUnitToggleLabel.Text = unitText;
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
                    UpdateSpeedUnitLabels();   // domyślnie km/h (_useMph = false)

                    RefreshColorButtonSwatches();
                    SyncHudColors();

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

                _vehicleRevSettings = settings.VehicleRevLimiterSettings ?? new Dictionary<string, VehicleRevSettings>();

                InSimColor1 = settings.InSimColor1;
                InSimColor2 = settings.InSimColor2;
                InSimColor3 = settings.InSimColor3;
                InSimColor4 = settings.InSimColor4;
                InSimColor5 = settings.InSimColor5;
                SyncHudColors();

                if (settings.OverlayColor1 != -1) OverlayColor1 = Color.FromArgb(settings.OverlayColor1);
                if (settings.OverlayColor2 != -1) OverlayColor2 = Color.FromArgb(settings.OverlayColor2);
                if (settings.OverlayColor3 != -1) OverlayColor3 = Color.FromArgb(settings.OverlayColor3);
                if (settings.OverlayColor4 != -1) OverlayColor4 = Color.FromArgb(settings.OverlayColor4);
                if (settings.OverlayColor5 != -1) OverlayColor5 = Color.FromArgb(settings.OverlayColor5);

                SetButtonColor(_colorBtn1, InSimCodeToColor(InSimColor1));
                SetButtonColor(_colorBtn2, InSimCodeToColor(InSimColor2));
                SetButtonColor(_colorBtn3, InSimCodeToColor(InSimColor3));
                SetButtonColor(_colorBtn4, InSimCodeToColor(InSimColor4));
                SetButtonColor(_colorBtn5, InSimCodeToColor(InSimColor5));
                RefreshColorButtonSwatches();
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

                // Głośność/auto-cancel kierunkowskazów wczytujemy zawsze, niezależnie
                // od tego czy zapisana jest skonfigurowana kierownica (wcześniej ten blok
                // był zagnieżdżony w if() powyżej, więc bez kierownicy nigdy się nie ładował).
                try
                {
                    _indicatorSoundsVolume = Math.Clamp(settings.IndicatorSoundsVolume, 0, 100);
                    _indicatorVolumeSlider.SetValueSilent(_indicatorSoundsVolume);
                    _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                    _indicatorSoundsCheck.Checked = settings.IndicatorSoundsEnabled;
                    _indicatorAutoCancelCheck.Checked = settings.IndicatorAutoCancelOnCenter;

                    _speedoTachoEnabledCheck.Checked = settings.SpeedoTachoEnabled;
                    _speedoTachoOffsetXNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetX,
                        _speedoTachoOffsetXNumeric.Minimum, _speedoTachoOffsetXNumeric.Maximum);
                    _speedoTachoOffsetYNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetY,
                        _speedoTachoOffsetYNumeric.Minimum, _speedoTachoOffsetYNumeric.Maximum);
                    _speedoTachoScaleNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoScale,
                        _speedoTachoScaleNumeric.Minimum, _speedoTachoScaleNumeric.Maximum);
                    _speedoTachoRedlineColor = Color.FromArgb(settings.SpeedoTachoRedlineColor);
                    _speedoTachoTextColor = Color.FromArgb(settings.SpeedoTachoTextColor);
                    _speedoTachoIndicatorColor = Color.FromArgb(settings.SpeedoTachoIndicatorColor);
                    _speedoTachoTickColor = Color.FromArgb(settings.SpeedoTachoTickColor);
                    _speedoTachoBackgroundColor = Color.FromArgb(settings.SpeedoTachoBackgroundColor);

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
                }
                catch { }

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

                    OverlayColor1 = OverlayColor1.ToArgb(),
                    OverlayColor2 = OverlayColor2.ToArgb(),
                    OverlayColor3 = OverlayColor3.ToArgb(),
                    OverlayColor4 = OverlayColor4.ToArgb(),
                    OverlayColor5 = OverlayColor5.ToArgb(),

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

                    IndicatorSoundsEnabled = _indicatorSoundsCheck.Checked,
                    IndicatorSoundsVolume = _indicatorVolumeSlider.Value,
                    IndicatorAutoCancelOnCenter = _indicatorAutoCancelCheck.Checked,

                    SpeedoTachoEnabled = _speedoTachoEnabledCheck.Checked,
                    SpeedoTachoOffsetX = (float)_speedoTachoOffsetXNumeric.Value,
                    SpeedoTachoOffsetY = (float)_speedoTachoOffsetYNumeric.Value,
                    SpeedoTachoScale = (float)_speedoTachoScaleNumeric.Value,
                    SpeedoTachoRedlineColor = _speedoTachoRedlineColor.ToArgb(),
                    SpeedoTachoTextColor = _speedoTachoTextColor.ToArgb(),
                    SpeedoTachoIndicatorColor = _speedoTachoIndicatorColor.ToArgb(),
                    SpeedoTachoTickColor = _speedoTachoTickColor.ToArgb(),
                    SpeedoTachoBackgroundColor = _speedoTachoBackgroundColor.ToArgb(),
                    SpeedoTachoUseMph = _useMph,

                    VehicleRevLimiterSettings = _vehicleRevSettings,

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

        // Domyślne wartości stosowane, gdy dla danego auta nie ma jeszcze żadnych zapisanych
        // ustawień rev limitera — celowo WYSOKIE/bezpieczne (9000 RPM jest powyżej czerwonego
        // pola większości aut w LFS), żeby nowe/nieznane auto nie dostało przypadkowo obcięcia
        // zapłonu w normalnym zakresie obrotów, zanim użytkownik zdąży skalibrować (patrz
        // ShowRevLimiterCalibrationPromptIfNeeded — dla auta bez zapisanego presetu od razu
        // pojawia się okno z podpowiedzią kalibracji).
        private const int DefaultVehicleMaxRpm = 9000;
        private const int DefaultVehicleCutMs = 25;

        /// <summary>
        /// Wczytuje zapisane wcześniej ustawienia rev limitera (max RPM + cut ms) dla danego auta,
        /// jeśli takie istnieją. Wywoływane przy zmianie pojazdu (patrz OnRevData), przy resecie
        /// samochodu (IS_CRS) i przy wyjeździe z pit lane. Jeśli dla tego auta nic nie zapisano
        /// jeszcze — stosowane są wartości domyślne (DefaultVehicleMaxRpm/DefaultVehicleCutMs).
        /// </summary>
        private void LoadVehicleRevSettings(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName)) return;

            if (_vehicleRevSettings.TryGetValue(carName, out var vs))
            {
                ApplyVehicleRevValues(vs.MaxRpm, vs.CutMs,
                    $"Wczytano ustawienia rev limitera dla {carName}: {vs.MaxRpm} RPM / {vs.CutMs} ms");
                return;
            }

            // brak zapisanych ustawień dla tego auta — wartości domyślne, do ręcznej korekty
            // przez pole numeryczne albo przycisk CALIBRATE
            ApplyVehicleRevValues(DefaultVehicleMaxRpm, DefaultVehicleCutMs,
                $"{carName}: brak zapisanych ustawień rev limitera — użyto domyślnych {DefaultVehicleMaxRpm} RPM / {DefaultVehicleCutMs} ms");

            // Auto bez zapisanego presetu — podpowiedz użytkownikowi kalibrację od razu
            // (patrz ShowRevLimiterCalibrationPromptIfNeeded), zamiast liczyć na to, że
            // sam zauważy domyślną, niekalibrowaną wartość.
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

            if (!string.IsNullOrEmpty(statusMessage) && _statusLabel != null)
                _statusLabel.Text = statusMessage;
        }

        /// <summary>
        /// Czy Enter jest już czyimś bindowaniem (rev limiter/światła) — jeśli tak, tymczasowy
        /// globalny hotkey Enter dla okna kalibracji (patrz ShowRevLimiterCalibrationPromptIfNeeded)
        /// MUSIAŁby go nadpisać na czas otwarcia okna, więc zamiast tego po prostu z niego
        /// rezygnujemy (i nie obiecujemy go w komunikacie) — użytkownik ma wtedy własny klawisz.
        /// </summary>
        private bool IsEnterBoundElsewhere() =>
            (_revToggleBinding.Kind == InputKind.Keyboard && _revToggleBinding.Key == Keys.Enter) ||
            (_revCalibrateBinding.Kind == InputKind.Keyboard && _revCalibrateBinding.Key == Keys.Enter) ||
            (_revDecreaseBinding.Kind == InputKind.Keyboard && _revDecreaseBinding.Key == Keys.Enter) ||
            (_revIncreaseBinding.Kind == InputKind.Keyboard && _revIncreaseBinding.Key == Keys.Enter) ||
            (_lightToggleBinding.Kind == InputKind.Keyboard && _lightToggleBinding.Key == Keys.Enter);

        /// <summary>
        /// Pokazuje pływające okno-podpowiedź (patrz RevLimiterCalibrationPromptForm) z
        /// przyciskiem uruchamiającym DOKŁADNIE ten sam mechanizm kalibracji co przycisk
        /// CALIBRATE / zbindowany klawisz/przycisk kierownicy (RPMLimitterCalibrate) — RAZ
        /// na auto w ramach tego uruchomienia aplikacji.
        ///
        /// Dodatkowo, na czas gdy okno jest otwarte, Enter działa jak kolejny "zbindowany
        /// przycisk" — rejestrowany jako TYMCZASOWY globalny hotkey (ten sam mechanizm co
        /// _globalHotkey.SetBinding dla zwykłych bindowań, patrz GlobalHotkey.cs), więc działa
        /// NIEZALEŻNIE od tego, czy to okno ma fokus (a nie ma — patrz WS_EX_NOACTIVATE w
        /// RevLimiterCalibrationPromptForm), czyli też podczas jazdy w LFS. Usuwany natychmiast
        /// przy zamknięciu okna, żeby nie zostawić klawisza Enter globalnie zbindowanego.
        /// </summary>
        private void ShowRevLimiterCalibrationPromptIfNeeded(string carName)
        {
            if (_revLimiterPromptShownForCars.Contains(carName)) return;
            _revLimiterPromptShownForCars.Add(carName);

            _revLimiterCalibrationPrompt?.Close();

            bool enterAvailable = !IsEnterBoundElsewhere();

            _revLimiterCalibrationPrompt = new RevLimiterCalibrationPromptForm(
                this, carName, () => RPMLimitterCalibrate(), enterAvailable);

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

        /// <summary>
        /// Zapamiętuje AKTUALNE wartości pól max RPM / cut ms jako ustawienia konkretnego auta —
        /// wołane przy każdej ręcznej zmianie tych pól (patrz onValueChanged w BuildUI) oraz tuż
        /// przed przełączeniem na inny pojazd, żeby nie zgubić tego, co użytkownik ustawił.
        /// </summary>
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
            BeginInvoke((Action)(() =>
            {
                // Sygnalizuj overlayowi "żyjące" dane OutGauge — steruje widocznością
                // prędkościomierza+obrotomierza i blokadą HUD-u wyniku w trybie idle,
                // gdy dane milkną (menu/garaż/poza autem). Patrz OverlayForm.NotifyOutGaugeData.
                _overlay.NotifyOutGaugeData();
                _outGaugeFreshness.Ping();

                _drift.SetHandbrakeActive(data.HandbrakeOn);
                _rpmLabel.Text = ((int)data.RPM).ToString("N0");
                _overlay.UpdateRpm((int)data.RPM);
                _overlay.UpdateGear((int)data.Gear);   // ← NOWE: zasila obrotomierz aktualnym biegiem
                _rpmBar.Value = (int)Math.Min(data.RPM, _rpmBar.Maximum);


                // Podświetl czerwono gdy blisko limitu
                _rpmLabel.ForeColor = data.RPM >= _revLimiter.RpmLimit * 0.95
                    ? Color.FromArgb(255, 60, 60)
                    : Color.FromArgb(80, 220, 120);

                if (calibrationON == true)
                {
                    if (data.RPM > CalibratedMAXRPM) { CalibratedMAXRPM = (int)data.RPM; } else { }
                    _revLimiterCalibrationPrompt?.UpdateLiveRpm(CalibratedMAXRPM);
                }

                // ── Ustawienia rev limitera per pojazd ──────────────────────────────────
                // OutGauge niesie krótki kod auta w KAŻDYM pakiecie (data.Car), więc to
                // najprostsze i najbardziej wiarygodne źródło wykrywania zmiany pojazdu —
                // nie trzeba osobno podpinać się pod IS_NPL/IS_SLC z InSim. Zmiana wykryta →
                // zapisz ustawienia poprzedniego auta (jeśli jakieś śledziliśmy), wczytaj
                // zapisane wcześniej dla nowego (jeśli istnieją), inaczej wartości domyślne.
                if (!string.IsNullOrEmpty(data.Car) && data.Car != _currentCarName)
                {
                    if (!string.IsNullOrEmpty(_currentCarName))
                        SaveVehicleRevSettings(_currentCarName);

                    _currentCarName = data.Car;
                    LoadVehicleRevSettings(_currentCarName);
                }

                //_revLimiterNumeric.Value = CalibratedMAXRPM;
                _hud.ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());

                // ── Telemetria silnika dla wykrywania burnoutu (patrz DriftEngine.UpdateBurnout) ──
                // Próby estymowania prędkości kół (z RPM+przełożenia, potem z OutSim AngVel)
                // okazały się niewiarygodne w praktyce — usunięte. Burnout wykrywamy teraz
                // wprost z RPM/gazu/biegu z OutGauge, bez pośredniej estymacji.
                _drift.UpdateEngineTelemetry(data.RPM, data.Throttle, (int)data.Gear);

                // ── Weryfikacja kontrolek kierunkowskazów z OutGauge ──────────────
                // Realny stan lampki na desce rozdzielczej LFS (nie nasza intencja
                // przełącznika) — na tej podstawie synchronizowane są dźwięki
                // indicator_click_on.wav / indicator_click_off.wav / indicator_cancel.wav,
                // patrz OnIndicatorLampStateChanged.
                _indicators.UpdateFromOutGauge(data.LeftSignalOn, data.RightSignalOn, data.AnySignalOn);

                // Diagnostyka widoczna w panelu Kierunkowskazów (patrz UpdateIndicatorDiagnosticsLabel) —
                // licznik pakietów rosnący na oczach użytkownika to najprostszy dowód, że OutGauge
                // w ogóle dociera do aplikacji. Throttlowane do ~5x/s, żeby nie zamulać UI (OutGauge
                // potrafi wysyłać dane znacznie częściej niż trzeba to odświeżać na ekranie).
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

            Size = new Size(890, 670 + TitleBarHeight);

            MinimumSize = new Size(890, 670 + TitleBarHeight);

            MaximumSize = Size;

            DoubleBuffered = true;

            BackColor = Color.Black;

            Font = new Font("Segoe UI", 10f);

            //Region = CreateSmoothRoundedRegion(Width, Height, 20);




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
                    // Zablokuj przełącznik na czas próby połączenia — jeśli się nie uda
                    // (zły host/port, LFS bez otwartego /insim, timeout), sam wróci do
                    // OFF (patrz OnConnectFailed) zamiast zostać widocznie "włączony"
                    // mimo braku realnego połączenia z grą. Connect() leci w tle, bo
                    // nawet z ograniczonym timeoutem (patrz InSimConnection) to wciąż
                    // blokujące wywołanie sieciowe — nie chcemy zamrażać UI na ten czas.
                    _connectionSwitch.Enabled = false;

                    string host = _hostBox.Text.Trim();
                    int port = (int)_portBox.Value;
                    string admin = _adminBox.Text;
                    Task.Run(() => _insim.Connect(host, port, admin));
                }
                else
                {
                    // blokada przełącznika na czas rozłączania, żeby użytkownik
                    // nie zmienił stanu w trakcie sekwencji
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
                260, locKey: "speedometer.title");


            // ── Speedometer ───────────────────────────────────
            _speedometer = new SpeedometerControl
            {
                Location = new Point(140, 55),
                Size = new Size(240, 130),
                BackColor = Color.White
            };
            // speedometerPanel.Controls.Add(_speedometer);

            _speedLabel = MakeLabel(speedometerPanel, "0", 20, 85, 70, 25, Color.FromArgb(255, 200, 50), new Font("Segoe UI", 24f, FontStyle.Bold));
            _speedUnitLabel = MakeLabel(speedometerPanel, Localization.T("speedometer.unit"), 20, 120, 50, 20, ApplePalette.Text);
            MakeLabel(speedometerPanel, "SPEED", 20, 60, 70, 20, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), locKey: "speedometer.speed");

            // ── Ustawienia HUD-u prędkościomierz+obrotomierz (overlay, styl Forza) ──────
            _speedoTachoEnabledCheck = new MacToggleSwitch
            {
                //Text = Localization.T("speedometer.showhud"),
                Location = new Point(180, 10),
                //Size = new Size(200, 20),
                BackColor = Color.Transparent,
                Checked = true
            };
            _speedoTachoEnabledCheck.CheckedChanged += (s, e) =>
            {
                _overlay.SpeedoTachoEnabled = _speedoTachoEnabledCheck.Checked;
                SaveSettings();
            };
            speedometerPanel.Controls.Add(_speedoTachoEnabledCheck);
            _localizedControls.Add((_speedoTachoEnabledCheck, "speedometer.showhud"));

            MakeLabel(speedometerPanel, "X:", 90, 140 - 80, 16, 22, ApplePalette.Text, locKey: "speedometer.offsetx");
            _speedoTachoOffsetXNumeric = MakeNumericUpDown(
                speedometerPanel, 140, 140 - 82, 75, 28, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetX = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Y:", 90, 180 - 80, 16, 22, ApplePalette.Text, locKey: "speedometer.offsety");
            _speedoTachoOffsetYNumeric = MakeNumericUpDown(
                speedometerPanel, 140, 180 - 82, 75, 28, -500, 500, 0, 5,
                v => { _overlay.SpeedoTachoOffsetY = (float)v; SaveSettings(); });

            MakeLabel(speedometerPanel, "Scale:", 90, 220 - 80, 45, 22, ApplePalette.Text, locKey: "speedometer.scale");
            _speedoTachoScaleNumeric = MakeNumericUpDown(
                speedometerPanel, 140, 220 - 82, 75, 28, 0.3m, 2.5m, 1.0m, 0.05m,
                v => { _overlay.SpeedoTachoScale = (float)v; SaveSettings(); });
            _speedoTachoScaleNumeric.DecimalPlaces = 2;

            // ── Przełącznik jednostki prędkości (km/h ↔ mph) — pomniejszony MacToggleSwitch,
            // wpasowany w wolną przestrzeń pod blokiem SPEED (lewa kolumna, x=15-88), nad
            // checkboxem "Show Speedo+Tacho HUD". Wpływa zarówno na overlay HUD (prędkościomierz+
            // obrotomierz), jak i na zwykłą etykietę prędkości w tym panelu — jedna, spójna
            // jednostka w całej aplikacji. MPH nie jest tłumaczone (uniwersalny skrót), dlatego
            // etykieta jest odświeżana ręcznie (UpdateSpeedUnitLabels), nie przez generyczny
            // mechanizm _localizedControls, żeby zmiana języka nie nadpisała wybranego MPH z powrotem na km/h.
            _speedUnitToggle = new MacToggleSwitch
            {
                Location = new Point(15, 145),
                Size = new Size(34, 20),   // pomniejszony (domyślnie 51x30)
                Checked = false            // false = km/h, true = mph
            };
            speedometerPanel.Controls.Add(_speedUnitToggle);

            _speedUnitToggleLabel = MakeLabel(speedometerPanel, "", 54, 145, 34, 20,
                ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.MiddleLeft);

            _speedUnitToggle.CheckedChanged += (s, e) =>
            {
                _useMph = _speedUnitToggle.Checked;
                _overlay.SpeedoTachoUseMph = _useMph;
                UpdateSpeedUnitLabels();
                SaveSettings();
            };
            UpdateSpeedUnitLabels();   // ustaw początkowy tekst etykiet (km/h)

            var hudColorsBtn = MakeButton(speedometerPanel, Localization.T("hud.colors"), 15, 215, 210, 30, ApplePalette.Blue);
            hudColorsBtn.Click += (s, e) => ShowHudColorsMenu();
            _localizedControls.Add((hudColorsBtn, "hud.colors"));

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

            MakeLabel(scorePanel, "COMBO", 150 + marginSide, marginTop + 20, 60, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.combo");
            _comboValueLabel = MakeLabel(scorePanel, "x1", 150 + marginSide, marginTop + 40, 60, 20, Color.FromArgb(255, 120, 40), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "DRIFT ANGLE", 150 + marginSide, marginTop + 70, 70, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.angle");
            _angleValueLabel = MakeLabel(scorePanel, "0°", 150 + marginSide, marginTop + 90, 50, 20, ApplePalette.Text, new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "BEST RUN", 240 + marginSide, marginTop + 20, 160, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.bestrun");
            _bestRunValueLabel = MakeLabel(scorePanel, "0", 240 + marginSide, marginTop + 40, 160, 18, Color.FromArgb(255, 200, 50), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "BEST DRIFT", 240 + marginSide, marginTop + 70, 75, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.bestdrift");
            _bestDriftValueLabel = MakeLabel(scorePanel, "0.0s", 240 + marginSide, marginTop + 90, 75, 18, Color.FromArgb(60, 180, 255), new Font("Segoe UI", 14f, FontStyle.Bold));

            MakeLabel(scorePanel, "BEST DEEP DRIFT", 325 + marginSide, marginTop + 70, 80, 14, ApplePalette.Text, new Font("Segoe UI", 7.5f, FontStyle.Bold), ContentAlignment.TopLeft, locKey: "score.bestdeepdrift");
            _bestDeepDriftValueLabel = MakeLabel(scorePanel, "0.0s", 325 + marginSide, marginTop + 90, 80, 18, Color.FromArgb(255, 80, 80), new Font("Segoe UI", 14f, FontStyle.Bold));



            _resetBtn = MakeButton(scorePanel, "RESET SCORE", 240 + marginSide, 10, 160, 30, Color.FromArgb(200, 20, 20), locKey: "score.reset");

            _resetBtn.Enabled = false;


            _resetBtn.Click += (s, e) => { _drift.ResetTotal(); UpdateScoreLabels(); };


            hudPanel = CreateCard(
                "HUD Settings",
                270,
                330 + TitleBarHeight,
                280,
                260, locKey: "hud.title");

            MakeLabel(hudPanel, "Colors:", 15, marginTop + 15, 100, 22, ApplePalette.Text, null, ContentAlignment.MiddleLeft, locKey: "hud.colors");


            _colorBtn1 = MakeButton(hudPanel, "", 15, 80, 45, 45, Color.White);
            _colorBtn2 = MakeButton(hudPanel, "", 65, 80, 45, 45, Color.Cyan);
            _colorBtn3 = MakeButton(hudPanel, "", 115, 80, 45, 45, Color.Yellow);
            _colorBtn4 = MakeButton(hudPanel, "", 165, 80, 45, 45, Color.Magenta);
            _colorBtn5 = MakeButton(hudPanel, "", 215, 80, 45, 45, Color.Red);


            _colorBtn1.Click += (s, e) => HandleColorButtonClick(_colorBtn1, 1);
            _colorBtn2.Click += (s, e) => HandleColorButtonClick(_colorBtn2, 2);
            _colorBtn3.Click += (s, e) => HandleColorButtonClick(_colorBtn3, 3);
            _colorBtn4.Click += (s, e) => HandleColorButtonClick(_colorBtn4, 4);
            _colorBtn5.Click += (s, e) => HandleColorButtonClick(_colorBtn5, 5);

            _showHudCheck = new MacCheckBox
            {
                Text = "Currently unavailable",

                Location = new Point(16, 155),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = false
               

            };

            _showOverlayCheck = new MacCheckBox
            {
                Text = "Forza-style overlay",
                Location = new Point(16, 130),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = true
            };

            _showOverlayCheck.CheckedChanged += (s, e) =>
            {
                if (_showOverlayCheck.Checked)
                {
                    IntPtr lfsHwnd = FindLfsWindow();
                    if (lfsHwnd != IntPtr.Zero)
                    {
                        _overlay.UpdateScore(_drift.TotalScore);
                        _overlay.UpdateAccentColor(OverlayColor1);   // ← zmienione z InSimCodeToColor(InSimColor1)
                        _overlay.UpdateMaxRpm(CalibratedMAXRPM);   // ← NOWE: startowa kalibracja obrotomierza
                        _overlay.AttachTo(lfsHwnd);
                        //_showRPMHudCheck.Checked = false;

                    }
                    _showHudCheck.Checked = false;
                }
                else
                {
                    _overlay.Detach();
                }
                SaveSettings();
                RefreshColorButtonSwatches();   // ← NOWE
            };

            _showRPMHudCheck = new MacCheckBox
            {

                Text = "Show ingame REV Limitter HUD",

                Location = new Point(16, 180),
                Size = new Size(240, 20),
                BackColor = Color.Transparent,
                Checked = true

            };

            hudPanel.Controls.Add(_showOverlayCheck);


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


            hudPanel.Controls.Add(_showHudCheck);
            hudPanel.Controls.Add(_showRPMHudCheck);
            _localizedControls.Add((_showHudCheck, "hud.show-disabled"));
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
                v =>
                {
                    _revLimiter.RpmLimit = (int)v;
                    _rpmBar.Maximum = (int)v;   // pasek RPM ma zawsze skalę do aktualnego limitu
                    _overlay.UpdateMaxRpm((int)v);   // ← NOWE: obrotomierz przekalibrowuje się na bieżąco
                    SaveVehicleRevSettings(_currentCarName);
                }
            );
            //_hud.ShowInGameRPMLimitter((_revLimiterNumeric.Value).ToString());
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
                v =>
                {
                    _revLimiter.CutMs = (int)v;
                    SaveVehicleRevSettings(_currentCarName);
                }
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
               260, locKey: "indicators.title");

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

                // Auto-cancel steruje realnym stanem kierunkowskazów (i finalnie wysyła klawisz
                // do LFS w OnIndicatorStateChanged) — bez połączenia z InSim nie ma to sensu
                // i mogłoby rozjechać lokalny stan IndicatorManager z tym co "myśli" gra.
                if (_insim.IsConnected && _indicatorAutoCancelCheck != null && _indicatorAutoCancelCheck.Checked)
                {
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

            _indicatorSoundsCheck = new MacCheckBox
            {
                Text = Localization.T("indicators.soundscheck"),

                Location = new Point(15, 175),
                Size = new Size(270, 20),
                BackColor = Color.Transparent,
                Checked = true
            };
            _indicatorSoundsCheck.CheckedChanged += (s, e) =>
            {
                if (!_indicatorSoundsCheck.Checked)
                {
                    _indicatorClickOn.Stop();
                    _indicatorClickOff.Stop();
                    _indicatorCancel.Stop();
                }
                SaveSettings();
            };
            indicatorPanel.Controls.Add(_indicatorSoundsCheck);

            _indicatorAutoCancelCheck = new MacCheckBox
            {
                Text = Localization.T("indicators.centeroff"),

                Location = new Point(15, 200),
                Size = new Size(270, 20),
                BackColor = Color.Transparent,
                Checked = true
            };
            _indicatorAutoCancelCheck.CheckedChanged += (s, e) => SaveSettings();
            indicatorPanel.Controls.Add(_indicatorAutoCancelCheck);

            MakeLabel(indicatorPanel, Localization.T("indicators.volume"), 15, 230, 65, 22, locKey: "indicators.volume");
            _indicatorVolumeSlider = new MacSlider
            {
                Location = new Point(80, 230),
                Size = new Size(170, 22),
                Minimum = 0,
                Maximum = 100,
                Value = _indicatorSoundsVolume
            };

            _indicatorVolumeValueLabel = MakeLabel(indicatorPanel, $"{_indicatorSoundsVolume}%", 260, 230, 45, 22,
                ApplePalette.Text, null, ContentAlignment.MiddleLeft);

            _indicatorVolumeSlider.ValueChanged += (s, e) =>
            {
                _indicatorSoundsVolume = _indicatorVolumeSlider.Value;
                _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                ApplyIndicatorVolume();
                SaveSettings();
            };

            indicatorPanel.Controls.Add(_indicatorVolumeSlider);

            _localizedControls.Add((_indicatorAutoCancelCheck, "indicators.centeroff"));
            _localizedControls.Add((_indicatorSoundsCheck, "indicators.soundscheck"));


            statusPanel = CreateCard(
                "",
                20,
                600 + TitleBarHeight,
                850,
                50);

            _statusLabel = MakeLabel(statusPanel, "Type /insim 29999 w LFS, and click CONNECT.", 20, 16, 620, 18, ApplePalette.Text, new Font("Segoe UI", 8f), locKey: "status.hint");


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

        // Bindowania (klawisz/kierownica) sterujące rev limiterem i światłami mają działać
        // tylko gdy program jest połączony z grą — bez tego kalibracja/przełączanie i tak
        // nie ma żadnego efektu w LFS, a mogłoby np. przypadkowo nadpisać CalibratedMAXRPM
        // przez ExecuteRevIncrease/Decrease bez faktycznej sesji jazdy.
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

                // Bindowanie kierownicy dla kierunkowskazów ma działać TYLKO gdy program
                // jest połączony z grą przez InSim — bez połączenia przełączanie i tak
                // niczego by nie wysłało do LFS, a mogłoby zostawić niespójny stan
                // IndicatorManager po (re)connect. Blokada na poziomie samego wywołania,
                // nie na poziomie wskaźnika w UI, żeby zadziałało niezależnie od tego,
                // skąd akcja została zarejestrowana.
                _wheelInput.SetBinding(newBinding.WheelButton, () =>
                {
                    if (_insim.IsConnected) wheelAction();
                });
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

            _hud.ShowInGameAward("REV LIMITTER CALIBRATION - SELECT NEUTRAL AND HOLD FULL THROTTLE!!!");
            _revLimiterCalibrationPrompt?.ShowCalibratingState();

            _revLimitLabel.Text = "CALIBRATION... ";
            //PressWKey(true);
            //_revLimitLabel.ForeColor = Color.FromArgb(255, 60, 60);

            await Task.Delay(4000);   // nie blokuje UI

            calibrationON = false;

            CalibratedMAXRPM = CalibratedMAXRPM - 50;
            _revLimitLabel.Text = CalibratedMAXRPM.ToString();
            _revLimitLabel.Text = "RPM LIMIT: ";
            //_revLimitLabel.ForeColor = Color.FromArgb(60, 60, 60);
            _revLimiterNumeric.Value = CalibratedMAXRPM;   // ← wyzwala też SaveVehicleRevSettings

            _revLimiter.Enabled = wasEnabledBeforeCalibration;
            _revEnableSwitch.SetCheckedSilent(wasEnabledBeforeCalibration);
            _hud.ShowInGameAward($"RPM LIMIT: {CalibratedMAXRPM}");
            _hud.ShowInGameRPMLimitter(CalibratedMAXRPM.ToString());
            _revLimiterCalibrationPrompt?.ShowDoneState(CalibratedMAXRPM);
            await Task.Delay(1000);

            _hud.ShowInGameAward("REV LIMITTER CALIBRATION DONE!!!");
            await Task.Delay(1000);
            _hud.ShowInGameAward($"RPM LIMIT: {CalibratedMAXRPM}");
            await Task.Delay(1000);
            _hud.ShowInGameAward("REV LIMITTER CALIBRATION DONE!!!");


            //PressWKey(false);
            await Task.Delay(3000);
            _hud.ShowInGameAward($"");
            _revLimitLabel.Text = "RPM LIMIT: ";
            SaveSettings();

            // Wynik już był widoczny przez ~5s (ShowDoneState powyżej + opóźnienia nad tą
            // linią) — teraz zamknij okno-podpowiedź, jeśli wciąż otwarte.
            _revLimiterCalibrationPrompt?.Close();
            _revLimiterCalibrationPrompt = null;
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
            // Ten sam ciemny "backdrop" co ShowHudColorsMenu/OpenLanguagePicker itd. —
            // przyciemnia resztę aplikacji pod modalnym dialogiem.
            Form overlayBg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
            };

            using var dlg = new WheelSetupForm(this, _wheelInput, devices);
            dlg.Owner = overlayBg;

            overlayBg.Show();
            try
            {
                if (dlg.ShowDialog(overlayBg) == DialogResult.OK)
                {
                    _savedWheelGuid = dlg.SelectedDeviceGuid;
                    _savedWheelAxis = dlg.SelectedAxis;
                    SaveSettings();
                    _statusLabel.Text = $"Kierownica skonfigurowana: {_wheelInput.DeviceName} (oś: {_wheelInput.SteeringAxis})";
                }
            }
            finally
            {
                overlayBg.Close();
                overlayBg.Dispose();
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
        // ────────────────────────────────────────────────────────
        // Dialog konfiguracji kierownicy — DOKŁADNIE ten sam mechanizm rysowania co
        // ShowHudColorsMenu/OpenLanguagePicker itd.: prawdziwy zaokrąglony KSZTAŁT okna
        // przez Region (nie tylko rysunek na prostokątnym canvasie), miękki cień w Paint,
        // przycisk "×" w tym samym stylu, i przyciski treści przez owner.MakeButton (ta
        // sama instancja MainForm co wszędzie indziej — bez lokalnie duplikowanego
        // koloru/gradientu). Kolor przycisków to teraz DOMYŚLNY ApplePalette.Blue
        // (MakeButton bez podanego bg), tak jak w reszcie aplikacji — bez ręcznych
        // override'ów na Secondary/Card jak poprzednio.
        // ────────────────────────────────────────────────────────
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

            // Chrome (karta + × + tytuł) jest stałe; obie "strony" budują się tylko
            // wewnątrz _content, więc przełączanie stron nie rusza reszty okna.
            private readonly Panel _content;

            public WheelSetupForm(MainForm owner, SteeringWheelInput wheelInput, List<(Guid Guid, string Name)> devices)
            {
                _owner = owner;
                _wheelInput = wheelInput;
                _devices = devices;

                Text = Localization.T("wheelconfig.title");
                Size = new Size(380, 460);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                BackColor = ApplePalette.Background;

                Shown += (s, e) => Region = _owner.CreateSmoothRoundedRegion(Width, Height, 20);

                var card = new RoundedPanel { Dock = DockStyle.Fill };
                Controls.Add(card);

                var closeButton = new Button
                {
                    Text = "×",
                    Size = new Size(28, 28),
                    Location = new Point(Width - 38, 10),
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
                closeButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                closeButton.Region = _owner.CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);
                card.Controls.Add(closeButton);

                _owner.MakeLabel(card, Localization.T("wheelconfig.title"), 20, 15, 250, 24,
                    ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

                _content = new Panel
                {
                    Location = new Point(20, 50),
                    Size = new Size(Width - 40, Height - 50 - 20),
                    BackColor = Color.Transparent,
                };
                card.Controls.Add(_content);

                Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    for (int i = 30; i >= 1; i--)
                    {
                        int alpha = (int)(22 * (1.0 - i / 30.0));
                        Rectangle shadowRect = new Rectangle(12 - i, 12 - i, Width - 24 + i * 2, Height - 24 + i * 2);
                        using (GraphicsPath p = _owner.RoundedPath(shadowRect, 20 + i))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                            e.Graphics.FillPath(b, p);
                    }
                };

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

                    var guid = d.Guid; // capture
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

                // podłącz od razu z osią domyślną X — użytkownik zaraz ją potwierdzi lub zmieni
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

        // ────────────────────────────────────────────────────────
        // Okno-podpowiedź kalibracji rev limitera dla auta bez zapisanego presetu —
        // ten sam ciemny styl co KeyBindingForm powyżej, TopMost nad grą (patrz
        // ShowRevLimiterCalibrationPromptIfNeeded). Sam nie prowadzi kalibracji —
        // tylko woła przekazany callback (RPMLimitterCalibrate) i odzwierciedla jego
        // stan (ShowCalibratingState/UpdateLiveRpm/ShowDoneState), więc działa tak
        // samo niezależnie czy kalibrację uruchomiono z tego okna, z przycisku
        // CALIBRATE w głównym oknie, czy ze zbindowanego klawisza/przycisku kierownicy.
        // ────────────────────────────────────────────────────────
        public class RevLimiterCalibrationPromptForm : Form
        {
            private readonly MainForm _owner;
            private readonly Label _messageLabel;
            private readonly Label _liveRpmLabel;
            private readonly Button _calibrateButton;
            private readonly Func<Task> _startCalibration;

            // ── Nie kradnij fokusu z LFS ─────────────────────────────────────────
            // Domyślnie WinForms aktywuje (i przechwytuje klawiaturę) każde okno
            // pokazane przez Show() — dla zwykłego okna to normalne, ale TO okno wisi
            // TopMost nad grą właśnie wtedy, gdy gracz trzyma gaz do kalibracji, więc
            // przechwycenie klawiatury odcinałoby sterowanie w LFS (WASD/gaz/biegi).
            // WS_EX_NOACTIVATE sprawia, że okno NIGDY nie staje się aktywne/nie
            // przejmuje fokusu klawiatury — także po kliknięciu — a mimo to jego
            // kontrolki (przycisk CALIBRATE) nadal normalnie reagują na klik myszą,
            // bo Windows i tak routuje komunikaty myszy do okna pod kursorem
            // niezależnie od aktywacji. ShowWithoutActivation to oficjalny "hak"
            // WinForms na tę samą sytuację przy samym Show() (bez tego .NET i tak
            // próbowałby aktywować okno przy pierwszym pokazaniu).
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

            // Ten sam mechanizm rysowania co ShowHudColorsMenu/OpenLanguagePicker itd. —
            // prawdziwy zaokrąglony KSZTAŁT okna przez Region (nie tylko rysunek na
            // prostokątnym canvasie), miękki cień w Paint, i przyciski przez owner.MakeButton
            // (ta sama instancja MainForm, więc identyczny gradient/poświata co wszędzie
            // indziej w aplikacji — bez lokalnie duplikowanej wersji tego kodu).
            public RevLimiterCalibrationPromptForm(MainForm owner, string carName, Func<Task> startCalibration, bool enterHintAvailable)
            {
                _owner = owner;
                _startCalibration = startCalibration;

                Text = Localization.T("rev.calibration_prompt.title");
                Size = new Size(440, 250);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;   // nad grą, niezależnie gdzie akurat jest okno LFS
                BackColor = ApplePalette.Background;

                Shown += (s, e) => Region = _owner.CreateSmoothRoundedRegion(Width, Height, 20);

                var card = new RoundedPanel { Dock = DockStyle.Fill };
                Controls.Add(card);

                var closeButton = new Button
                {
                    Text = "×",
                    Size = new Size(28, 28),
                    Location = new Point(Width - 38, 10),
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
                closeButton.Click += (s, e) => Close();
                closeButton.Region = _owner.CreateSmoothRoundedRegion(closeButton.Width, closeButton.Height, 14);
                card.Controls.Add(closeButton);

                _owner.MakeLabel(card, Localization.T("rev.calibration_prompt.title"), 20, 15, 300, 24,
                    ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

                // Wzmianka o Enter dopisywana TYLKO gdy faktycznie działa (patrz
                // MainForm.ShowRevLimiterCalibrationPromptIfNeeded) — nie okłamujemy
                // użytkownika, jeśli akurat koliduje z jego własnym bindowaniem klawisza Enter.
                string bodyText = string.Format(Localization.T("rev.calibration_prompt.body"), carName)
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

                // Lokalny fallback: DZIAŁA tylko jeśli to okno akurat ma fokus klawiatury
                // (np. LFS nie jest aktywny) — awarie WS_EX_NOACTIVATE. Głównym mechanizmem
                // "Enter potwierdza kalibrację" jest tymczasowy GLOBALNY hotkey rejestrowany
                // z zewnątrz (patrz MainForm.ShowRevLimiterCalibrationPromptIfNeeded), który
                // działa NIEZALEŻNIE od fokusu — czyli też podczas jazdy w LFS.
                AcceptButton = _calibrateButton;

                Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    for (int i = 30; i >= 1; i--)
                    {
                        int alpha = (int)(22 * (1.0 - i / 30.0));
                        Rectangle shadowRect = new Rectangle(12 - i, 12 - i, Width - 24 + i * 2, Height - 24 + i * 2);
                        using (GraphicsPath p = _owner.RoundedPath(shadowRect, 20 + i))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                            e.Graphics.FillPath(b, p);
                    }
                };
            }

            /// <summary>Wywoływane, gdy kalibracja faktycznie się rozpoczyna (niezależnie
            /// od tego, co ją uruchomiło) — pokazuje instrukcję i odsłania licznik RPM.</summary>
            public void ShowCalibratingState()
            {
                _messageLabel.Text = Localization.T("rev.calibration_prompt.inprogress");
                _messageLabel.TextAlign = ContentAlignment.MiddleCenter;
                _liveRpmLabel.Visible = true;
                _liveRpmLabel.Text = "0 RPM";
                _calibrateButton.Enabled = false;
            }

            /// <summary>Aktualny, na bieżąco odczytany maksymalny RPM podczas kalibracji.</summary>
            public void UpdateLiveRpm(int rpm)
            {
                if (!_liveRpmLabel.Visible) return;
                _liveRpmLabel.Text = rpm.ToString("N0") + " RPM";
            }

            public void ShowDoneState(int finalRpm)
            {
                _messageLabel.Text = string.Format(Localization.T("rev.calibration_prompt.done"), finalRpm);
                _messageLabel.TextAlign = ContentAlignment.MiddleCenter;
                _liveRpmLabel.Visible = false;
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

        /// <summary>
        /// Podmenu "HUD Colors" — lista wszystkich konfigurowalnych kolorów prędkościomierza+
        /// obrotomierza (redline, tekst, wskaźnik/igła, kreski/ticki, tło). Każdy wiersz otwiera
        /// ten sam picker RGB+A (OpenCustomColorPicker z includeAlpha:true) dla danego elementu.
        /// </summary>
        private void ShowHudColorsMenu()
        {
            Form overlayBg = new Form
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
                Size = new Size(300, 310),
                BackColor = ApplePalette.Background
            };

            popup.Shown += (s, e) => popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 20);

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

            MakeLabel(card, "HUD Colors", 20, 15, 200, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));
            MakeLabel(card, "Speedo+Tacho — RGB i przezroczystość", 20, 40, 250, 16,
                Color.FromArgb(140, 140, 170), new Font("Segoe UI", 7.5f));

            int y = 66;
            void AddRow(string label, Func<Color> getColor, Action<Color> setColor)
            {
                MakeLabel(card, label, 20, y + 6, 150, 20, ApplePalette.Text);

                var swatch = MakeButton(card, "", 195, y, 65, 30, getColor());
                swatch.Click += (s, e) => OpenCustomColorPicker(swatch, getColor(), label, setColor, includeAlpha: true);
                card.Controls.Add(swatch);

                y += 40;
            }

            AddRow("Redline", () => _speedoTachoRedlineColor, c => { _speedoTachoRedlineColor = c; _overlay.RedlineColor = c; });
            AddRow("Text", () => _speedoTachoTextColor, c => { _speedoTachoTextColor = c; _overlay.SpeedoTachoTextColor = c; });
            AddRow("Indicator", () => _speedoTachoIndicatorColor, c => { _speedoTachoIndicatorColor = c; _overlay.SpeedoTachoIndicatorColor = c; });
            AddRow("Ticks", () => _speedoTachoTickColor, c => { _speedoTachoTickColor = c; _overlay.SpeedoTachoTickColor = c; });
            AddRow("Background", () => _speedoTachoBackgroundColor, c => { _speedoTachoBackgroundColor = c; _overlay.SpeedoTachoBackgroundColor = c; });

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

            overlayBg.Show();
            popup.Owner = overlayBg;

            try { popup.ShowDialog(overlayBg); }
            finally { overlayBg.Close(); overlayBg.Dispose(); }
        }

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
            // Fires on the InSim receive thread, not the UI thread.
            byte viewPlid = _insim.ViewPLID;
            if (_playerPLID == 0)
                _playerPLID = e.Car.PLID;

            if (viewPlid != 0)
                _lastKnownPlayerPLID = viewPlid;
            else if (_lastKnownPlayerPLID == 0)
                return;

            // Filter BEFORE any BeginInvoke — MCI includes every car, not just ours;
            // dispatching per foreign car wasted a UI-thread hop on full servers.
            if (e.Car.PLID != _lastKnownPlayerPLID)
                return;

            double speed = e.Car.SpeedKmh;

            // Whole body on the UI thread. _drift.Update()/_indicators.Update() used to
            // run unmarshaled here while also being mutated from the UI thread elsewhere
            // (HandleObjectHit, Reset Score, OnRevData) — an unsynchronized race on
            // DriftEngine's score fields. Same single-BeginInvoke pattern as OnRevData.
            this.BeginInvoke((Action)(() =>
            {
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
                _overlay.UpdateSpeedGauge(_speedKmh);   // ← NOWE: zasila prędkościomierz w overlay
                // Burnout ma pokazywać ten sam aktywny pasek HUD (etykieta/run/combo) co drift
                // i speeding — bez tego overlay zostawał w stanie "idle" mimo że DriftScored
                // faktycznie strzelał z tekstami burnoutu (patrz UpdateLabel niżej).
                _overlay.SetActive(_isDrifting || _isSpeeding || _isBurnout);
                _overlay.UpdateLapScore(_drift.LapScore);

                if (!_isDrifting && !_isSpeeding && !_isBurnout)
                    _overlay.UpdateAccentColor(OverlayColor1);
                _speedLabel.Text = _useMph
                    ? ((int)(speed * 0.621371)).ToString()
                    : ((int)speed).ToString();
                _angleValueLabel.Text = ((int)_driftAngle).ToString() + "°";
                _angleValueLabel.ForeColor = _isDrifting
                    ? Color.FromArgb(255, 80, 80) : Color.FromArgb(60, 180, 255);

                _speedometer.Speed = speed;
                _speedometer.DriftAngle = _driftAngle;
                _speedometer.IsDrifting = _isDrifting;
                _speedometer.Invalidate();

                // Odświeżanie HUD-a IS_BTN na KAŻDYM ticku telemetrii, nie tylko przy zdarzeniu
                // DriftScored (które strzela wyłącznie podczas aktywnego driftu/przyspieszenia).
                // Bez tego licznik wyniku znikał przez większość czasu i pojawiał się tylko
                // sporadycznie na chwilę — patrz komentarz w InGameHudManager.UpdateInGameHUD().
                if (_showHudCheck.Checked && _insim.IsConnected && _insim.IsRaceNow)
                    _hud.UpdateInGameHUD();

                // Catches bonuses whose award frame skips DriftScored (e.g. burnout 360-spin
                // bonus when conditionNow flips false the same frame it's granted) — without
                // this, points were credited but the popup never showed. Runs every tick,
                // same dedup field as OnDriftScored.
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
                // NOTE: wcześniej gałąź "else" wołała _hud.ClearAllButtons() — DriftScored strzela
                // wielokrotnie na sekundę WYŁĄCZNIE podczas aktywnego driftu/przyspieszenia, więc
                // każde chwilowe niespełnienie warunku (np. _insim.IsRaceNow) podczas driftu
                // twardo czyściło cały HUD. Odświeżanie z OnCarData w tej samej sytuacji tylko
                // pomija aktualizację, nie kasuje niczego — ujednolicone tutaj, żeby HUD nie znikał
                // w trakcie driftu. Czyszczeniem przy rozłączeniu/wyjściu z wyścigu zajmują się już
                // RaceStateChanged / OnDisconnected / przełącznik "Show ingame HUD".
                if (_showHudCheck.Checked && _insim.IsConnected && _insim.IsRaceNow)
                    _hud.UpdateInGameHUD();

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
                    DriftLabelKind.BurnoutGood => InSimColor2,
                    DriftLabelKind.BurnoutHigh => InSimColor3,
                    DriftLabelKind.BurnoutExtreme => InSimColor4,
                    DriftLabelKind.BurnoutInsane => InSimColor5,
                    _ => InSimColor1,
                };

                Color overlayAccent = _driftLabelKind switch
                {
                    DriftLabelKind.AngleHigh => OverlayColor3,
                    DriftLabelKind.AngleExtreme => OverlayColor4,
                    DriftLabelKind.AngleBackward => OverlayColor3,
                    DriftLabelKind.AngleUltraExtreme => OverlayColor5,
                    DriftLabelKind.AngleHighE => OverlayColor3,
                    DriftLabelKind.AngleExtremeE => OverlayColor4,
                    DriftLabelKind.AngleBackwardE => OverlayColor3,
                    DriftLabelKind.AngleUltraExtremeE => OverlayColor5,
                    DriftLabelKind.Fast2 => OverlayColor3,
                    DriftLabelKind.Fast3 => OverlayColor4,
                    DriftLabelKind.AngleGood => OverlayColor2,
                    DriftLabelKind.AngleGoodE => OverlayColor2,
                    DriftLabelKind.Fast1 => OverlayColor2,
                    DriftLabelKind.BurnoutGood => OverlayColor2,
                    DriftLabelKind.BurnoutHigh => OverlayColor3,
                    DriftLabelKind.BurnoutExtreme => OverlayColor4,
                    DriftLabelKind.BurnoutInsane => OverlayColor5,
                    _ => OverlayColor1,
                };

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

        // Wspólny debounce dla WSZYSTKICH obiektów (wcześniej osobno dla postów i stosów opon) —
        // jeden hit na obiekt niezależnie od jego typu, żeby ten sam kolizja z tego samego obiektu
        // nie odpaliła dwóch bonusów/kar w jednej klatce fizyki.
        private DateTime _lastObjectHitTime = DateTime.MinValue;
        private static readonly TimeSpan ObjectHitCooldown = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Ujednolicona obsługa kolizji z DOWOLNYM wykrywalnym obiektem toru/layoutu (pachołek,
        /// stos opon, słupek, bariera, banner itd.) — dokładnie ten sam wzorzec, który wcześniej
        /// działał tylko dla "Tyre Stack Big":
        ///   • poza driftem  → natychmiastowa kara
        ///   • w trakcie driftu → 1 sekunda zwłoki, potem sprawdzenie czy drift/prędkość/kąt nie
        ///     zostały istotnie naruszone (KISS = lekkie muśnięcie) czy jednak przerwane (HIT)
        /// Nazwa obiektu (np. "CONE", "POST", "ARMCO BARRIER") wchodzi bezpośrednio w tekst bonusu.
        /// </summary>
        // LFS może wysłać IS_OBH kilka razy dla JEDNEGO fizycznego muśnięcia (wejście/wyjście
        // kontaktu) — bez tej blokady każde z nich odpalało własny, niezależny 1-sekundowy
        // timer i podwajało (albo więcej) bonus/karę za to samo zdarzenie.
        private bool _objectHitCheckPending = false;

        private void HandleObjectHit(string objectName)
        {
            var buforspeed = _speedKmh;
            var buforangle = _driftAngle;
            var now = DateTime.UtcNow;
            if (now - _lastObjectHitTime < ObjectHitCooldown) return;   // debounce — 1 efekt na uderzenie
            _lastObjectHitTime = now;

            if (_drift.IsSpeeding)
            {
                if (_objectHitCheckPending) return;   // poprzednie muśnięcie wciąż czeka na rozstrzygnięcie
                _objectHitCheckPending = true;

                // szybka jazda (bez driftu) — czekamy 1s; jeśli prędkość nie spadła o >=10 km/h,
                // uderzenie nie przerwało jazdy, więc zamiast kary dostajesz bonus "Niepowstrzymany"
                var speedingDelayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                speedingDelayTimer.Tick += (s, e) =>
                {
                    speedingDelayTimer.Stop();
                    speedingDelayTimer.Dispose();
                    _objectHitCheckPending = false;

                    if (buforspeed - _speedKmh < 10)
                        _drift.AwardUnstoppableBonus();
                    else
                    {
                        _drift.ApplyPostPoints(-100, $"{objectName} HIT -100");
                        _drift.RegisterCollisionPenalty();
                    }

                    PushObjectHitOverlayState();
                };
                speedingDelayTimer.Start();
                return;
            }

            if (!_drift.IsDrifting)
            {
                // uderzenie poza driftem — natychmiastowa kara
                _drift.ApplyPostPoints(-100, $"{objectName} HIT -100");
                _drift.RegisterCollisionPenalty();
                PushObjectHitOverlayState();
                return;
            }

            if (_objectHitCheckPending) return;   // poprzednie muśnięcie wciąż czeka na rozstrzygnięcie
            _objectHitCheckPending = true;

            // uderzenie podczas driftu — czekamy 1s i sprawdzamy, czy drift nadal trwa
            // w podobnym stanie (ciągłość kąta/prędkości), czy został naruszony
            var delayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            delayTimer.Tick += (s, e) =>
            {
                _objectHitCheckPending = false;
                double anglegap = 0;
                if (buforangle > _driftAngle) { anglegap = buforangle - _driftAngle; }
                if (buforangle < _driftAngle) { anglegap = _driftAngle - buforangle; }
                delayTimer.Stop();
                delayTimer.Dispose();
                if (_speedKmh >= 15 || _speedKmh >= buforspeed / 1.5 || anglegap <= 25)
                {
                    if (_drift.IsDrifting)
                    {
                        long bonus = (long)Math.Round(250 * _drift.ComboMultiplier);
                        _drift.ApplyPostPoints(bonus, $"{objectName} KISS! +{bonus}");
                    }
                    else
                    {
                        _drift.ApplyPostPoints(-250, $"{objectName} HIT -250");
                        _drift.RegisterCollisionPenalty();
                    }
                }
                else
                {
                    _drift.ApplyPostPoints(-250, $"{objectName} HIT -250");
                    _drift.RegisterCollisionPenalty();
                }

                PushObjectHitOverlayState();
            };
            delayTimer.Start();
        }

        // Push PEŁNEGO stanu, nie tylko TotalScore — ApplyPostPoints (patrz DriftEngine) zmienia
        // też CurrentRunPoints/LapScore/ComboMultiplier, więc HUD ma się odświeżyć od razu, a nie
        // dopiero przy najbliższej naturalnej klatce driftu (bez tego LapScore na overlayu
        // potrafił chwilowo wyprzedzić "RUN"/combo). Współdzielone przez wszystkie gałęzie HandleObjectHit.
        private void PushObjectHitOverlayState()
        {
            UpdateScoreLabels();
            _overlay.UpdateScore(_drift.TotalScore);
            _overlay.UpdateRun(_drift.CurrentRunPoints);
            _overlay.UpdateLapScore(_drift.LapScore);
            _overlay.UpdateCombo(_drift.ComboMultiplier);

            // Dedup against _lastOverlayBonusText — same guard OnCarData's per-tick catch-up
            // check uses. Without it, this call shows the bonus once here and the catch-up
            // check (which doesn't know this already happened) shows it again next tick.
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
                    // Flash award text in game
                    _hud.ShowInGameAward(_lastAward);
                    _hud.StartAwardFlash();
                }
                else
                {
                    _hud.ClearAllButtons();
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
        // Chroni przed nałożeniem się dźwięku "tyknięcia" z lampki (OnIndicatorLampStateChanged)
        // na dźwięk "cancel" wywołany chwilę wcześniej przez wyłączenie kierunkowskazu — bo LFS
        // gasi lampkę niemal natychmiast po anulowaniu, co bez tej blokady odpaliłoby dodatkowo
        // indicator_click_off.wav tuż obok indicator_cancel.wav.
        private DateTime _lastIndicatorCancelTime = DateTime.MinValue;
        private const int IndicatorCancelSuppressMs = 250;

        // ── Diagnostyka OutGauge widoczna w panelu Kierunkowskazów ──────────────
        // Licznik pakietów rosnący na oczach użytkownika + surowa wartość ShowLights
        // pozwalają jednoznacznie odróżnić "OutGauge w ogóle nie dociera / źle
        // skonfigurowany" od "dociera, ale zły bit sygnalizacji" od "wszystko działa,
        // tylko brakuje plików .wav" — bez zgadywania.
        private long _outGaugePacketCount = 0;
        private uint _lastShowLightsRaw = 0;
        private DateTime _lastIndicatorDiagUpdate = DateTime.MinValue;

        private void UpdateIndicatorDiagnosticsLabel()
        {
            if (_indicatorStatusLabel == null) return;

            string stateLine = $"{Localization.T("indicators.currentstatus")} {_indicators.CurrentState}";

            /*
            string outGaugeLine = _outGaugePacketCount > 0
                ? $"OutGauge - packets: {_outGaugePacketCount:N0}"
                : "OutGauge: BRAK DANYCH";
            */
           

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
                // Cała logika kierunkowskazów (wysyłka klawisza do LFS, dźwięki, UI) ma sens
                // tylko gdy program jest faktycznie połączony z grą przez InSim. Bez połączenia
                // SendKeyToLFS() i tak nic by nie zmieniło w bieżącej sesji, a odpalone dźwięki
                // czy zmiana etykiety wprowadzałyby w błąd, że kierunkowskaz realnie zadziałał.
                // Blokada tutaj obejmuje zarówno bindowanie klawiaturowe (obsługiwane wewnątrz
                // IndicatorManager), jak i kierownicowe (patrz ApplyIndicatorBinding).
                if (!_insim.IsConnected)
                    return;

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

                // StateChanged odpala się TYLKO przy faktycznej zmianie CurrentState (patrz
                // IndicatorManager.SetState), więc newState == Off oznacza, że kierunkowskaz
                // został właśnie wyłączony — czy to ręcznie (przycisk/klawisz/kierownica),
                // czy automatycznie przez centrowanie kierownicy (patrz auto-cancel w handlerze
                // SteeringChanged). Obie te sytuacje mają odtworzyć indicator_cancel.wav.
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
                    catch { }
                }

                // Zaktualizuj label statusu (stan + diagnostyka OutGauge)
                UpdateIndicatorDiagnosticsLabel();
            }));
        }

        /// <summary>
        /// Odtwarza "tyknięcia" kierunkowskazu w rytm REALNEGO mrugania kontrolki na desce
        /// rozdzielczej LFS (OutGauge ShowLights), a nie naszej wewnętrznej intencji przełącznika —
        /// dzięki temu tykanie zawsze jest zsynchronizowane z tym, co faktycznie widać w grze.
        ///
        ///  • kontrolka się zapala → przerwij cokolwiek gra, odtwórz indicator_click_on.wav
        ///  • kontrolka gaśnie     → przerwij cokolwiek gra, odtwórz indicator_click_off.wav
        ///
        /// Samo wyłączenie kierunkowskazu (przycisk / auto-cancel od centrowania kierownicy)
        /// obsługuje osobno OnIndicatorStateChanged, które odtwarza indicator_cancel.wav —
        /// tutaj jest krótkie okno wygaszające (IndicatorCancelSuppressMs), żeby lampka gasnąca
        /// tuż po cancelu nie dograła jeszcze dodatkowo indicator_click_off.wav.
        /// </summary>
        private void OnIndicatorLampStateChanged(bool isOn)
        {
            this.BeginInvoke((Action)(() =>
            {
                if (!_insim.IsConnected) return;
                if (!_indicatorSoundsCheck.Checked) return;

                if ((DateTime.UtcNow - _lastIndicatorCancelTime).TotalMilliseconds < IndicatorCancelSuppressMs)
                    return;   // cancel dopiero co obsłużył ten dźwięk — pomijamy tyknięcie

                try
                {
                    ApplyIndicatorVolume();

                    // niezależnie od kierunku przejścia — najpierw twardo przerwij wszystko,
                    // co aktualnie gra, żeby nowy dźwięk zawsze startował "na czysto"
                    _indicatorClickOn.Stop();
                    _indicatorClickOff.Stop();

                    if (isOn)
                        _indicatorClickOn.Play();
                    else
                        _indicatorClickOff.Play();
                }
                catch (Exception ex)
                {
                    // widoczne w statusie zamiast cichego "nic nie słychać" — najczęstsza
                    // przyczyna to brakujący plik .wav pod oczekiwaną nazwą w folderze Sounds
                    _statusLabel.Text = "Błąd dźwięku kierunkowskazu: " + ex.Message;
                }

                // Diagnostyka: pokazuje realny stan kontrolek z OutGauge niezależnie od tego,
                // czy odtworzenie dźwięku się powiodło — jeśli to się NIGDY nie zmienia mimo
                // migającego kierunkowskazu w grze, oznacza to że zdarzenie z OutGauge w ogóle
                // nie dociera (zły bit ShowLights / OutGauge nieskonfigurowany w LFS), a nie że
                // brakuje plików dźwiękowych.
                UpdateIndicatorDiagnosticsLabel();
            }));
        }

        private void OnConnected(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(true);
                _connectionSwitch.Enabled = true;   // odblokuj po udanej próbie (patrz CheckedChanged)
                _resetBtn.Enabled = true;
                _revLimiter.Start();
                _connectionStateLabel.Text = "⬤  Connected with LFS";
                _connectionStateLabel.ForeColor = Color.FromArgb(60, 220, 100);

                // KLUCZOWE: LFS czyści wszystkie wyświetlane przyciski IS_BTN przy każdym
                // rozłączeniu InSim (celowym albo przypadkowym, np. restart gry/utrata sieci) —
                // ale nasz lokalny cache w InGameHudManager o tym nie wie i po cichu pomijałby
                // wysyłkę przycisków z tekstem identycznym jak przed rozłączeniem, mimo że
                // fizycznie już nie istnieją w grze. Stąd "po reconnect widać tylko idle score
                // i nic więcej się nie dzieje" — reset cache musi nastąpić przy KAŻDYM connect,
                // nie tylko przy jawnym DeleteAllButtons().
                _hud.InvalidateCache();

                if (_showHudCheck.Checked)
                    _hud.InitInGameHUD();


                if (_showOverlayCheck.Checked)
                {
                    IntPtr lfsHwnd = FindLfsWindow();
                    if (lfsHwnd != IntPtr.Zero)
                    {
                        _overlay.UpdateScore(_drift.TotalScore);
                        _overlay.UpdateAccentColor(OverlayColor1);
                        _overlay.UpdateMaxRpm(CalibratedMAXRPM);   // ← NOWE: startowa kalibracja obrotomierza
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
                _overlay.ResetOutGaugeData();
                _outGaugeFreshness.Reset();
                _drift.SetOutGaugeDataFresh(false);
                _hud.InvalidateCache();
            }));

            _tireTemperatureLimiter.Disconnect();
        }

        /// <summary>
        /// Próba połączenia (InSimConnection.Connect) się nie powiodła — w
        /// odróżnieniu od OnDisconnected (utrata JUŻ nawiązanego połączenia).
        /// Przełącznik NIE może zostać widocznie "włączony" mimo braku realnego
        /// połączenia z grą, więc cofamy go tutaj do OFF. Treść błędu trafia do
        /// _statusLabel już przez OnStatus (StatusChanged) — nie duplikujemy jej tu.
        /// </summary>
        private void OnConnectFailed(object sender, string message)
        {
            BeginInvoke((Action)(() =>
            {
                _connectionSwitch.SetCheckedSilent(false);
                _connectionSwitch.Enabled = true;
                _connectionStateLabel.Text = "⬤  Rozłączono";
                _connectionStateLabel.ForeColor = Color.FromArgb(130, 130, 165);
            }));
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
            // NOTE: oryginalnie było tu też przypisanie na podstawie IsDrifting,
            // ale było od razu nadpisywane poniższym — usunięte jako martwy kod.
            _runValueLabel.Text = _drift.IsSpeeding
                ? _drift.CurrentRunPoints.ToString("N0") : "—";
            _comboValueLabel.Text = $"x{_drift.ComboMultiplier}";
            _comboValueLabel.ForeColor = Color.FromArgb(255, 60, 60);

            UpdateBestStatsLabels();
        }

        private void UpdateBestStatsLabels()
        {
            if (_bestRunValueLabel == null) return;

            _bestRunValueLabel.Text = _drift.BestRunScore.ToString("N0");
            _bestDriftValueLabel.Text = $"{_drift.BestDriftDurationMs / 1000.0:F1}s";
            _bestDeepDriftValueLabel.Text = $"{_drift.BestDeepDriftDurationMs / 1000.0:F1}s";
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

        private Color GetOverlayColorSlot(int slot) => slot switch
        {
            1 => OverlayColor1,
            2 => OverlayColor2,
            3 => OverlayColor3,
            4 => OverlayColor4,
            5 => OverlayColor5,
            _ => OverlayColor1
        };

        private void SetOverlayColorSlot(int slot, Color c)
        {
            switch (slot)
            {
                case 1: OverlayColor1 = c; break;
                case 2: OverlayColor2 = c; break;
                case 3: OverlayColor3 = c; break;
                case 4: OverlayColor4 = c; break;
                case 5: OverlayColor5 = c; break;
            }
        }

        // InSim paleta ustawia kod ^X ORAZ synchronizuje OverlayColorX tym samym kolorem
        private void SetInSimColorSlot(int slot, string code)
        {
            Color c = InSimCodeToColor(code);
            switch (slot)
            {
                case 1: InSimColor1 = code; OverlayColor1 = c; break;
                case 2: InSimColor2 = code; OverlayColor2 = c; break;
                case 3: InSimColor3 = code; OverlayColor3 = c; break;
                case 4: InSimColor4 = code; OverlayColor4 = c; break;
                case 5: InSimColor5 = code; OverlayColor5 = c; break;
            }
            SyncHudColors();
        }

        // Przekazuje aktualną paletę InSim do menedżera HUD-u w grze (IS_BTN)
        private void SyncHudColors()
        {
            if (_hud == null) return;
            _hud.InSimColor1 = InSimColor1;
            _hud.InSimColor2 = InSimColor2;
            _hud.InSimColor3 = InSimColor3;
            _hud.InSimColor4 = InSimColor4;
            _hud.InSimColor5 = InSimColor5;
        }

        // Podgląd na przyciskach: gdy tryb custom (overlay ON, IS_BTN HUD OFF) — pokaż OverlayColorX,
        // w każdym innym wypadku — pokaż kolor z InSim palety
        private void RefreshColorButtonSwatches()
        {
            bool useOverlayColors = _showOverlayCheck != null && _showOverlayCheck.Checked
                                  && (_showHudCheck == null || !_showHudCheck.Checked);

            if (_colorBtn1 == null) return;

            SetButtonColor(_colorBtn1, useOverlayColors ? OverlayColor1 : InSimCodeToColor(InSimColor1));
            SetButtonColor(_colorBtn2, useOverlayColors ? OverlayColor2 : InSimCodeToColor(InSimColor2));
            SetButtonColor(_colorBtn3, useOverlayColors ? OverlayColor3 : InSimCodeToColor(InSimColor3));
            SetButtonColor(_colorBtn4, useOverlayColors ? OverlayColor4 : InSimCodeToColor(InSimColor4));
            SetButtonColor(_colorBtn5, useOverlayColors ? OverlayColor5 : InSimCodeToColor(InSimColor5));
        }

        private void OpenCustomColorPicker(Button targetBtn, Color initialColor, string title, Action<Color> onApply, bool includeAlpha = false)
        {
            Color initial = initialColor;

            Form overlayBg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                Bounds = this.Bounds,
                BackColor = Color.Black,
                Opacity = 0.5,
                Owner = this
            };

            int popupHeight = includeAlpha ? 320 : 275;

            Form popup = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                Size = new Size(300, popupHeight),
                BackColor = ApplePalette.Background
            };

            popup.Shown += (s, e) => popup.Region = CreateSmoothRoundedRegion(popup.Width, popup.Height, 20);

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

            MakeLabel(card, title, 20, 15, 240, 24,
                ApplePalette.Title, new Font("Segoe UI Semibold", 11f));

            // ── podgląd koloru (styl jak przyciski palety) — szachownica pod spodem,
            //    żeby przezroczystość była faktycznie widoczna, nie tylko "domyślne tło" ──
            var preview = new Panel
            {
                Location = new Point(20, 55),
                Size = new Size(56, 56),
                BackColor = Color.Transparent
            };
            preview.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundedPath(new Rectangle(0, 0, preview.Width - 1, preview.Height - 1), 12);

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

            MacSlider sliderR = null, sliderG = null, sliderB = null, sliderA = null;
            Label valR = null, valG = null, valB = null, valA = null;

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
                int a = includeAlpha ? sliderA.Value : 255;
                Color c = Color.FromArgb(a, sliderR.Value, sliderG.Value, sliderB.Value);
                preview.Tag = c;
                preview.Invalidate();
                valR.Text = sliderR.Value.ToString();
                valG.Text = sliderG.Value.ToString();
                valB.Text = sliderB.Value.ToString();
                if (includeAlpha) valA.Text = sliderA.Value.ToString();

                SetButtonColor(targetBtn, c);
                onApply(c);
            }

            sliderR.ValueChanged += (s, e) => ApplyLive();
            sliderG.ValueChanged += (s, e) => ApplyLive();
            sliderB.ValueChanged += (s, e) => ApplyLive();
            if (includeAlpha) sliderA.ValueChanged += (s, e) => ApplyLive();

            // ── NOWE: rząd 5 przycisków z domyślnymi kolorami ──
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

                var chosen = dc; // capture
                swatch.Click += (s, e) =>
                {
                    // presety zmieniają tylko RGB — jeśli jest suwak przezroczystości,
                    // celowo NIE dotykamy go, żeby nie zgubić ustawionej transparencji
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

            var applyBtn = MakeButton(card, "OK", 20, includeAlpha ? 252 : 218, 260, 36, ApplePalette.Blue);
            applyBtn.Click += (s, e) => { SaveSettings(); popup.Close(); };

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

            overlayBg.Show();
            popup.Owner = overlayBg;

            try { popup.ShowDialog(overlayBg); }
            finally { overlayBg.Close(); overlayBg.Dispose(); }
        }

        private void HandleColorButtonClick(Button btn, int slot)
        {
            bool overlayOn = _showOverlayCheck != null && _showOverlayCheck.Checked;
            bool insimHudOn = _showHudCheck != null && _showHudCheck.Checked;

            // custom RGB dostępny TYLKO gdy overlay ON i IS_BTN HUD OFF (IS_BTN i tak
            // wspiera wyłącznie 10 stałych kolorów, więc custom RGB jest blokowany)
            if (overlayOn && !insimHudOn)
            {
                OpenCustomColorPicker(btn, GetOverlayColorSlot(slot), "Custom overlay color", c =>
                {
                    SetOverlayColorSlot(slot, c);
                    if (!_isDrifting && !_isSpeeding)
                        _overlay.UpdateAccentColor(OverlayColor1);
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
            _drift.FlushStats();   // bypass save throttle so the last few seconds aren't lost
            _outGaugeFreshnessPoll.Stop();
            _outGaugeFreshnessPoll.Dispose();
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

            Rectangle trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
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
        public Color FillColor { get; set; } = Color.FromArgb(0, 122, 255); // Apple blue
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

            // pełne tło paska
            using (GraphicsPath trackPath = RoundedPath(trackRect, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            int knobX = ValueToX(_value);

            // wypełnienie od lewej do kropki
            int fillWidth = Math.Max(0, knobX - knobRadius);
            if (fillWidth > 0)
            {
                Rectangle fillRect = new Rectangle(knobRadius, trackY, fillWidth, trackHeight);
                using (GraphicsPath fillPath = RoundedPath(fillRect, radius))
                using (var gradient = new LinearGradientBrush(
                           new Rectangle(knobRadius, trackY, Math.Max(fillWidth, 1), trackHeight),
                           Lighten(FillColor, 0.10),
                           FillColor,
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(gradient, fillPath);
                }
            }

            // cień pod kropką
            Rectangle knobRect = new Rectangle(knobX - knobRadius, (Height - knobDiameter) / 2, knobDiameter, knobDiameter);
            Rectangle shadowRect = knobRect;
            shadowRect.Offset(0, 1);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowRect);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillPath(shadowBrush, shadowPath);
            }

            // kropka
            Color knob = _isDragging ? Darken(KnobColor, 0.05)
                       : _isHoverKnob ? Lighten(KnobColor, 0.0)
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

        private static Color Lighten(Color c, double amount)
        {
            return Color.FromArgb(
                c.A,
                Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
                Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
                Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
        }

        private static Color Darken(Color c, double amount)
        {
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();

            if (radius <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                if (rect.Width > 0 && rect.Height > 0)
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

    /// <summary>Ustawienia rev limitera dla jednego konkretnego pojazdu.</summary>
    public class VehicleRevSettings
    {
        public int MaxRpm { get; set; }
        public int CutMs { get; set; }
    }

    public class AppSettings
    {
        public int CalibratedMAXRPM { get; set; } = 7600;

        public int SavedMSCUT { get; set; } = 40;

        // Ustawienia rev limitera zapamiętane osobno dla każdego auta (klucz = krótki kod
        // auta z OutGauge, np. "XFG", "FXO") — patrz MainForm.LoadVehicleRevSettings/
        // SaveVehicleRevSettings. CalibratedMAXRPM/SavedMSCUT powyżej pozostają jako
        // ostatnio używane wartości "globalne" (np. zanim OutGauge w ogóle przyśle nazwę auta).
        public Dictionary<string, VehicleRevSettings> VehicleRevLimiterSettings { get; set; } = new();

        public string Language { get; set; } = "English";

        public bool DarkTheme { get; set; } = true;

        public string InSimColor1 { get; set; } = "^7";
        public string InSimColor2 { get; set; } = "^6";
        public string InSimColor3 { get; set; } = "^3";
        public string InSimColor4 { get; set; } = "^5";
        public string InSimColor5 { get; set; } = "^5";

        public int OverlayColor1 { get; set; } = -1;   // -1 = brak zapisu, użyj domyślnego
        public int OverlayColor2 { get; set; } = -1;
        public int OverlayColor3 { get; set; } = -1;
        public int OverlayColor4 { get; set; } = -1;
        public int OverlayColor5 { get; set; } = -1;

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

        public bool IndicatorSoundsEnabled { get; set; } = true;
        public int IndicatorSoundsVolume { get; set; } = 100;
        public bool IndicatorAutoCancelOnCenter { get; set; } = true;

        // ── Speedometer + Tachometer HUD (overlay, styl Forza) ──────────────────
        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public int SpeedoTachoRedlineColor { get; set; } = Color.Red.ToArgb();
        public int SpeedoTachoTextColor { get; set; } = Color.White.ToArgb();
        public int SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230).ToArgb();
        public int SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218).ToArgb();
        public int SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20).ToArgb();
        public bool SpeedoTachoUseMph { get; set; } = false;



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