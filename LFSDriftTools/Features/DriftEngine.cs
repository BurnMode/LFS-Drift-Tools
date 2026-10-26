using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LFSDriftTools
{

    public class DriverStats
    {
        public long TotalScore { get; set; } = 0;
        public long BestRunScore { get; set; } = 0;
        public double BestDriftDurationMs { get; set; } = 0;
        public double BestDeepDriftDurationMs { get; set; } = 0;
    }

    public class DriverData
    {
        public int ConfigVersion { get; set; } = ConfigVersions.Current;
        public DriverStats Stats { get; set; } = new();
        public Dictionary<string, Dictionary<string, long>> LapRecords { get; set; } = new();
        public Dictionary<string, Dictionary<string, long>> LastLapRecords { get; set; } = new();
    }

    public class DriftEngine
    {

        private double _minDriftSpeedKmh = 20.0;
        public double MinDriftSpeedKmh => _minDriftSpeedKmh;
        public void SetMinDriftSpeedKmh(double kmh)
        {
            if (kmh >= 0) _minDriftSpeedKmh = kmh;
        }

        private const double MAX_DRIFT_ANGLE_DEG = 150.0;
        public const double COMBO_TIMEOUT_SEC = 2.5;
        private const double BONUS_TIMEOUT_SEC = 2.5;
        private const double POINTS_PER_SECOND = 400.0;
        private const double MaxResumeGapSec = 1.0;

        private double[] _angleLevelThresholds = { 20.0, 40.0, 50.0, 60.0, 70.0 };
        public IReadOnlyList<double> AngleLevelThresholds => _angleLevelThresholds;
        public void SetAngleLevelThresholds(double[] values)
        {
            if (values == null || values.Length != 5) return;
            _angleLevelThresholds = SanitizeThresholds(values, MAX_DRIFT_ANGLE_DEG - 1.0);
        }
        private static double[] SanitizeThresholds(double[] values, double maxFirst)
        {
            var copy = (double[])values.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                if (double.IsNaN(copy[i]) || double.IsInfinity(copy[i]) || copy[i] < 0)
                    copy[i] = 0;
            }

            Array.Sort(copy);
            copy[0] = Math.Min(copy[0], maxFirst);
            return copy;
        }

        private double[] _speedLevelThresholds = { 100.0, 150.0, 200.0, 250.0, 300.0 };
        public IReadOnlyList<double> SpeedLevelThresholds => _speedLevelThresholds;
        public void SetSpeedLevelThresholds(double[] values)
        {
            if (values == null || values.Length != 5) return;
            _speedLevelThresholds = SanitizeThresholds(values, double.MaxValue);
        }

        private double _backwardDriftAngleThreshold = 95.0;
        public double BackwardDriftAngleThreshold => _backwardDriftAngleThreshold;
        public void SetBackwardDriftAngleThreshold(double value)
        {
            if (double.IsNaN(value)) return;
            _backwardDriftAngleThreshold = Math.Clamp(value, 0, 180);
        }
        private const double FAST_DRIVE_POINTS = 8.0;

        private const double FAST_DRIVE_COMBO_STEP_PER_SEC = 0.02;

                private int GetFastDriveLevel(double speedKmh)
        {
            if (speedKmh >= _speedLevelThresholds[4]) return 5;
            if (speedKmh >= _speedLevelThresholds[3]) return 4;
            if (speedKmh >= _speedLevelThresholds[2]) return 3;
            if (speedKmh >= _speedLevelThresholds[1]) return 2;
            return 1;
        }

        public bool IsDrifting { get; private set; }
        public bool IsSpeeding { get; private set; }
        public double DriftAngleDeg { get; private set; }
        public bool DriftSideRight { get; private set; }
        public bool _isHandBrakeON { get; private set; } = false;
        public void SetHandbrakeActive(bool active) => _isHandBrakeON = active;
        public long CurrentRunPoints { get; private set; }
        public long LapScore { get; private set; }
        public long BestLapScore { get; private set; }
        public long LastLapScore { get; private set; }

        public long TotalScore { get; private set; }
        public double ComboMultiplier { get; private set; } = 1;
        public long BestRunScore { get; private set; }
        public double BestDriftDurationMs { get; private set; }
        public double BestDeepDriftDurationMs { get; private set; }
        public double SpeedKmh { get; private set; }
        public string LastAwardedText { get; private set; } = "";

        public string CurrentDriver { get; private set; } = "default_driver";

        private static readonly string SavesDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Saves");
        private Dictionary<string, DriverData> _driverData = new();

        private static readonly HashSet<string> ReservedFileNames = new()
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private const int MaxCachedDrivers = 32;

        private static bool IsEmptyDriverData(DriverData data) =>
            data.Stats.TotalScore == 0 && data.Stats.BestRunScore == 0 &&
            data.Stats.BestDriftDurationMs == 0 && data.Stats.BestDeepDriftDurationMs == 0 &&
            data.LapRecords.Count == 0 && data.LastLapRecords.Count == 0;

        private static string DriverFilePath(string driver)
        {
            if (string.IsNullOrWhiteSpace(driver)) driver = "default_driver";
            foreach (char c in Path.GetInvalidFileNameChars())
                driver = driver.Replace(c, '_');

            driver = driver.TrimEnd('.', ' ');
            if (driver.Length == 0) driver = "default_driver";
            if (driver.Length > 100) driver = driver.Substring(0, 100);

            int dot = driver.IndexOf('.');
            string stem = dot > 0 ? driver.Substring(0, dot) : driver;
            if (ReservedFileNames.Contains(stem.ToUpperInvariant()))
                driver = "_" + driver;

            return Path.Combine(SavesDir, driver + ".dat");
        }

        private static string DriverIdentity(string driver) =>
            "driver:" + Path.GetFileNameWithoutExtension(DriverFilePath(driver)).ToLowerInvariant();

        private const double MaxPlausibleDriftMs = 24 * 3600 * 1000.0;

        private static bool IsPlausible(DriverData data)
        {
            if (data.Stats == null || data.LapRecords == null || data.LastLapRecords == null) return false;
            if (data.Stats.BestRunScore < 0) return false;
            if (!IsPlausibleDuration(data.Stats.BestDriftDurationMs) || !IsPlausibleDuration(data.Stats.BestDeepDriftDurationMs)) return false;
            foreach (var layouts in data.LapRecords.Values)
            {
                if (layouts == null) return false;
                foreach (long score in layouts.Values)
                    if (score < 0) return false;
            }
            foreach (var layouts in data.LastLapRecords.Values)
                if (layouts == null) return false;
            return true;
        }

        private static bool IsPlausibleDuration(double ms) => double.IsFinite(ms) && ms >= 0 && ms <= MaxPlausibleDriftMs;

        private readonly HashSet<string> _saveBlockedDrivers = new();

        private string _currentTrackCode = "";
        private string _currentLayoutName = "";
        private string _currentTrackKey = "";

        public bool HasActiveLapContext { get; private set; } = false;

        private DateTime _driftStartTime;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private DateTime _lastBonusTime = DateTime.MinValue;
        private DateTime _lastDriftTime = DateTime.MinValue;
        private DateTime _lastSpeedingTime = DateTime.MinValue;
        private DateTime _driftSessionStart = DateTime.MinValue;
        private DateTime? _deepDriftSessionStart = null;
        private double _dtSec = 0;
        private bool _previouslyDrifting = false;
        private bool _previouslyDriftSideRight = true;

        private const double DRIFT_ENTRY_EVAL_SEC = 1.5;
        private const double DRIFT_ENTRY_GOOD_DEG = 60.0;
        private const double DRIFT_ENTRY_HIGH_DEG = 70.0;
        private const double DRIFT_ENTRY_EXTREME_DEG = 80.0;
        private const double DRIFT_ENTRY_ULTRAEXTREME_DEG = 90.0;
        private const double DRIFT_ENTRY_BACKWARD_DEG = 100.0;

        private DateTime _driftEntryStartTime = DateTime.MinValue;
        private bool _driftEntryEvaluated = false;

        public event Action<long, double, string, string, string, DriftLabelKind>? DriftScored;
        public event Action? DriftStarted;
        public event Action<long>? DriftEnded;
        public event Action<int>? DonutCompleted;
        public event Action<int>? BurnoutSpinCompleted;
        public event Action? BurnoutToDriftCompleted;

        public double BurnoutActiveSeconds =>
            IsBurnout && _burnoutActiveStartTime != DateTime.MinValue ? (DateTime.UtcNow - _burnoutActiveStartTime).TotalSeconds : 0;

        public double StationaryBurnoutSeconds =>
            _stationaryBurnoutStart != DateTime.MinValue ? (DateTime.UtcNow - _stationaryBurnoutStart).TotalSeconds : 0;
        public event Action<long, long>? LapCompleted;
        public event Action<string>? Error;

        public DriftEngine()
        {
            Directory.CreateDirectory(SavesDir);
            ArchiveLegacySaves();
        }

        private static void ArchiveLegacySaves()
        {
            try
            {
                foreach (string pattern in new[] { "*.json", "*.json.bak", "*.json.tmp" })
                    foreach (string legacy in Directory.GetFiles(SavesDir, pattern))
                        SecureFile.ArchiveLegacy(legacy);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Legacy saves", ex);
            }
        }

        public void SetDriver(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                driverName = "default_driver";

            if (_driverInitialized && _pendingSave && CurrentDriver != driverName)
                SaveCurrentDriver(force: true);

            CurrentDriver = driverName;
            _driverInitialized = true;

            var stats = GetOrCreateDriverData(CurrentDriver).Stats;
            TotalScore = stats.TotalScore;
            BestRunScore = stats.BestRunScore;
            BestDriftDurationMs = stats.BestDriftDurationMs;
            BestDeepDriftDurationMs = stats.BestDeepDriftDurationMs;

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LastLapScore = GetLastLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
        }

        private DriverData GetOrCreateDriverData(string driver)
        {
            if (_driverData.TryGetValue(driver, out var d))
                return d;

            d = LoadDriverFile(driver) ?? new DriverData();

            if (_driverData.Count >= MaxCachedDrivers)
            {
                foreach (var key in _driverData.Keys.Where(k => k != CurrentDriver && k != driver).ToList())
                    _driverData.Remove(key);
            }

            _driverData[driver] = d;
            return d;
        }

        private DriverData? LoadDriverFile(string driver)
        {
            string path = DriverFilePath(driver);
            if (!File.Exists(path)) return null;

            try
            {
                var result = SecureFile.Load<DriverData>(path, SecureFileKind.Scores, DriverIdentity(driver), IsPlausible, out var data);
                if (result.Error != SecureFileError.None)
                {
                    if (result.SaveBlocked) _saveBlockedDrivers.Add(driver);
                    Error?.Invoke(SecureFile.DescribeRejection(SecureFileKind.Scores, driver, result));
                }
                return data;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Driver load", ex);
                Error?.Invoke(string.Format(Localization.T("status.driver_load_error"), driver, ex.Message));
                return null;
            }
        }

        private static Dictionary<string, long> GetOrCreateLayoutMap(
            Dictionary<string, Dictionary<string, long>> trackMap, string track)
        {
            if (!trackMap.TryGetValue(track, out var layoutMap))
            {
                layoutMap = new Dictionary<string, long>();
                trackMap[track] = layoutMap;
            }
            return layoutMap;
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

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LastLapScore = GetLastLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LapScore = 0;
            HasActiveLapContext = false;
        }

        public void OnLapCompleted()
        {

            if (!_lapHadPenalty && LapScore >= CLEAN_LAP_MIN_SCORE)
                AwardFixedBonus(CLEAN_LAP_BONUS_POINTS, Localization.T("bonus.clean_lap"));
            _lapHadPenalty = false;

            LastLapScore = LapScore;
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

        private double _fastPtsRemainder;
        private double _driftPtsRemainder;
        private double _burnoutPtsRemainder;

        int driftTimeMultipler = 0;
        double driftTotalTime= 0;
        int deepAngleCount = 0;
        int eBrakeCount = 0;
        bool eDriftActive = false;

        private bool _outGaugeDataFresh = true;

                public void SetOutGaugeDataFresh(bool isFresh)
        {
            if (isFresh == _outGaugeDataFresh) return;
            _outGaugeDataFresh = isFresh;
            if (!isFresh)
                EndBurnoutIfActive();
        }

        private bool _advancedOutGaugeEnabled = true;

                public void SetAdvancedOutGaugeEnabled(bool enabled)
        {
            if (enabled == _advancedOutGaugeEnabled) return;
            _advancedOutGaugeEnabled = enabled;
            if (!enabled)
                EndBurnoutIfActive();
        }

                private void EndBurnoutIfActive()
        {
            if (IsBurnout)
            {
                DriftEnded?.Invoke(CurrentRunPoints);
                RegisterRunEnd(CurrentRunPoints);
            }
            IsBurnout = false;
            _burnoutConditionStart = DateTime.MinValue;
            _pendingDriftToBurnout = false;
            _pendingBurnoutToDrift = false;
            _lastBurnoutHeadingDeg = null;
            _burnoutSpinAccumDeg = 0;
            _burnoutActiveDuringSpin = false;
            _burnoutSpinComboCount = 0;
            _lastBurnoutSpinBonusTime = DateTime.MinValue;
            _stationaryBurnoutStart = DateTime.MinValue;
            _stationaryBurnoutTierAwarded = 0;
            _burnoutActiveStartTime = DateTime.MinValue;
            _lastTransitionBonusTime = DateTime.MinValue;
        }

                public void ResetCurrentRun() => CancelActiveRun();

        private void CancelActiveRun()
        {
            ResetRunState();
        }

        private void ResetRunState()
        {
            IsDrifting = false;
            IsSpeeding = false;
            IsBurnout = false;
            ComboMultiplier = 1;
            CurrentRunPoints = 0;
            LastAwardedText = "";
            _previouslyDrifting = false;
            _wasDriftingPrev = false;
            _previouslyDriftSideRight = true;

            _lastDriftTime = DateTime.MinValue;
            _lastSpeedingTime = DateTime.MinValue;
            _lastBurnoutTime = DateTime.MinValue;
            _lastBonusTime = DateTime.MinValue;

            _driftStartTime = DateTime.MinValue;
            _driftSessionStart = DateTime.MinValue;
            _deepDriftSessionStart = null;
            _driftEntryStartTime = DateTime.MinValue;
            _driftEntryEvaluated = false;
            driftTimeMultipler = 0;
            driftTotalTime = 0;
            deepAngleCount = 0;
            eBrakeCount = 0;
            eDriftActive = false;

            _burnoutConditionStart = DateTime.MinValue;
            _lastBurnoutHeadingDeg = null;
            _burnoutSpinAccumDeg = 0;
            _burnoutActiveDuringSpin = false;
            _lastSpinActivityTime = DateTime.MinValue;
            _burnoutSpinComboCount = 0;
            _lastBurnoutSpinBonusTime = DateTime.MinValue;
            _stationaryBurnoutStart = DateTime.MinValue;
            _stationaryBurnoutTierAwarded = 0;
            _burnoutActiveStartTime = DateTime.MinValue;

            _donutComboCount = 0;
            _lastDonutBonusTime = DateTime.MinValue;
            _driftSpinAccumDeg = 0;
            _driftSpinLastHeadingDeg = null;
            _driftSpinStartSet = false;
            _driftSpinStartWasBurnout = false;
            _driftSpinSideChanged = false;
            _lastDriftSpinActivityTime = DateTime.MinValue;
            _donutPrevIsBurnout = null;

            _lastTransitionBonusTime = DateTime.MinValue;
            _pendingBurnoutToDrift = false;
            _pendingDriftToBurnout = false;

            _powerSpinAccumDeg = 0;
            _powerSpinLastHeadingDeg = null;
            _powerSpinPending = false;
            _powerSpinComboCount = 0;
            _lastPowerSpinBonusTime = DateTime.MinValue;

            _fastPtsRemainder = 0;
            _driftPtsRemainder = 0;
            _burnoutPtsRemainder = 0;
        }
        private static readonly (int Threshold, double ComboFactor)[] LongDriftMilestones =
        {
            (5, 0.15), (10, 0.25), (15, 0.35), (20, 0.45)
        };

        private static readonly (int Threshold, double ComboFactor)[] DeepAngleMilestones =
        {
            (4, 0.35), (8, 0.45), (12, 0.55), (16, 0.65)
        };

        public void Update(InSim.CompCar car)
        {
            var now = DateTime.UtcNow;

            _dtSec = _lastUpdateTime == DateTime.MinValue ? 0 : (now - _lastUpdateTime).TotalSeconds;
            if (_dtSec > MaxResumeGapSec)
                _dtSec = 0;
            else if (_dtSec > 0.5)
                _dtSec = 0.5;

            _lastUpdateTime = now;

            SpeedKmh = car.SpeedKmh;

            double motionDeg = car.Direction / 65536.0 * 360.0;
            double headingDeg = car.Heading / 65536.0 * 360.0;

            double diff = NormalizeAngleDelta(headingDeg - motionDeg);

            DriftSideRight = diff >= 0;
            DriftAngleDeg = Math.Abs(diff);

            bool motionBlocked = IsMotionBlocked(now);

            bool speeding = !motionBlocked && SpeedKmh >= _speedLevelThresholds[0];

            bool drifting = !motionBlocked
                         && SpeedKmh >= _minDriftSpeedKmh
                         && DriftAngleDeg >= _angleLevelThresholds[0]
                         && DriftAngleDeg <= MAX_DRIFT_ANGLE_DEG;

            bool driftJustStarted = drifting && !_wasDriftingPrev;
            _wasDriftingPrev = drifting;

            if (motionBlocked)
            {
                EndBurnoutIfActive();
            }
            else if (_advancedOutGaugeEnabled && _outGaugeDataFresh)
            {
                double precedingBurnoutSec = _burnoutActiveStartTime != DateTime.MinValue
                    ? (_lastBurnoutTime - _burnoutActiveStartTime).TotalSeconds : 0;

                if (driftJustStarted && _lastBurnoutTime != DateTime.MinValue &&
                    (now - _lastBurnoutTime).TotalSeconds <= TRANSITION_BONUS_GRACE_SEC &&
                    precedingBurnoutSec >= TRANSITION_MIN_ACTIVITY_SEC)
                {
                    _pendingBurnoutToDrift = true;
                    _pendingBurnoutToDriftStart = now;
                }

                if (_pendingBurnoutToDrift)
                {
                    if (!drifting)
                    {
                        _pendingBurnoutToDrift = false;
                    }
                    else if ((now - _pendingBurnoutToDriftStart).TotalSeconds >= TRANSITION_MIN_ACTIVITY_SEC)
                    {
                        _pendingBurnoutToDrift = false;
                        BurnoutToDriftCompleted?.Invoke();
                        if ((now - _lastTransitionBonusTime).TotalSeconds >= TRANSITION_BONUS_COOLDOWN_SEC)
                        {
                            _lastTransitionBonusTime = now;
                            AwardMilestoneBonus(Localization.T("bonus.burnout_to_drift"));
                        }
                    }
                }

                UpdateBurnout(now, drifting, speeding, headingDeg);
                UpdateStationaryBurnoutBonus(now);
            }

            UpdateDonutTracking(now, drifting, IsBurnout, headingDeg, car.X / 65536f, car.Y / 65536f);
            UpdatePowerSpinTracking(now, drifting, headingDeg);

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
                bool speedingEnded = IsSpeeding && !_previouslyDrifting;
                IsSpeeding = false;

                if (speedingEnded)
                {
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                }

                double gap = (now - _lastDriftTime).TotalSeconds;
                double gap2 = (now - _lastSpeedingTime).TotalSeconds;
                double gap3 = (now - _lastBurnoutTime).TotalSeconds;
                bool comboStillFresh = gap <= COMBO_TIMEOUT_SEC || gap2 <= COMBO_TIMEOUT_SEC || gap3 <= COMBO_TIMEOUT_SEC;

                if (!comboStillFresh)
                {
                    CurrentRunPoints = 0;
                    ComboMultiplier = 1;
                    driftTotalTime = 0;
                    _burnoutSpinComboCount = 0;
                    _donutComboCount = 0;
                    eBrakeCount--;
                    if (eBrakeCount <= 0)
                    {
                        eDriftActive = false;
                        eBrakeCount = 0;
                    }
                }
            }

            if (!drifting && speeding)
            {

                int speedLevel = GetFastDriveLevel(SpeedKmh);
                double comboGainPerSec = speedLevel * FAST_DRIVE_COMBO_STEP_PER_SEC;
                ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + comboGainPerSec * _dtSec, 10.0), 2);

                double fastRaw = FAST_DRIVE_POINTS * _dtSec * ComboMultiplier + _fastPtsRemainder;
                long fastPts = (long)fastRaw;
                _fastPtsRemainder = fastRaw - fastPts;

                CurrentRunPoints += fastPts;
                TotalScore += fastPts;
                IsSpeeding = true;
                LapScore += fastPts;

                _lastSpeedingTime = now;

                SaveCurrentDriver();

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

                if (!_previouslyDrifting)
                {
                    _driftStartTime = now;
                    _driftSessionStart = now;
                    driftTimeMultipler = 0;

                    if (!_isHandBrakeON) { eBrakeCount--; }
                    eDriftActive = false;

                    IsDrifting = true;

                    _driftEntryStartTime = now;
                    _driftEntryEvaluated = false;
                    double gap = (now - _lastDriftTime).TotalSeconds;
                    double bonusgap = (now - _lastBonusTime).TotalSeconds;

                    if (gap <= COMBO_TIMEOUT_SEC && _lastDriftTime != DateTime.MinValue)
                    {
                        if (_previouslyDriftSideRight != DriftSideRight)
                        {
                            double bonusScore = 100;

                            if (bonusgap >= BONUS_TIMEOUT_SEC && gap <= (COMBO_TIMEOUT_SEC / 4))
                            {
                                if (SpeedKmh >= 35 && DriftAngleDeg >= _angleLevelThresholds[0] && !motionBlocked)
                                {

                                    if (!_isHandBrakeON) { eBrakeCount = 0; }

                                    var labelKindT = DriftLabelKind.TransitionDrift;
                                    string bonusText = Localization.T(LabelKindToKey(labelKindT));
                                    _lastBonusTime = now;
                                    ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.11 * speedScore), 10.0), 2);
                                    bonusScore = Math.Round(bonusScore, 0) * Math.Round(ComboMultiplier, 2);

                                    long bonusPts = (long)Math.Round(bonusScore, 0);
                                    CurrentRunPoints += bonusPts;
                                    TotalScore += bonusPts;
                                    LapScore += bonusPts;
                                    LastAwardedText = $"{bonusText} + {bonusPts}";

                                }

                            }

                            _previouslyDriftSideRight = DriftSideRight;
                        }

                    }

                    _previouslyDriftSideRight = DriftSideRight;

                    DriftStarted?.Invoke();
                }

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
                    _driftStartTime = driftTime >= 1000 ? now : _driftStartTime.AddMilliseconds(500);

                    foreach (var (threshold, comboFactor) in LongDriftMilestones)
                    {
                        if (driftTimeMultipler != threshold) continue;
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (comboFactor * angleScore), 10.0), 2);
                        break;
                    }

                    labelKindT = DriftLabelKind.DeepLongDrift;
                    bonusText = Localization.T(LabelKindToKey(labelKindT));

                    if (DriftAngleDeg >= 55)
                    {
                        deepAngleCount++;
                    }
                    else deepAngleCount = 0;

                    foreach (var (threshold, comboFactor) in DeepAngleMilestones)
                    {
                        if (deepAngleCount != threshold) continue;
                        if (motionBlocked) break;
                        long bonus = 50 * deepAngleCount;
                        TotalScore += bonus;
                        LapScore += bonus;
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {bonus}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (comboFactor * angleScore), 10.0), 2);
                        break;
                    }
                }

                if (eBrakeCount >= 1) { eDriftActive = true; }

                double framePts = POINTS_PER_SECOND
                                * (ComboMultiplier
                                * 0.25f)
                                * (angleScore * 1.5f)
                                * speedScore
                                * _dtSec;

                double driftRaw = framePts + _driftPtsRemainder;
                long driftPts = (long)driftRaw;
                _driftPtsRemainder = driftRaw - driftPts;

                CurrentRunPoints += driftPts;
                TotalScore += driftPts;
                LapScore += driftPts;
                SaveCurrentDriver();

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

                _driftEntryStartTime = DateTime.MinValue;
                _driftEntryEvaluated = false;

                SaveCurrentDriver();

                if (!speeding)
                {
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                    IsSpeeding = false;
                }
            }

            if (!drifting && _previouslyDrifting)
            {
                _lastDriftTime = now;
            }

            _previouslyDrifting = drifting;
        }

        private void SaveDriverFile(string driver, DriverData data)
        {
            if (_saveBlockedDrivers.Contains(driver)) return;

            try
            {
                SecureFile.Write(DriverFilePath(driver), SecureFileKind.Scores, DriverIdentity(driver), data);
            }
            catch (Exception ex)
            {
                Error?.Invoke(string.Format(Localization.T("status.driver_save_error"), driver, ex.Message));
            }
        }

        private long GetLastLapForDriver(string driver, string track, string layout)
        {
            string layoutKey = string.IsNullOrWhiteSpace(layout) ? "default" : layout;

            if (_driverData.TryGetValue(driver, out var d) &&
                d.LastLapRecords.TryGetValue(track, out var layoutMap) &&
                layoutMap.TryGetValue(layoutKey, out var last))
                return last;

            return 0;
        }

        private void SaveLastLapRecord()
        {
            string layoutKey = string.IsNullOrWhiteSpace(_currentLayoutName) ? "default" : _currentLayoutName;
            var trackMap = GetOrCreateDriverData(CurrentDriver).LastLapRecords;
            var layoutMap = GetOrCreateLayoutMap(trackMap, _currentTrackCode);

            layoutMap[layoutKey] = LastLapScore;
            SaveDriverDataRaw();
        }

        private long GetBestLapForDriver(string driver, string track, string layout)
        {
            string layoutKey = string.IsNullOrWhiteSpace(layout) ? "default" : layout;

            if (_driverData.TryGetValue(driver, out var d) &&
                d.LapRecords.TryGetValue(track, out var layoutMap) &&
                layoutMap.TryGetValue(layoutKey, out var best))
                return best;

            return 0;
        }

        private void SaveLapRecords()
        {
            string layoutKey = string.IsNullOrWhiteSpace(_currentLayoutName) ? "default" : _currentLayoutName;
            var trackMap = GetOrCreateDriverData(CurrentDriver).LapRecords;
            var layoutMap = GetOrCreateLayoutMap(trackMap, _currentTrackCode);

            layoutMap[layoutKey] = BestLapScore;
            SaveDriverDataRaw();
        }

        private DateTime _lastStatsSaveUtc = DateTime.MinValue;
        private bool _pendingSave;
        private bool _driverInitialized;
        private static readonly TimeSpan StatsSaveInterval = TimeSpan.FromSeconds(2);

        private void SaveDriverDataRaw(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastStatsSaveUtc) < StatsSaveInterval)
            {
                _pendingSave = true;
                return;
            }

            _lastStatsSaveUtc = DateTime.UtcNow;
            _pendingSave = false;

            try
            {
                Directory.CreateDirectory(SavesDir);
                var driverData = GetOrCreateDriverData(CurrentDriver);
                if (IsEmptyDriverData(driverData) && !File.Exists(DriverFilePath(CurrentDriver)))
                    return;

                SaveDriverFile(CurrentDriver, driverData);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Driver save", ex);
            }
        }

        private void SaveCurrentDriver(bool force = false)
        {
            var stats = GetOrCreateDriverData(CurrentDriver).Stats;
            stats.TotalScore = TotalScore;
            stats.BestRunScore = BestRunScore;
            stats.BestDriftDurationMs = BestDriftDurationMs;
            stats.BestDeepDriftDurationMs = BestDeepDriftDurationMs;

            SaveDriverDataRaw(force);
        }

                public void FlushStats() => SaveCurrentDriver(force: true);

        private void RegisterRunEnd(long runPoints)
        {
            if (runPoints > BestRunScore)
            {
                BestRunScore = runPoints;
            }

            SaveCurrentDriver(force: true);
        }

                public void ApplyPostPoints(long delta, string awardLabel)
        {
            if (delta > 0 && IsMotionBlocked(DateTime.UtcNow)) return;

            CurrentRunPoints += delta;
            TotalScore += delta;
            LapScore += delta;
            if (ComboMultiplier > 0) ComboMultiplier += 0.1;
            LastAwardedText = awardLabel;
            SaveCurrentDriver();
        }

        public void ApplyCollisionPoints(long delta, string awardLabel)
        {
            if (delta > 0 && IsMotionBlocked(DateTime.UtcNow)) return;

            CurrentRunPoints += delta;
            TotalScore += delta;
            LapScore += delta;
            LastAwardedText = awardLabel;
            SaveCurrentDriver();
        }

        private long _motionBlockedUntilTicks;

        public void BlockMotionScoring(double holdSec)
        {
            long until = DateTime.UtcNow.AddSeconds(Math.Max(0, holdSec)).Ticks;
            long current = Interlocked.Read(ref _motionBlockedUntilTicks);
            while (until > current)
            {
                long seen = Interlocked.CompareExchange(ref _motionBlockedUntilTicks, until, current);
                if (seen == current) break;
                current = seen;
            }
        }

        public void ClearMotionBlock() => Interlocked.Exchange(ref _motionBlockedUntilTicks, 0);

        public bool IsMotionBlocked(DateTime utcNow) => utcNow.Ticks < Interlocked.Read(ref _motionBlockedUntilTicks);

        private void AwardMilestoneBonus(string label)
        {
            long pts = (long)Math.Round(MILESTONE_BONUS_POINTS * ComboMultiplier);
            _lastBonusTime = DateTime.UtcNow;
            ApplyPostPoints(pts, $"{label} +{pts}");
        }

        private void AwardStreakBonus(string baseLabel, ref int comboCount, ref DateTime lastBonusTime, DateTime now)
        {
            if ((now - lastBonusTime).TotalSeconds > SPIN_COMBO_GAP_SEC)
                comboCount = 0;
            comboCount++;
            lastBonusTime = now;

            string label = baseLabel + (comboCount > 1 ? $" x{comboCount}" : "");
            AwardMilestoneBonus(label);
        }

        private void AwardFixedBonus(long points, string label)
        {
            _lastBonusTime = DateTime.UtcNow;
            ApplyPostPoints(points, $"{label} +{points}");
        }

                public void RegisterCollisionPenalty() => _lapHadPenalty = true;

                private const double UNSTOPPABLE_COOLDOWN_SEC = 0.5;
        private DateTime _lastUnstoppableBonusTime = DateTime.MinValue;
        public void AwardUnstoppableBonus()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUnstoppableBonusTime).TotalSeconds < UNSTOPPABLE_COOLDOWN_SEC) return;
            _lastUnstoppableBonusTime = now;
            AwardMilestoneBonus(Localization.T("bonus.unstoppable"));
        }

        private void EvaluateDriftEntry(double angleAtEval)
        {
            DriftLabelKind kind;
            long bonus;

            if (angleAtEval > DRIFT_ENTRY_BACKWARD_DEG) { kind = DriftLabelKind.EntryBackward; bonus = 1100; }
            else if (angleAtEval > DRIFT_ENTRY_ULTRAEXTREME_DEG) { kind = DriftLabelKind.EntryUltraExtreme; bonus = 750; }
            else if (angleAtEval > DRIFT_ENTRY_EXTREME_DEG) { kind = DriftLabelKind.EntryExtreme; bonus = 500; }
            else if (angleAtEval > DRIFT_ENTRY_HIGH_DEG) { kind = DriftLabelKind.EntryHigh; bonus = 300; }
            else if (angleAtEval > DRIFT_ENTRY_GOOD_DEG) { kind = DriftLabelKind.EntryGood; bonus = 150; }
            else return;

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

        private static double NormalizeAngleDelta(double delta)
        {
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;
            return delta;
        }

        private double AngleScore(double angle)
        {
            angle = Math.Clamp(angle, _angleLevelThresholds[0], MAX_DRIFT_ANGLE_DEG);

            return 0.00001 +
                   ((angle - _angleLevelThresholds[0]) /
                   Math.Max(1.0, MAX_DRIFT_ANGLE_DEG - _angleLevelThresholds[0])) * 2.99;
        }

        private static double SpeedScore(double kmh)
        {

            kmh = Math.Clamp(kmh, 1, 100);

            return 0.001 +
                   ((kmh - 1) /
                   (100 - 1)) * 1.99;
        }

        private int GetDriftIndicatorLevel(double angle)
        {
            if (angle > 90) return 6;
            if (angle > _angleLevelThresholds[4]) return 5;
            if (angle > _angleLevelThresholds[3]) return 4;
            if (angle > _angleLevelThresholds[2]) return 3;
            if (angle > _angleLevelThresholds[1]) return 2;
            if (angle > _angleLevelThresholds[0]) return 1;
            return 0;
        }

        private static readonly string[] IndicatorTextRight = { "", " >", " >>", " >>>", " >>>>", " >>>>>", " ???" };
        private static readonly string[] IndicatorTextLeft = { "", "< ", "<< ", "<<< ", "<<<< ", "<<<<< ", "??? " };
        private static readonly string[] IndicatorBlank = { "", "  ", "   ", "    ", "     ", "      ", "    " };

        private string GetDriftValueINDR(double angle, bool side)
        {
            int level = GetDriftIndicatorLevel(angle);
            return side ? IndicatorTextRight[level] : IndicatorBlank[level];
        }

        private string GetDriftValueINDL(double angle, bool side)
        {
            int level = GetDriftIndicatorLevel(angle);
            return !side ? IndicatorTextLeft[level] : IndicatorBlank[level];
        }
        public enum DriftLabelKind
        {
            Generic,
            GenericE,
            Fast1,
            Fast2,
            Fast3,
            Fast4,
            Fast5,
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
            if (angle < 8)
            {
                if (speed >= _speedLevelThresholds[4]) return DriftLabelKind.Fast5;
                if (speed >= _speedLevelThresholds[3]) return DriftLabelKind.Fast4;
                if (speed >= _speedLevelThresholds[2]) return DriftLabelKind.Fast3;
                if (speed >= _speedLevelThresholds[1]) return DriftLabelKind.Fast2;
                if (speed >= _speedLevelThresholds[0]) return DriftLabelKind.Fast1;
            }
            if (!brake)
            {
                if (angle > _backwardDriftAngleThreshold && speed > 30) return DriftLabelKind.AngleBackward;
                if (angle > _angleLevelThresholds[4]) return DriftLabelKind.AngleUltraExtreme;
                if (angle > _angleLevelThresholds[3]) return DriftLabelKind.AngleExtreme;
                if (angle > _angleLevelThresholds[2]) return DriftLabelKind.AngleHigh;
                if (angle > _angleLevelThresholds[1]) return DriftLabelKind.AngleGood;
                if (angle < _angleLevelThresholds[1] && angle > _angleLevelThresholds[0]) return DriftLabelKind.Generic;
            }
            else
            {
                if (angle > _backwardDriftAngleThreshold && speed > 30) return DriftLabelKind.AngleBackwardE;
                if (angle > _angleLevelThresholds[4]) return DriftLabelKind.AngleUltraExtremeE;
                if (angle > _angleLevelThresholds[3]) return DriftLabelKind.AngleExtremeE;
                if (angle > _angleLevelThresholds[2]) return DriftLabelKind.AngleHighE;
                if (angle > _angleLevelThresholds[1]) return DriftLabelKind.AngleGoodE;
                if (angle < _angleLevelThresholds[1] && angle > _angleLevelThresholds[0]) return DriftLabelKind.GenericE;
            }

            if (speed > 120 && angle > _angleLevelThresholds[0]) return DriftLabelKind.FastDrift;

            return DriftLabelKind.Generic;
        }

        public static string LabelKindToKey(DriftLabelKind kind) => kind switch
        {
            DriftLabelKind.Fast1 => "drift.label.fast1",
            DriftLabelKind.Fast2 => "drift.label.fast2",
            DriftLabelKind.Fast3 => "drift.label.fast3",
            DriftLabelKind.Fast4 => "drift.label.fast4",
            DriftLabelKind.Fast5 => "drift.label.fast5",
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

                public static int GetColorTier(DriftLabelKind kind) => kind switch
        {
            DriftLabelKind.AngleHigh => 3,
            DriftLabelKind.AngleExtreme => 4,
            DriftLabelKind.AngleBackward => 6,
            DriftLabelKind.AngleUltraExtreme => 5,
            DriftLabelKind.AngleHighE => 3,
            DriftLabelKind.AngleExtremeE => 4,
            DriftLabelKind.AngleBackwardE => 6,
            DriftLabelKind.AngleUltraExtremeE => 5,
            DriftLabelKind.Fast1 => 1,
            DriftLabelKind.Fast2 => 2,
            DriftLabelKind.Fast3 => 3,
            DriftLabelKind.Fast4 => 4,
            DriftLabelKind.Fast5 => 5,
            DriftLabelKind.AngleGood => 2,
            DriftLabelKind.AngleGoodE => 2,
            DriftLabelKind.BurnoutGood => 1,
            DriftLabelKind.BurnoutHigh => 2,
            DriftLabelKind.BurnoutExtreme => 3,
            DriftLabelKind.BurnoutInsane => 4,
            _ => 1,
        };

        private double _maxBurnoutSpeedKmh = 20.0;
        public double MaxBurnoutSpeedKmh => _maxBurnoutSpeedKmh;
        public void SetMaxBurnoutSpeedKmh(double kmh)
        {
            if (kmh >= 0) _maxBurnoutSpeedKmh = kmh;
        }

        private const float BURNOUT_MIN_RPM = 3000f;

        private const float BURNOUT_MIN_THROTTLE = 0.45f;

        private const double BURNOUT_ACTIVATE_SEC = 0.5;

        private const double BURNOUT_POINTS_PER_SECOND = 5.0;

        private const long MILESTONE_BONUS_POINTS = 100;

        private const long CLEAN_LAP_BONUS_POINTS = 250;
        private const long CLEAN_LAP_MIN_SCORE = 1500;
        private bool _lapHadPenalty = false;

        private const double STATIONARY_BURNOUT_MAX_SPEED_KMH = 1.5;
        private static readonly double[] STATIONARY_BURNOUT_TIERS_SEC = { 5, 10, 15, 20 };

        private const double DONUT_RADIUS_METERS = 20.0;
        private const double DRIFT_SPIN_ABANDON_SEC = 1.0;

        private const double POWER_SPIN_MIN_SPEED_KMH = 50.0;
        private const double POWER_SPIN_VERIFY_DELAY_SEC = 2.0;
        private const double POWER_SPIN_MAX_SPEED_DROP_KMH = 15.0;

        private const double TRANSITION_BONUS_GRACE_SEC = 1.0;

        private const double TRANSITION_BONUS_COOLDOWN_SEC = 2.0;
        private DateTime _lastTransitionBonusTime = DateTime.MinValue;

        private const double TRANSITION_MIN_ACTIVITY_SEC = 2.0;
        private DateTime _burnoutActiveStartTime = DateTime.MinValue;

        private bool _pendingBurnoutToDrift = false;
        private DateTime _pendingBurnoutToDriftStart = DateTime.MinValue;
        private bool _pendingDriftToBurnout = false;
        private DateTime _pendingDriftToBurnoutStart = DateTime.MinValue;

        public bool IsBurnout { get; private set; }

        private float _lastRpm = 0f;
        private float _lastThrottle = 0f;
        private int _lastGear = 0;
        private double _lastDashSpeedKmh = 0;
        private DateTime _lastDashSpeedUpdateTime = DateTime.MinValue;
        private const double DASH_SPEED_MAX_AGE_SEC = 0.3;

        private const double BURNOUT_SLIP_WEIGHT = 0.38;
        private const double BURNOUT_DURATION_WEIGHT = 2.0;

        private DateTime _burnoutConditionStart = DateTime.MinValue;

        private DateTime _lastBurnoutTime = DateTime.MinValue;

        private double? _lastBurnoutHeadingDeg = null;
        private double _burnoutSpinAccumDeg = 0;
        private DateTime _lastSpinActivityTime = DateTime.MinValue;

        private const double BURNOUT_SPIN_MIN_YAW_RATE_DEG_PER_SEC = 85.0;

        private const double BURNOUT_SPIN_ABANDON_SEC = 1.0;

        private bool _burnoutActiveDuringSpin = false;

        private const double SPIN_COMBO_GAP_SEC = 7.5;
        private int _burnoutSpinComboCount = 0;
        private DateTime _lastBurnoutSpinBonusTime = DateTime.MinValue;
        private int _donutComboCount = 0;
        private DateTime _lastDonutBonusTime = DateTime.MinValue;

        private DateTime _stationaryBurnoutStart = DateTime.MinValue;
        private int _stationaryBurnoutTierAwarded = 0;

        private double? _driftSpinLastHeadingDeg = null;
        private double _driftSpinAccumDeg = 0;
        private DateTime _lastDriftSpinActivityTime = DateTime.MinValue;
        private float _driftSpinStartX, _driftSpinStartY;
        private bool _driftSpinStartSet = false;
        private bool _driftSpinStartWasBurnout = false;
        private bool _driftSpinStartSideRight = true;
        private bool _driftSpinSideChanged = false;
        private bool? _donutPrevIsBurnout = null;

        private double _powerSpinAccumDeg = 0;
        private double? _powerSpinLastHeadingDeg = null;
        private double _powerSpinSegmentStartSpeed = 0;

        private bool _powerSpinPending = false;
        private DateTime _powerSpinPendingCheckTime = DateTime.MinValue;
        private double _powerSpinPendingStartSpeed = 0;
        private int _powerSpinComboCount = 0;
        private DateTime _lastPowerSpinBonusTime = DateTime.MinValue;

        private bool _wasDriftingPrev = false;

                public void UpdateEngineTelemetry(float rpm, float throttle, int gear, double dashSpeedKmh = 0)
        {
            _lastRpm = Math.Max(0, rpm);
            _lastThrottle = Math.Clamp(throttle, 0f, 1f);
            _lastGear = gear;
            _lastDashSpeedKmh = Math.Max(0, dashSpeedKmh);
            _lastDashSpeedUpdateTime = DateTime.UtcNow;
        }

        private void UpdateBurnout(DateTime now, bool drifting, bool speeding, double headingDeg)
        {

            if (_lastBurnoutHeadingDeg.HasValue && _dtSec > 0.0001)
            {
                double spinDelta = NormalizeAngleDelta(headingDeg - _lastBurnoutHeadingDeg.Value);

                double yawRateDegPerSec = Math.Abs(spinDelta) / _dtSec;

                if (yawRateDegPerSec >= BURNOUT_SPIN_MIN_YAW_RATE_DEG_PER_SEC)
                {
                    _burnoutSpinAccumDeg += Math.Abs(spinDelta);
                    _burnoutActiveDuringSpin = _burnoutActiveDuringSpin || IsBurnout;
                    _lastSpinActivityTime = now;
                }
                else if ((now - _lastSpinActivityTime).TotalSeconds > BURNOUT_SPIN_ABANDON_SEC)
                {

                    _burnoutSpinAccumDeg = 0;
                    _burnoutActiveDuringSpin = false;
                }

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
                    AwardStreakBonus(Localization.T("drift.label.burnout_spin"),
                        ref _burnoutSpinComboCount, ref _lastBurnoutSpinBonusTime, now);
                    BurnoutSpinCompleted?.Invoke(_burnoutSpinComboCount);
                }

                _burnoutActiveDuringSpin = false;
            }

            bool conditionNow = SpeedKmh < _maxBurnoutSpeedKmh
                              && _lastGear >= 2
                              && _lastThrottle >= BURNOUT_MIN_THROTTLE
                              && _lastRpm >= BURNOUT_MIN_RPM;

            if (!conditionNow)
            {
                if (IsBurnout)
                {

                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                }
                _burnoutConditionStart = DateTime.MinValue;
                IsBurnout = false;
                _pendingDriftToBurnout = false;

                return;
            }

            if (_burnoutConditionStart == DateTime.MinValue)
                _burnoutConditionStart = now;

            double heldSec = (now - _burnoutConditionStart).TotalSeconds;

            if (heldSec < BURNOUT_ACTIVATE_SEC)
                return;

            bool justActivated = !IsBurnout;
            IsBurnout = true;

            if (justActivated)
            {
                _burnoutActiveStartTime = now;
                DriftStarted?.Invoke();

                double precedingDriftSec = _driftEntryStartTime != DateTime.MinValue
                    ? (_lastDriftTime - _driftEntryStartTime).TotalSeconds : 0;

                if (_lastDriftTime != DateTime.MinValue &&
                    (now - _lastDriftTime).TotalSeconds <= TRANSITION_BONUS_GRACE_SEC &&
                    precedingDriftSec >= TRANSITION_MIN_ACTIVITY_SEC)
                {
                    _pendingDriftToBurnout = true;
                    _pendingDriftToBurnoutStart = now;
                }
            }

            if (_pendingDriftToBurnout &&
                (now - _pendingDriftToBurnoutStart).TotalSeconds >= TRANSITION_MIN_ACTIVITY_SEC)
            {
                _pendingDriftToBurnout = false;
                if ((now - _lastTransitionBonusTime).TotalSeconds >= TRANSITION_BONUS_COOLDOWN_SEC)
                {
                    _lastTransitionBonusTime = now;
                    AwardMilestoneBonus(Localization.T("bonus.drift_to_burnout"));
                }
            }

            double rpmFactor = Math.Clamp(_lastRpm / (BURNOUT_MIN_RPM ), 1.0, 5.0);
            ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.01 * rpmFactor * _dtSec), 10.0), 2);

            double burnoutRaw = BURNOUT_POINTS_PER_SECOND * ComboMultiplier * _dtSec * (_lastGear / 2.0) + _burnoutPtsRemainder;
            long framePts = (long)burnoutRaw;
            _burnoutPtsRemainder = burnoutRaw - framePts;

            CurrentRunPoints += framePts;
            TotalScore += framePts;
            LapScore += framePts;
            _lastBurnoutTime = now;
            SaveCurrentDriver();

            bool dashSpeedFresh = (now - _lastDashSpeedUpdateTime).TotalSeconds <= DASH_SPEED_MAX_AGE_SEC;
            double wheelSlipKmh = dashSpeedFresh ? Math.Max(0, _lastDashSpeedKmh - SpeedKmh) : 0;
            var burnoutKind = GetBurnoutLabelKind(wheelSlipKmh, heldSec);
            DriftScored?.Invoke(
                CurrentRunPoints,
                ComboMultiplier,
                Localization.T(LabelKindToKey(burnoutKind)),
                "",
                "",
                burnoutKind
            );
        }

        private void UpdateStationaryBurnoutBonus(DateTime now)
        {
            bool stationary = IsBurnout && SpeedKmh < STATIONARY_BURNOUT_MAX_SPEED_KMH;
            if (!stationary)
            {
                _stationaryBurnoutStart = DateTime.MinValue;
                _stationaryBurnoutTierAwarded = 0;
                return;
            }

            if (_stationaryBurnoutStart == DateTime.MinValue)
                _stationaryBurnoutStart = now;

            double heldSec = (now - _stationaryBurnoutStart).TotalSeconds;
            while (_stationaryBurnoutTierAwarded < STATIONARY_BURNOUT_TIERS_SEC.Length &&
                   heldSec >= STATIONARY_BURNOUT_TIERS_SEC[_stationaryBurnoutTierAwarded])
            {
                int tierSec = (int)STATIONARY_BURNOUT_TIERS_SEC[_stationaryBurnoutTierAwarded];
                _stationaryBurnoutTierAwarded++;
                AwardMilestoneBonus($"{Localization.T("bonus.stationary_burnout")} {tierSec}s");
            }
        }

        private void UpdatePowerSpinTracking(DateTime now, bool drifting, double headingDeg)
        {
            if (_powerSpinPending && now >= _powerSpinPendingCheckTime)
            {
                _powerSpinPending = false;
                if (SpeedKmh >= _powerSpinPendingStartSpeed - POWER_SPIN_MAX_SPEED_DROP_KMH)
                {
                    AwardStreakBonus(Localization.T("bonus.power_spin"),
                        ref _powerSpinComboCount, ref _lastPowerSpinBonusTime, now);
                }
            }

            bool eligible = drifting && SpeedKmh > POWER_SPIN_MIN_SPEED_KMH;
            if (!eligible)
            {
                _powerSpinAccumDeg = 0;
                _powerSpinLastHeadingDeg = null;
                return;
            }

            if (_powerSpinLastHeadingDeg == null)
                _powerSpinSegmentStartSpeed = SpeedKmh;
            else
                _powerSpinAccumDeg += Math.Abs(NormalizeAngleDelta(headingDeg - _powerSpinLastHeadingDeg.Value));

            _powerSpinLastHeadingDeg = headingDeg;

            while (_powerSpinAccumDeg >= 360.0)
            {
                _powerSpinAccumDeg -= 360.0;

                if (_powerSpinPending)
                {
                    _powerSpinPending = false;
                    if (SpeedKmh >= _powerSpinPendingStartSpeed - POWER_SPIN_MAX_SPEED_DROP_KMH)
                    {
                        AwardStreakBonus(Localization.T("bonus.power_spin"),
                            ref _powerSpinComboCount, ref _lastPowerSpinBonusTime, now);
                    }
                }

                _powerSpinPending = true;
                _powerSpinPendingCheckTime = now.AddSeconds(POWER_SPIN_VERIFY_DELAY_SEC);
                _powerSpinPendingStartSpeed = _powerSpinSegmentStartSpeed;

                _powerSpinSegmentStartSpeed = SpeedKmh;
            }
        }

        private void UpdateDonutTracking(DateTime now, bool drifting, bool isBurnout, double headingDeg, float carXMeters, float carYMeters)
        {
            bool spinning = drifting || isBurnout;
            if (!spinning)
            {
                if ((now - _lastDriftSpinActivityTime).TotalSeconds > DRIFT_SPIN_ABANDON_SEC)
                {
                    _driftSpinAccumDeg = 0;
                    _driftSpinLastHeadingDeg = null;
                    _driftSpinStartSet = false;
                }
                _donutPrevIsBurnout = null;
                return;
            }

            if (_donutPrevIsBurnout.HasValue && _donutPrevIsBurnout.Value != isBurnout)
            {
                _driftSpinAccumDeg = 0;
                _driftSpinLastHeadingDeg = null;
                _driftSpinStartSet = false;
            }
            _donutPrevIsBurnout = isBurnout;

            if (!_driftSpinStartSet)
            {
                _driftSpinStartX = carXMeters;
                _driftSpinStartY = carYMeters;
                _driftSpinStartSet = true;
                _driftSpinStartWasBurnout = isBurnout;
                _driftSpinStartSideRight = DriftSideRight;
                _driftSpinSideChanged = false;
            }
            else if (DriftSideRight != _driftSpinStartSideRight)
            {

                _driftSpinSideChanged = true;
            }

            if (_driftSpinLastHeadingDeg.HasValue)
            {
                double delta = NormalizeAngleDelta(headingDeg - _driftSpinLastHeadingDeg.Value);
                _driftSpinAccumDeg += Math.Abs(delta);
            }
            _driftSpinLastHeadingDeg = headingDeg;
            _lastDriftSpinActivityTime = now;

            while (_driftSpinAccumDeg >= 360.0)
            {
                _driftSpinAccumDeg -= 360.0;

                double dx = carXMeters - _driftSpinStartX;
                double dy = carYMeters - _driftSpinStartY;
                bool positionOk = Math.Sqrt(dx * dx + dy * dy) <= DONUT_RADIUS_METERS;
                bool crossedState = !(_driftSpinStartWasBurnout && isBurnout);

                if (positionOk && crossedState && !_driftSpinSideChanged)
                {
                    AwardStreakBonus(Localization.T("bonus.donut"),
                        ref _donutComboCount, ref _lastDonutBonusTime, now);
                    DonutCompleted?.Invoke(_donutComboCount);
                }

                _driftSpinStartWasBurnout = isBurnout;
                _driftSpinStartSideRight = DriftSideRight;
                _driftSpinSideChanged = false;

                _driftSpinStartX = carXMeters;
                _driftSpinStartY = carYMeters;
            }
        }

        private DriftLabelKind GetBurnoutLabelKind(double wheelSlipKmh, double heldSec)
        {
            double severity = wheelSlipKmh * BURNOUT_SLIP_WEIGHT + (heldSec - BURNOUT_ACTIVATE_SEC) * BURNOUT_DURATION_WEIGHT;

            if (severity >= 80) return DriftLabelKind.BurnoutInsane;
            if (severity >= 65) return DriftLabelKind.BurnoutExtreme;
            if (severity >= 50) return DriftLabelKind.BurnoutHigh;
            return DriftLabelKind.BurnoutGood;
        }

        public void ResetTotal()
        {
            ResetRunState();

            TotalScore = 0;
            LapScore = 0;
            _lapHadPenalty = false;

            BestRunScore = 0;
            BestDriftDurationMs = 0;
            BestDeepDriftDurationMs = 0;

            SaveCurrentDriver();
        }
        public void ResetLapScore()
        {
            LapScore = 0;
        }

                public bool ActivateLapCounting()
        {
            if (HasActiveLapContext) return false;

            LapScore = 0;
            HasActiveLapContext = true;
            return true;
        }

                public void DeactivateLapCounting()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }

                public void OnRaceRestarted()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }

    }
}