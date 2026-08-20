using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LFSDriftBuddy
{

    public class DriverStats
    {
        public long TotalScore { get; set; } = 0;
        public long BestRunScore { get; set; } = 0;
        public double BestDriftDurationMs { get; set; } = 0;
        public double BestDeepDriftDurationMs { get; set; } = 0;
    }


    /// InSim Direction/Heading: word, 0 = world Y axis, 32768 = 180 degrees.
    /// Drift angle = angular difference between motion direction and car heading.

    public class DriftEngine
    {
        // ── Drift scoring ───────────────────────────────────────
        private const double MIN_SPEED_KMH = 20.0;
        private const double MIN_DRIFT_ANGLE_DEG = 20.0;
        private const double MAX_DRIFT_ANGLE_DEG = 130.0;
        public const double COMBO_TIMEOUT_SEC = 3.0;
        private const double BONUS_TIMEOUT_SEC = 2.5;
        private const double POINTS_PER_SECOND = 400.0;

        // ── Fast drive scoring ──────────────────────────────────
        private const double FAST_DRIVE_THRESHOLD = 110.0;
        private const double FAST_DRIVE_POINTS = 8.0;



        // ── State ───────────────────────────────────────────────
        public bool IsDrifting { get; private set; }
        public bool IsSpeeding { get; private set; }
        public double DriftAngleDeg { get; private set; }
        public bool DriftSideRight { get; private set; }
        public bool _isHandBrakeON { get; private set; } = false;
        public void SetHandbrakeActive(bool active) => _isHandBrakeON = active;
        public long CurrentRunPoints { get; private set; }
        public long LapScore { get; private set; }       // ← NOWE
        public long BestLapScore { get; private set; }    // ← NOWE
        public long LastLapScore { get; private set; }   // ← NOWE

        private readonly string _lastLapRecordsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "driver_lastlap_records.json");

        private Dictionary<string, Dictionary<string, Dictionary<string, long>>> _driverLastLapRecords = new();
        public long TotalScore { get; private set; }
        public double ComboMultiplier { get; private set; } = 1;
        public long BestRunScore { get; private set; }
        public double BestDriftDurationMs { get; private set; }
        public double BestDeepDriftDurationMs { get; private set; }
        public double SpeedKmh { get; private set; }
        public string LastAwardedText { get; private set; } = "";

        // ── Driver ──────────────────────────────────────────────
        public string CurrentDriver { get; private set; } = "default_driver";

        private readonly string _statsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "driver_stats.json");

        private Dictionary<string, DriverStats> _driverScores = new();
        private readonly string _lapRecordsFile = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "driver_lap_records.json");

        // driver → track → layout → bestLapScore
        private Dictionary<string, Dictionary<string, Dictionary<string, long>>> _driverLapRecords = new();
        private string _currentTrackCode = "";
        private string _currentLayoutName = "";
        private string _currentTrackKey = "";

        public bool HasActiveLapContext { get; private set; } = false;   // ← NOWE: false dopóki nie wykryto 1. okrążenia

        private DateTime _driftStartTime;
        private DateTime _lastUpdateTime = DateTime.UtcNow;
        private DateTime _lastBonusTime = DateTime.MinValue;
        private DateTime _lastDriftTime = DateTime.MinValue;
        private DateTime _lastSpeedingTime = DateTime.MinValue;
        private DateTime _driftSessionStart = DateTime.MinValue;
        private DateTime? _deepDriftSessionStart = null;
        private double _dtSec = 0;
        private bool _previouslyDrifting = false;
        private bool _previouslyDriftSideRight = true;

        // ── Kąt entry (wejścia w pierwszy zakręt sekwencji driftu) ─────────────
        // Oceniany DOKŁADNIE w momencie T+1,5s od rozpoczęcia sekwencji driftu (nie
        // wcześniej, nie jako średnia) — patrz EvaluateDriftEntry(). Jeśli drift skończy
        // się przed upływem 1,5s, ocena nigdy nie następuje (żaden bonus nie jest przyznany).
        private const double DRIFT_ENTRY_EVAL_SEC = 1.8;
        private const double DRIFT_ENTRY_GOOD_DEG = 55.0;
        private const double DRIFT_ENTRY_HIGH_DEG = 65.0;
        private const double DRIFT_ENTRY_EXTREME_DEG = 75.0;
        private const double DRIFT_ENTRY_ULTRAEXTREME_DEG = 85.0;
        private const double DRIFT_ENTRY_BACKWARD_DEG = 95.0;

        private DateTime _driftEntryStartTime = DateTime.MinValue;
        private bool _driftEntryEvaluated = false;


        // ── Events ──────────────────────────────────────────────

        public event Action<long, double, string, string, string, DriftLabelKind>? DriftScored;
        public event Action? DriftStarted;
        public event Action<long>? DriftEnded;
        public event Action<long, long>? LapCompleted;

        public DriftEngine()
        {
            LoadStats();
            LoadLapRecords();
            LoadLastLapRecords();
        }


        // ────────────────────────────────────────────────────────
        //  Driver handling
        // ────────────────────────────────────────────────────────
        public void SetDriver(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                driverName = "default_driver";

            CurrentDriver = driverName;

            if (!_driverScores.ContainsKey(CurrentDriver))
                _driverScores[CurrentDriver] = new DriverStats();

            var stats = _driverScores[CurrentDriver];
            TotalScore = stats.TotalScore;
            BestRunScore = stats.BestRunScore;
            BestDriftDurationMs = stats.BestDriftDurationMs;
            BestDeepDriftDurationMs = stats.BestDeepDriftDurationMs;

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);   // ← poprawione
            LastLapScore = GetLastLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
        }
        private static string CombineTrackKey(string track, string layout)
        {
            if (string.IsNullOrWhiteSpace(track)) track = "unknown";
            return string.IsNullOrWhiteSpace(layout) ? track : $"{track}_{layout}";
        }

        public void SetTrack(string trackCode)
        {
            _currentTrackCode = string.IsNullOrWhiteSpace(trackCode) ? "unknown" : trackCode;
            ApplyTrackKey();
        }

        public void SetLayout(string layoutName)
        {
            _currentLayoutName = layoutName ?? "";
            ApplyTrackKey();
        }

        private void ApplyTrackKey()
        {
            string newKey = CombineTrackKey(_currentTrackCode, _currentLayoutName);
            if (_currentTrackKey == newKey) return;

            _currentTrackKey = newKey;

            MigrateDefaultLapRecordIfNeeded();

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LastLapScore = GetLastLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LapScore = 0;
            HasActiveLapContext = false;
        }


        private void MigrateDefaultLapRecordIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_currentLayoutName)) return;
            if (!_driverLapRecords.TryGetValue(CurrentDriver, out var trackMap)) return;
            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap)) return;
            if (!layoutMap.TryGetValue("default", out var defaultScore)) return;
            if (layoutMap.ContainsKey(_currentLayoutName)) return;

            layoutMap[_currentLayoutName] = defaultScore;
            layoutMap.Remove("default");
            SaveLapRecordsRaw();
        }

        public void OnLapCompleted()
        {
            LastLapScore = LapScore;    // ← NOWE
            SaveLastLapRecord();
            if (!string.IsNullOrEmpty(_currentTrackKey) && LapScore > BestLapScore)
            {
                BestLapScore = LapScore;
                SaveLapRecords();
            }

            LapCompleted?.Invoke(LapScore, BestLapScore);
            LapScore = 0;
            HasActiveLapContext = true;
        }

        // ────────────────────────────────────────────────────────
        //  Update
        // ────────────────────────────────────────────────────────

        int driftTimeMultipler = 0;
        double driftTotalTime= 0;
        int deepAngleCount = 0;
        int eBrakeCount = 0;
        bool eDriftActive = false;
        public void Update(InSim.CompCar car)
        {
            var now = DateTime.UtcNow;

            _dtSec = (now - _lastUpdateTime).TotalSeconds;
            if (_dtSec > 0.5)
                _dtSec = 0.5;

            _lastUpdateTime = now;

            SpeedKmh = car.SpeedKmh;

            double motionDeg = car.Direction / 65536.0 * 360.0;
            double headingDeg = car.Heading / 65536.0 * 360.0;

            double diff = headingDeg - motionDeg;

            while (diff > 180) diff -= 360;
            while (diff < -180) diff += 360;

            DriftSideRight = diff >= 0 ? true : false;
            DriftAngleDeg = Math.Abs(diff);

            bool speeding = SpeedKmh >= FAST_DRIVE_THRESHOLD;

            bool drifting = SpeedKmh >= MIN_SPEED_KMH
                         && DriftAngleDeg >= MIN_DRIFT_ANGLE_DEG
                         && DriftAngleDeg <= MAX_DRIFT_ANGLE_DEG;

            UpdateBurnout(now, drifting, speeding, headingDeg);

            // ── FAST DRIVE SCORING ─────────────────────────────

            if (_isHandBrakeON && SpeedKmh >= 35)
            {
                eBrakeCount += 2;
                eDriftActive = true;
            }
            if (SpeedKmh <= 25)
            {
                eBrakeCount = 0;
                eDriftActive = false;
            }

            if (!speeding && !drifting)
            {
                IsSpeeding = false;
                LastAwardedText = $"";
                //IsDrifting = false;
                //_driftStartTime = now;
                double gap = (now - _lastDriftTime).TotalSeconds;
                double gap2 = (now - _lastSpeedingTime).TotalSeconds;
                double gap3 = (now - _lastBurnoutTime).TotalSeconds;

                if (gap <= COMBO_TIMEOUT_SEC)
                { }
                else
                {
                    if (gap2 <= COMBO_TIMEOUT_SEC)
                    { }
                    else
                    {
                        if (gap3 <= COMBO_TIMEOUT_SEC)
                        { }
                        else
                        {
                            CurrentRunPoints = 0;
                            ComboMultiplier = 1;
                            driftTotalTime = 0;
                            eBrakeCount--;
                            if (eBrakeCount <= 0)
                            {
                                eDriftActive = false;
                                eBrakeCount = 0;
                            }
                        }
                    }

                    //if (!speeding) { CurrentRunPoints = 0; }
                }

                //if (!speeding && !drifting && !_previouslyDrifting) { ComboMultiplier = 0; }

                if (ComboMultiplier <= 1 && !_previouslyDrifting)
                {
                    //CurrentRunPoints = 0;
                }
                else
                {

                }

            }



            if (!drifting && speeding)
            {

                if (SpeedKmh >= FAST_DRIVE_THRESHOLD * 2.2)
                    ComboMultiplier = 5;
                else if (SpeedKmh >= FAST_DRIVE_THRESHOLD * 1.9 && SpeedKmh < FAST_DRIVE_THRESHOLD * 2.2)
                    ComboMultiplier = 4;
                else if (SpeedKmh >= FAST_DRIVE_THRESHOLD * 1.6 && SpeedKmh < FAST_DRIVE_THRESHOLD * 1.9)
                    ComboMultiplier = 3;
                else if (SpeedKmh >= FAST_DRIVE_THRESHOLD * 1.3 && SpeedKmh < FAST_DRIVE_THRESHOLD * 1.6)
                    ComboMultiplier = 2;
                else if (SpeedKmh >= FAST_DRIVE_THRESHOLD * 1.0 && SpeedKmh < FAST_DRIVE_THRESHOLD * 1.3)
                    ComboMultiplier = 1;
                else
                {
                    ComboMultiplier = 1;
                }


                long fastPts = (long)(FAST_DRIVE_POINTS * _dtSec * ComboMultiplier);



                CurrentRunPoints += (long)fastPts;
                TotalScore += fastPts;
                IsSpeeding = true;
                LapScore += fastPts;


                _lastSpeedingTime = now;

                SaveCurrentDriver();

                // after
                var labelKind = GetLabelKind(DriftAngleDeg, SpeedKmh, eDriftActive);
                DriftScored?.Invoke(
                    CurrentRunPoints,
                    ComboMultiplier,
                    Localization.T(LabelKindToKey(labelKind)),
                    GetDriftValueINDR(DriftAngleDeg, DriftSideRight),
                    GetDriftValueINDL(DriftAngleDeg, DriftSideRight),
                    labelKind
                );
                //DriftEnded?.Invoke(CurrentRunPoints);
                if (!drifting)
                {
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                    if (ComboMultiplier > 1) { }
                    else
                    {

                        //CurrentRunPoints = 0;

                    }
                    if (!speeding)
                    {
                        IsSpeeding = false;
                        //CurrentRunPoints = 0;

                        //LastAwardedText = FormatRunResult(CurrentRunPoints, ComboMultiplier);
                    }

                }
            }

            double bonusgapCLR = (now - _lastBonusTime).TotalSeconds;
            if (bonusgapCLR >= BONUS_TIMEOUT_SEC)
            {
                LastAwardedText = $"";
            }



            if (drifting)
            {
                if (DriftAngleDeg >= 55)
                {
                    if (_deepDriftSessionStart == null)
                        _deepDriftSessionStart = now;
                }
                else if (_deepDriftSessionStart != null)
                {
                    FinalizeDeepDriftSession(now);
                }

                double angleScore = AngleScore(DriftAngleDeg);
                double speedScore = SpeedScore(SpeedKmh);

                //if (_isHandBrakeON) { eBrakeCount+=2; }


                if (!_previouslyDrifting)
                {
                                     


                    _driftStartTime = now;
                    _driftSessionStart = now;
                    driftTimeMultipler = 0;
                   
                    //CurrentRunPoints = 0;
                    if (!_isHandBrakeON) { eBrakeCount--; }
                    eDriftActive = false;
                    //
                    //

                    IsDrifting = true;

                    // ── kąt entry: start pomiaru dla nowej sekwencji driftu ──
                    _driftEntryStartTime = now;
                    _driftEntryEvaluated = false;
                    //_driftStartTime = now;
                    double gap = (now - _lastDriftTime).TotalSeconds;
                    double bonusgap = (now - _lastBonusTime).TotalSeconds;

                    if (gap <= COMBO_TIMEOUT_SEC && _lastDriftTime != DateTime.MinValue)
                    {
                        if (_previouslyDriftSideRight != DriftSideRight)
                        {
                            double bonusScore = 100;


                            if (bonusgap >= BONUS_TIMEOUT_SEC && gap <= (COMBO_TIMEOUT_SEC / 4))
                            {
                                if (SpeedKmh >= 35 && DriftAngleDeg >= MIN_DRIFT_ANGLE_DEG)
                                {


                                    if (!_isHandBrakeON) { eBrakeCount = 0; }

                                    var labelKindT = DriftLabelKind.TransitionDrift;
                                    string bonusText = Localization.T(LabelKindToKey(labelKindT));
                                    _lastBonusTime = now;
                                    ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.11 * speedScore), 10.0), 2);
                                    bonusScore = Math.Round(bonusScore, 0) * Math.Round(ComboMultiplier, 2);
                                    CurrentRunPoints = CurrentRunPoints + (long)Math.Round(bonusScore, 0);
                                    TotalScore = TotalScore + (long)bonusScore;
                                    LapScore = LapScore + (long)bonusScore;
                                    LastAwardedText = $"{bonusText} + {Math.Round(bonusScore)}";

                                }

                            }

                            _previouslyDriftSideRight = DriftSideRight;
                        }

                    }
                    else
                    {

                        //ComboMultiplier = 1;
                        if (!speeding)
                        {

                            //CurrentRunPoints = 0; 
                        }
                    }


                    DriftStarted?.Invoke();
                    
                }

                // ── kąt entry: ocena dokładnie w momencie T+1,5s od startu sekwencji driftu ──
                // (nie w chwili startu ani nie jako średnia — tylko wartość DriftAngleDeg
                // dokładnie w klatce, w której mija 1,5s nieprzerwanego driftu)
                if (!_driftEntryEvaluated
                    && _driftEntryStartTime != DateTime.MinValue
                    && (now - _driftEntryStartTime).TotalSeconds >= DRIFT_ENTRY_EVAL_SEC
                    && driftTimeMultipler <= 2
                    && driftTotalTime <= 1.0)
                {
                    _driftEntryEvaluated = true;
                    EvaluateDriftEntry(DriftAngleDeg);
                }

                _lastDriftTime = now;
                double driftTime = (now - _driftStartTime).TotalMilliseconds;


                if (driftTime >= 500)
                {
                    if (!_isHandBrakeON) { eBrakeCount--; }

                    if (eBrakeCount <= 0)
                    {
                        eDriftActive = false;
                        eBrakeCount = 0;
                    }

                    var labelKindT = DriftLabelKind.LongDrift;
                    string bonusText = Localization.T(LabelKindToKey(labelKindT));

                    driftTotalTime += 0.5;
                    driftTimeMultipler++;

                    ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.036 * angleScore), 10.0), 2);
                    _driftStartTime = now;

                    if (driftTimeMultipler == 5)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.15 * angleScore), 10.0), 2);

                    }
                    else if (driftTimeMultipler == 10)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.25 * angleScore), 10.0), 2);
                    }
                    else if (driftTimeMultipler == 15)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.35 * angleScore), 10.0), 2);
                    }
                    else if (driftTimeMultipler == 20)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.45 * angleScore), 10.0), 2);
                    }

                    labelKindT = DriftLabelKind.DeepLongDrift;
                    bonusText = Localization.T(LabelKindToKey(labelKindT));


                    if (DriftAngleDeg >= 55)
                    {
                        deepAngleCount++;
                    }
                    else deepAngleCount = 0;
                    if (deepAngleCount == 4)
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.35 * angleScore), 10.0), 2);

                    }
                    else if (deepAngleCount == 8)
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.45 * angleScore), 10.0), 2);
                    }
                    else if (deepAngleCount == 12)
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.55 * angleScore), 10.0), 2);
                    }
                    else if (deepAngleCount == 16)
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.65 * angleScore), 10.0), 2);
                    }



                    driftTime = 0;

                }

                if (eBrakeCount >= 1) { eDriftActive = true; }



                double framePts = POINTS_PER_SECOND
                                * (ComboMultiplier
                                * 0.25f)
                                * (angleScore * 1.5f)
                                * speedScore
                                * _dtSec;

                CurrentRunPoints += (long)framePts;
                TotalScore += (long)framePts;
                LapScore += (long)framePts;
                SaveCurrentDriver();






                // after
                var labelKind = GetLabelKind(DriftAngleDeg, SpeedKmh, eDriftActive);
                DriftScored?.Invoke(
                    CurrentRunPoints,
                    ComboMultiplier,
                    Localization.T(LabelKindToKey(labelKind)),
                    GetDriftValueINDR(DriftAngleDeg, DriftSideRight),
                    GetDriftValueINDL(DriftAngleDeg, DriftSideRight),
                    labelKind
                );
            }
            else if (_previouslyDrifting)
            {
                IsDrifting = false;

                FinalizeDriftSession(now);
                FinalizeDeepDriftSession(now);

                // koniec sekwencji driftu — sprzątamy stan oceny entry, żeby kolejna
                // sekwencja zaczynała pomiar od zera
                _driftEntryStartTime = DateTime.MinValue;
                _driftEntryEvaluated = false;

                SaveCurrentDriver();




                if (!speeding)
                {


                    //LastAwardedText = FormatRunResult(CurrentRunPoints, ComboMultiplier);
                    //_driftStartTime = now;
                    //_lastDriftTime = now;
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);



                    IsSpeeding = false;


                }

            }
            if (!drifting && _previouslyDrifting)
            {
                _lastDriftTime = now;


            }

            if (!speeding && !drifting && _previouslyDrifting)
            {



                if (ComboMultiplier > 1) { }
                else
                {
                    //LastAwardedText = FormatRunResult(CurrentRunPoints, ComboMultiplier);
                    //CurrentRunPoints = 0;

                }


            }


            _previouslyDrifting = drifting;
        }

        // ────────────────────────────────────────────────────────
        //  Persistence
        // ────────────────────────────────────────────────────────
        private void LoadStats()
        {
            try
            {
                if (!File.Exists(_statsFile))
                {
                    _driverScores = new Dictionary<string, DriverStats>();
                    return;
                }

                string json = File.ReadAllText(_statsFile);

                try
                {
                    _driverScores = JsonSerializer.Deserialize<Dictionary<string, DriverStats>>(json)
                                    ?? new Dictionary<string, DriverStats>();
                }
                catch
                {
                    // migracja ze starego formatu pliku (Dictionary<string, long>)
                    var old = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                    _driverScores = new Dictionary<string, DriverStats>();

                    if (old != null)
                    {
                        foreach (var kv in old)
                            _driverScores[kv.Key] = new DriverStats { TotalScore = kv.Value };
                    }
                }
            }
            catch
            {
                _driverScores = new Dictionary<string, DriverStats>();
            }
        }
        private long GetLastLapForDriver(string driver, string track, string layout)
        {
            string layoutKey = string.IsNullOrWhiteSpace(layout) ? "default" : layout;

            if (_driverLastLapRecords.TryGetValue(driver, out var trackMap) &&
                trackMap.TryGetValue(track, out var layoutMap) &&
                layoutMap.TryGetValue(layoutKey, out var last))
                return last;

            return 0;
        }

        private void LoadLastLapRecords()
        {
            try
            {
                if (!File.Exists(_lastLapRecordsFile))
                {
                    _driverLastLapRecords = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
                    return;
                }

                string json = File.ReadAllText(_lastLapRecordsFile);
                _driverLastLapRecords = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, long>>>>(json)
                                         ?? new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
            }
            catch
            {
                _driverLastLapRecords = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
            }
        }

        private void MigrateDefaultLastLapRecordIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_currentLayoutName)) return;
            if (!_driverLastLapRecords.TryGetValue(CurrentDriver, out var trackMap)) return;
            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap)) return;
            if (!layoutMap.TryGetValue("default", out var defaultScore)) return;
            if (layoutMap.ContainsKey(_currentLayoutName)) return;

            layoutMap[_currentLayoutName] = defaultScore;
            layoutMap.Remove("default");
            SaveLastLapRecordsRaw();
        }

        private void SaveLastLapRecord()
        {
            string layoutKey = string.IsNullOrWhiteSpace(_currentLayoutName) ? "default" : _currentLayoutName;

            if (!_driverLastLapRecords.TryGetValue(CurrentDriver, out var trackMap))
            {
                trackMap = new Dictionary<string, Dictionary<string, long>>();
                _driverLastLapRecords[CurrentDriver] = trackMap;
            }

            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap))
            {
                layoutMap = new Dictionary<string, long>();
                trackMap[_currentTrackCode] = layoutMap;
            }

            layoutMap[layoutKey] = LastLapScore;
            SaveLastLapRecordsRaw();
        }

        private void SaveLastLapRecordsRaw()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _driverLastLapRecords,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_lastLapRecordsFile, json);
            }
            catch
            {
            }
        }
        private long GetBestLapForDriver(string driver, string track, string layout)
        {
            string layoutKey = string.IsNullOrWhiteSpace(layout) ? "default" : layout;

            if (_driverLapRecords.TryGetValue(driver, out var trackMap) &&
                trackMap.TryGetValue(track, out var layoutMap) &&
                layoutMap.TryGetValue(layoutKey, out var best))
                return best;

            return 0;
        }

        private void LoadLapRecords()
        {
            try
            {
                if (!File.Exists(_lapRecordsFile))
                {
                    _driverLapRecords = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
                    return;
                }

                string json = File.ReadAllText(_lapRecordsFile);
                _driverLapRecords = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, long>>>>(json)
                                     ?? new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
            }
            catch
            {
                _driverLapRecords = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
            }
        }

        private void SaveLapRecords()
        {
            string layoutKey = string.IsNullOrWhiteSpace(_currentLayoutName) ? "default" : _currentLayoutName;

            if (!_driverLapRecords.TryGetValue(CurrentDriver, out var trackMap))
            {
                trackMap = new Dictionary<string, Dictionary<string, long>>();
                _driverLapRecords[CurrentDriver] = trackMap;
            }

            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap))
            {
                layoutMap = new Dictionary<string, long>();
                trackMap[_currentTrackCode] = layoutMap;
            }

            layoutMap[layoutKey] = BestLapScore;

            SaveLapRecordsRaw();
        }

        private void SaveLapRecordsRaw()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _driverLapRecords,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_lapRecordsFile, json);
            }
            catch
            {
            }
        }


        private void SaveCurrentDriver()
        {
            try
            {
                if (!_driverScores.TryGetValue(CurrentDriver, out var stats))
                {
                    stats = new DriverStats();
                    _driverScores[CurrentDriver] = stats;
                }

                stats.TotalScore = TotalScore;
                stats.BestRunScore = BestRunScore;
                stats.BestDriftDurationMs = BestDriftDurationMs;
                stats.BestDeepDriftDurationMs = BestDeepDriftDurationMs;

                string json = JsonSerializer.Serialize(
                    _driverScores,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(_statsFile, json);
            }
            catch
            {
            }
        }

        // ────────────────────────────────────────────────────────
        //  Rekordy: najlepszy run / najdłuższy drift / głęboki drift
        // ────────────────────────────────────────────────────────
        private void RegisterRunEnd(long runPoints)
        {
            if (runPoints > BestRunScore)
            {
                BestRunScore = runPoints;
                SaveCurrentDriver();
            }
        }

        /// <summary>Dodaje/odejmuje punkty za uderzenie w post (bonus/kara). Nie wpływa na CurrentRunPoints ani combo.</summary>
        public void ApplyPostPoints(long delta, string awardLabel)
        {
            TotalScore += delta;
            LapScore += delta;
            if (ComboMultiplier > 0) ComboMultiplier += 0.1;
            LastAwardedText = awardLabel;
            SaveCurrentDriver();
        }

        // Jednorazowa ocena kąta entry — wywoływana dokładnie raz na sekwencję driftu,
        // w klatce w której mija DRIFT_ENTRY_EVAL_SEC od jej rozpoczęcia. Kąt poniżej progu
        // "good" (50°) nie daje żadnego bonusu — to nie kara, po prostu entry nie było
        // wystarczająco głębokie, żeby się wyróżnić.
        private void EvaluateDriftEntry(double angleAtEval)
        {
            DriftLabelKind kind;
            long bonus;

            if (angleAtEval > DRIFT_ENTRY_BACKWARD_DEG) { kind = DriftLabelKind.EntryBackward; bonus = 1100; }
            else if (angleAtEval > DRIFT_ENTRY_ULTRAEXTREME_DEG) { kind = DriftLabelKind.EntryUltraExtreme; bonus = 750; }
            else if (angleAtEval > DRIFT_ENTRY_EXTREME_DEG) { kind = DriftLabelKind.EntryExtreme; bonus = 500; }
            else if (angleAtEval > DRIFT_ENTRY_HIGH_DEG) { kind = DriftLabelKind.EntryHigh; bonus = 300; }
            else if (angleAtEval > DRIFT_ENTRY_GOOD_DEG) { kind = DriftLabelKind.EntryGood; bonus = 150; }
            else return;   // kąt entry poniżej progu "good" — bez bonusu

            string text = Localization.T(LabelKindToKey(kind));
            ApplyPostPoints(bonus, $"{text} +{bonus}");
        }

        private void FinalizeDriftSession(DateTime now)
        {
            if (_driftSessionStart == DateTime.MinValue) return;

            double durationMs = (now - _driftSessionStart).TotalMilliseconds;
            if (durationMs > BestDriftDurationMs)
            {
                BestDriftDurationMs = durationMs;
                SaveCurrentDriver();
            }

            _driftSessionStart = DateTime.MinValue;
        }

        private void FinalizeDeepDriftSession(DateTime now)
        {
            if (_deepDriftSessionStart == null) return;

            double durationMs = (now - _deepDriftSessionStart.Value).TotalMilliseconds;
            if (durationMs > BestDeepDriftDurationMs)
            {
                BestDeepDriftDurationMs = durationMs;
                SaveCurrentDriver();
            }

            _deepDriftSessionStart = null;
        }

        // ────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────
        private static double AngleScore(double angle)
        {
            angle = Math.Clamp(angle, MIN_DRIFT_ANGLE_DEG, MAX_DRIFT_ANGLE_DEG);

            return 0.00001 +
                   ((angle - MIN_DRIFT_ANGLE_DEG) /
                   (MAX_DRIFT_ANGLE_DEG - MIN_DRIFT_ANGLE_DEG)) * 2.99;
        }

        private static double SpeedScore(double kmh)
        {

            kmh = Math.Clamp(kmh, 1, 100);

            return 0.001 +
                   ((kmh - 1) /
                   (100 - 15)) * 1.99;


            // double norm = kmh / (FAST_DRIVE_THRESHOLD - 40);
            // return Math.Max(0.5, Math.Min(2.5, norm));
        }

        private static string GetDriftValueINDR(double angle, bool side)
        {
            if (side == true)
            {
                if (angle > 90) return " ???";
                if (angle > 65) return " >>>>>";
                if (angle > 55) return " >>>>";
                if (angle > 45) return " >>>";
                if (angle > 25) return " >>";
                if (angle > 15) return " >";
            }
            else
            {
                if (angle > 90) return "       ";
                if (angle > 65) return "      ";
                if (angle > 55) return "     ";
                if (angle > 45) return "    ";
                if (angle > 25) return "   ";
                if (angle > 15) return "  ";
            }

            return "";
        }
        private static string GetDriftValueINDL(double angle, bool side)
        {
            if (side == false)
            {
                if (angle > 90) return "??? ";
                if (angle > 65) return "<<<<< ";
                if (angle > 55) return "<<<< ";
                if (angle > 45) return "<<< ";
                if (angle > 25) return "<< ";
                if (angle > 10) return "< ";
            }
            else
            {
                if (angle > 90) return "       ";
                if (angle > 65) return "      ";
                if (angle > 55) return "     ";
                if (angle > 45) return "    ";
                if (angle > 25) return "   ";
                if (angle > 15) return "  ";
            }

            return "";
        }
        public enum DriftLabelKind
        {
            Generic,
            GenericE,
            Fast1,
            Fast2,
            Fast3,
            AngleGood,
            AngleHigh,
            AngleExtreme,
            AngleUltraExtreme,
            AngleBackward,
            AngleGoodE,
            AngleHighE,
            AngleExtremeE,
            AngleUltraExtremeE,
            AngleBackwardE,
            FastDrift,
            TransitionDrift,
            DeepLongDrift,
            LongDrift,
            BurnoutGood,
            BurnoutHigh,
            BurnoutExtreme,
            BurnoutInsane,
            EntryGood,
            EntryHigh,
            EntryExtreme,
            EntryUltraExtreme,
            EntryBackward
        }
        private DriftLabelKind GetLabelKind(double angle, double speed, bool brake)
        {
            if (speed >= FAST_DRIVE_THRESHOLD && angle < 8 && speed < FAST_DRIVE_THRESHOLD * 1.5)
                return DriftLabelKind.Fast1;
            if (speed >= FAST_DRIVE_THRESHOLD * 1.5 && angle < 8 && speed < FAST_DRIVE_THRESHOLD * 2.0)
                return DriftLabelKind.Fast2;
            if (speed >= FAST_DRIVE_THRESHOLD * 2.0 && angle < 8)
                return DriftLabelKind.Fast3;
            if (!brake)
            {
                if (angle > 90 && speed > 30) return DriftLabelKind.AngleBackward;
                if (angle > 65) return DriftLabelKind.AngleUltraExtreme;
                if (angle > 55) return DriftLabelKind.AngleExtreme;
                if (angle > 45) return DriftLabelKind.AngleHigh;
                if (angle > 25) return DriftLabelKind.AngleGood;
                if (angle < 25 && angle > 15) return DriftLabelKind.Generic;
            }
            else
            {
                if (angle > 90 && speed > 30) return DriftLabelKind.AngleBackwardE;
                if (angle > 65) return DriftLabelKind.AngleUltraExtremeE;
                if (angle > 55) return DriftLabelKind.AngleExtremeE;
                if (angle > 45) return DriftLabelKind.AngleHighE;
                if (angle > 25) return DriftLabelKind.AngleGoodE;
                if (angle < 25 && angle > 15) return DriftLabelKind.GenericE;
            }

            if (speed > 120 && angle > 20) return DriftLabelKind.FastDrift;

            return DriftLabelKind.Generic;
        }

        private string GetLabel(double angle, double speed, bool brake)
        {
            return Localization.T(LabelKindToKey(GetLabelKind(angle, speed, brake)));
        }

        public static string LabelKindToKey(DriftLabelKind kind) => kind switch
        {
            DriftLabelKind.Fast1 => "drift.label.fast1",
            DriftLabelKind.Fast2 => "drift.label.fast2",
            DriftLabelKind.Fast3 => "drift.label.fast3",
            DriftLabelKind.AngleBackward => "drift.label.angle_backward",
            DriftLabelKind.AngleUltraExtreme => "drift.label.angle_ultraextreme",
            DriftLabelKind.AngleExtreme => "drift.label.angle_extreme",
            DriftLabelKind.AngleHigh => "drift.label.angle_high",
            DriftLabelKind.AngleGood => "drift.label.angle_good",
            DriftLabelKind.AngleBackwardE => "drift.label.angle_backwarde",
            DriftLabelKind.AngleUltraExtremeE => "drift.label.angle_ultraextremee",
            DriftLabelKind.AngleExtremeE => "drift.label.angle_extremee",
            DriftLabelKind.AngleHighE => "drift.label.angle_highe",
            DriftLabelKind.AngleGoodE => "drift.label.angle_goode",
            DriftLabelKind.FastDrift => "drift.label.fast_drift",
            DriftLabelKind.TransitionDrift => "drift.label.transition_drift",
            DriftLabelKind.LongDrift => "drift.label.long_drift",
            DriftLabelKind.DeepLongDrift => "drift.label.deep_long_drift",
            DriftLabelKind.GenericE => "drift.label.generice",
            DriftLabelKind.BurnoutGood => "drift.label.burnout_good",
            DriftLabelKind.BurnoutHigh => "drift.label.burnout_high",
            DriftLabelKind.BurnoutExtreme => "drift.label.burnout_extreme",
            DriftLabelKind.BurnoutInsane => "drift.label.burnout_insane",
            DriftLabelKind.EntryGood => "drift.label.entry_good",
            DriftLabelKind.EntryHigh => "drift.label.entry_high",
            DriftLabelKind.EntryExtreme => "drift.label.entry_extreme",
            DriftLabelKind.EntryUltraExtreme => "drift.label.entry_ultraextreme",
            DriftLabelKind.EntryBackward => "drift.label.entry_backward",
            _ => "drift.label.generic",
        };

        private static string FormatRunResult(long pts, int combo)
        {
            if (pts >= 5000) return $"SPERMASTYCZNIE! {pts:N0} pts";
            if (pts >= 2500) return $"ALE DOJEBAŁEŚ! {pts:N0} pts";
            if (pts >= 1000) return $"NAJS! {pts:N0} pts";
            if (pts >= 500) return $"NO NIEŹLE! {pts:N0} pts";

            return $"{pts:N0} pts";
        }

        // ────────────────────────────────────────────────────────
        //  Burnout detection — koła napędowe kręcą się dużo szybciej niż
        //  rzeczywiście jedzie auto (typowe stanie w miejscu z wciśniętym gazem).
        // ────────────────────────────────────────────────────────

        // Rzeczywista prędkość (SpeedKmh, z InSim MCI) musi być poniżej tego progu —
        // inaczej to już normalna jazda/drift, nie stanie w miejscu.
        private const double BURNOUT_MAX_REAL_SPEED_KMH = 25.0;

        // RPM musi być WYRAŹNIE powyżej typowego jałowego biegu (zwykle 800–1200 RPM w
        // LFS) — inaczej silnik pracujący na postoju (auto stoi, kierowca nic nie robi)
        // fałszywie zaliczał się jako "burnout" (patrz zgłoszony błąd: wykrywanie na
        // jałowym). Razem z progiem gazu i wymogiem włączonego biegu poniżej, to jest
        // teraz JEDYNY sygnał burnoutu — bez estymacji prędkości kół (patrz komentarz
        // przy UpdateEngineTelemetry: próby jej wyliczenia z RPM+przełożenia, a potem z
        // OutSim AngVel, okazały się niewiarygodne w praktyce i zostały usunięte).
        private const float BURNOUT_MIN_RPM = 3000f;

        // Gaz musi być wciśnięty wyraźnie mocno — samo wysokie RPM (np. przy zjeżdżaniu
        // z redline po zdjęciu nogi z gazu) nie powinno liczyć się jako burnout.
        private const float BURNOUT_MIN_THROTTLE = 0.6f;

        // Warunki (prędkość + RPM + gaz + bieg) muszą trzymać się nieprzerwanie co
        // najmniej tyle sekund, zanim burnout zostanie uznany za aktywny i zacznie
        // liczyć punkty.
        private const double BURNOUT_ACTIVATE_SEC = 0.5;

        // Bazowe punkty na sekundę PO aktywacji (czyli już po upływie progu 3s),
        // mnożone przez rosnący ComboMultiplier (patrz UpdateBurnout).
        private const double BURNOUT_POINTS_PER_SECOND = 5.0;

        // Bonus za każdy pełny obrót (360°) podczas aktywnego burnoutu ("bączek").
        private const long BURNOUT_SPIN_BONUS_POINTS = 250;

        public bool IsBurnout { get; private set; }

        // Telemetria silnika karmiona z zewnątrz (patrz UpdateEngineTelemetry) — InSim MCI
        // daje tylko rzeczywistą prędkość auta, więc RPM/gaz/bieg (z OutGauge) trzeba
        // dostarczyć osobno. OutGauge.Gear: 0=wsteczny, 1=jałowy, 2=1.bieg, 3=2.bieg...
        private float _lastRpm = 0f;
        private float _lastThrottle = 0f;
        private int _lastGear = 0;

        // Moment od którego warunki burnoutu (prędkość<10, różnica kół>=20) trzymają się
        // nieprzerwanie. DateTime.MinValue = warunki obecnie niespełnione.
        private DateTime _burnoutConditionStart = DateTime.MinValue;

        // Ostatni moment doliczenia punktów za aktywny burnout — analogicznie do
        // _lastDriftTime/_lastSpeedingTime, chroni CurrentRunPoints/ComboMultiplier
        // przed wyzerowaniem przez ogólny blok resetu combo (patrz Update()), który
        // inaczej zerowałby wynik co klatkę tuż po jego doliczeniu, bo burnout z
        // definicji ma SpeedKmh poniżej progu drift/speeding. Dzięki temu samemu
        // znacznikowi (i wspólnej właściwości ComboMultiplier) combo zdobyte podczas
        // burnoutu przeżywa cooldown i płynnie kontynuuje się przy przejściu do
        // driftu/szybkiej jazdy — dokładnie jak combo drift↔speeding.
        private DateTime _lastBurnoutTime = DateTime.MinValue;

        // Śledzenie obrotu (heading) auta — do wykrywania pełnych "bączków" 360°.
        // Oparte WYŁĄCZNIE na prędkości kątowej (zmianie headingDeg / czas), a nie na
        // SpeedKmh — bączek może mieć realny promień i realną prędkość liniową, więc
        // wymaganie "auto stoi w miejscu" odrzucało większość prawdziwych bączków.
        private double? _lastBurnoutHeadingDeg = null;
        private double _burnoutSpinAccumDeg = 0;
        private DateTime _lastSpinActivityTime = DateTime.MinValue;

        // Prędkość kątowa auta musi przekraczać ten próg, żeby liczyć to jako "kręcenie
        // bączka" a nie zwykłe pokonywanie zakrętu na torze (nawet ciasna szpilka rzadko
        // generuje takie tempo zmiany kierunku). Wartość do ew. dostrojenia.
        private const double BURNOUT_SPIN_MIN_YAW_RATE_DEG_PER_SEC = 90.0;

        // Jeśli prędkość kątowa spadnie poniżej progu na dłużej niż tyle sekund, uznajemy
        // że to nie był ciągły bączek (np. zwykła jazda) i zerujemy dotychczasowy postęp —
        // ale POJEDYNCZA wolniejsza klatka (np. przez szum odczytu) nie kasuje progresu.
        private const double BURNOUT_SPIN_ABANDON_SEC = 1.0;

        // Czy burnout był aktywny w JAKIMKOLWIEK momencie trwania bieżącego, niedokończonego
        // jeszcze obrotu — a nie tylko dokładnie w klatce, w której akumulator przekroczy 360°.
        // Bez tego pojedyncza klatka migotania IsBurnout (przez szum wheelDiff) dokładnie
        // w momencie ukończenia obrotu potrafiłaby po cichu "zjeść" należny bonus.
        private bool _burnoutActiveDuringSpin = false;

        /// <summary>
        /// Aktualizuje telemetrię silnika (RPM, gaz, bieg) używaną do wykrywania burnoutu —
        /// patrz MainForm.OnRevData (dane z OutGauge). Wywołuj to niezależnie od Update(car);
        /// najświeższa wartość jest brana pod uwagę przy najbliższym wywołaniu Update().
        /// </summary>
        public void UpdateEngineTelemetry(float rpm, float throttle, int gear)
        {
            _lastRpm = Math.Max(0, rpm);
            _lastThrottle = Math.Clamp(throttle, 0f, 1f);
            _lastGear = gear;
        }

        private void UpdateBurnout(DateTime now, bool drifting, bool speeding, double headingDeg)
        {
            // ── Śledzenie obrotu (bączki) ───────────────────────────────────────
            // Oparte na prędkości kątowej (deg/s), nie na SpeedKmh — bączek z realnym
            // promieniem ma realną prędkość liniową, więc wymaganie "auto stoi w miejscu"
            // odrzucało większość prawdziwych bączków. Prędkość kątowa naturalnie odróżnia
            // spin od zwykłej jazdy po torze, bo nawet ciasny zakręt rzadko osiąga tak
            // wysokie tempo zmiany kierunku na dłużej niż ułamek sekundy.
            if (_lastBurnoutHeadingDeg.HasValue && _dtSec > 0.0001)
            {
                double spinDelta = headingDeg - _lastBurnoutHeadingDeg.Value;
                while (spinDelta > 180) spinDelta -= 360;
                while (spinDelta < -180) spinDelta += 360;

                double yawRateDegPerSec = Math.Abs(spinDelta) / _dtSec;

                if (yawRateDegPerSec >= BURNOUT_SPIN_MIN_YAW_RATE_DEG_PER_SEC)
                {
                    _burnoutSpinAccumDeg += Math.Abs(spinDelta);
                    _burnoutActiveDuringSpin = _burnoutActiveDuringSpin || IsBurnout;
                    _lastSpinActivityTime = now;
                }
                else if ((now - _lastSpinActivityTime).TotalSeconds > BURNOUT_SPIN_ABANDON_SEC)
                {
                    // zbyt długo bez szybkiego obrotu — to nie był ciągły bączek, tylko
                    // zwykła jazda; zeruj dotychczasowy, niedokończony postęp
                    _burnoutSpinAccumDeg = 0;
                    _burnoutActiveDuringSpin = false;
                }
                // w przeciwnym razie: pojedyncza wolniejsza klatka w trakcie bączka
                // (np. moment zmiany biegu) — nie kasujemy postępu, czekamy na timeout
            }
            else
            {
                _lastSpinActivityTime = now;
            }
            _lastBurnoutHeadingDeg = headingDeg;

            while (_burnoutSpinAccumDeg >= 360.0)
            {
                _burnoutSpinAccumDeg -= 360.0;

                if (_burnoutActiveDuringSpin)
                {
                    ApplyPostPoints(BURNOUT_SPIN_BONUS_POINTS,
                        $"{Localization.T("drift.label.burnout_spin")} +{BURNOUT_SPIN_BONUS_POINTS}");
                }

                _burnoutActiveDuringSpin = false;   // reset dla kolejnego, nowego obrotu
            }

            // Bieg jałowy/wsteczny (Gear<2) wyklucza burnout niezależnie od RPM/gazu —
            // na jałowym silnik fizycznie nie napędza kół, więc nawet trzymanie gazu do
            // dechy na postoju to NIE jest buksowanie.
            bool conditionNow = SpeedKmh < BURNOUT_MAX_REAL_SPEED_KMH
                              && _lastGear >= 2
                              && _lastThrottle >= BURNOUT_MIN_THROTTLE
                              && _lastRpm >= BURNOUT_MIN_RPM;

            if (!conditionNow)
            {
                if (IsBurnout)
                {
                    // burnout był aktywny i właśnie się skończył (auto ruszyło albo koła przestały buksować)
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                    
                }
                _burnoutConditionStart = DateTime.MinValue;
                IsBurnout = false;
                
                return;
            }

            if (_burnoutConditionStart == DateTime.MinValue)
                _burnoutConditionStart = now;

            double heldSec = (now - _burnoutConditionStart).TotalSeconds;

            if (heldSec < BURNOUT_ACTIVATE_SEC)
                return;   // warunki spełnione, ale jeszcze nie od wystarczająco dawna

            bool justActivated = !IsBurnout;
            IsBurnout = true;

            if (justActivated)
                DriftStarted?.Invoke();

            // im wyższe RPM ponad próg aktywacji, tym szybciej rośnie ComboMultiplier —
            // ten sam wzorzec co w drifcie (ComboMultiplier rośnie z angleScore co klatkę,
            // ograniczone do 10.0). Ponieważ ComboMultiplier to ta sama, WSPÓLNA właściwość,
            // co dla driftu/speedingu, combo zdobyte w burnoucie naturalnie kontynuuje się
            // przy przejściu do driftu albo szybkiej jazdy (i odwrotnie), a blok resetu combo
            // w Update() chroni je przez COMBO_TIMEOUT_SEC dzięki _lastBurnoutTime.
            double rpmFactor = Math.Clamp(_lastRpm / BURNOUT_MIN_RPM, 1.0, 5.0);
            ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.01 * rpmFactor * _dtSec), 10.0), 2);

            long framePts = (long)Math.Round(BURNOUT_POINTS_PER_SECOND * ComboMultiplier * _dtSec);

            CurrentRunPoints += framePts;
            TotalScore += framePts;
            LapScore += framePts;
            _lastBurnoutTime = now;
            SaveCurrentDriver();

            var burnoutKind = GetBurnoutLabelKind(_lastRpm, heldSec);
            DriftScored?.Invoke(
                CurrentRunPoints,
                ComboMultiplier,
                Localization.T(LabelKindToKey(burnoutKind)),
                "",
                "",
                burnoutKind
            );
        }

        // Stopniowanie burnoutu na wzór GetLabelKind (drift/speed) — łączy nadwyżkę RPM
        // ponad próg aktywacji z czasem trwania w jedną skalę "intensywności", tak jak
        // tam łączy się kąt i prędkość. Dzielnik 100 dobrany tak, żeby solidne trzymanie
        // wysokich obrotów (kilka tysięcy RPM ponad próg) dawało odczuwalną severity od
        // razu, a nie dopiero po kilkudziesięciu sekundach — wartość do ew. dostrojenia.
        private DriftLabelKind GetBurnoutLabelKind(float rpm, double heldSec)
        {
            double rpmAboveThreshold = Math.Max(0, rpm - BURNOUT_MIN_RPM);
            double severity = (rpmAboveThreshold / 100.0) + ((heldSec - BURNOUT_ACTIVATE_SEC) * 1.8);

            if (severity >= 80) return DriftLabelKind.BurnoutInsane;
            if (severity >= 65) return DriftLabelKind.BurnoutExtreme;
            if (severity >= 50) return DriftLabelKind.BurnoutHigh;
            return DriftLabelKind.BurnoutGood;
        }

        public void ResetTotal()
        {
            TotalScore = 0;
            LapScore = 0;
            ComboMultiplier = 1;
            CurrentRunPoints = 0;
            IsDrifting = false;
            IsSpeeding = false;
            IsBurnout = false;
            _burnoutConditionStart = DateTime.MinValue;
            _lastBurnoutTime = DateTime.MinValue;
            _lastBurnoutHeadingDeg = null;
            _burnoutSpinAccumDeg = 0;
            _burnoutActiveDuringSpin = false;
            _lastSpinActivityTime = DateTime.MinValue;
            _driftEntryStartTime = DateTime.MinValue;
            _driftEntryEvaluated = false;
            _previouslyDrifting = false;
            _lastDriftTime = DateTime.MinValue;
            _lastSpeedingTime = DateTime.MinValue;

            BestRunScore = 0;
            BestDriftDurationMs = 0;
            BestDeepDriftDurationMs = 0;

            SaveCurrentDriver();
        }
        public void ResetLapScore()
        {
            LapScore = 0;
        }

        /// <summary>Aktywuje liczenie punktów okrążenia po przejeździe przez pierwszy punkt kontrolny InSim.
        /// Jeśli liczenie jest już aktywne — ignoruje wywołanie i zwraca false.</summary>
        public bool ActivateLapCounting()
        {
            if (HasActiveLapContext) return false;

            LapScore = 0;
            HasActiveLapContext = true;
            return true;
        }

        /// <summary>Wjazd na zakazany obszar — resetuje i dezaktywuje liczenie punktów okrążenia
        /// do czasu kolejnego przejazdu przez pierwszy punkt kontrolny.</summary>
        public void DeactivateLapCounting()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }

        /// <summary>Restart wyścigu — zeruje LapScore i chowa ramkę HUD do czasu 1. zaliczonego okrążenia.</summary>
        public void OnRaceRestarted()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }


    }
}