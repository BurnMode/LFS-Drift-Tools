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

    // Everything persisted for one driver — stats + best/last lap per track/layout — as a
    // single unit, so the whole driver record lives in one place in driver_data.json.
    public class DriverData
    {
        public DriverStats Stats { get; set; } = new();
        public Dictionary<string, Dictionary<string, long>> LapRecords { get; set; } = new();      // track -> layout -> best
        public Dictionary<string, Dictionary<string, long>> LastLapRecords { get; set; } = new();  // track -> layout -> last
    }


    /// InSim Direction/Heading: word, 0 = world Y axis, 32768 = 180 degrees.
    /// Drift angle = angular difference between motion direction and car heading.

    public class DriftEngine
    {
        // ── Drift scoring ───────────────────────────────────────
        private const double MIN_SPEED_KMH = 20.0;
        private const double MIN_DRIFT_ANGLE_DEG = 25.0;
        private const double MAX_DRIFT_ANGLE_DEG = 150.0;
        public const double COMBO_TIMEOUT_SEC = 2.5;
        private const double BONUS_TIMEOUT_SEC = 2.5;
        private const double POINTS_PER_SECOND = 400.0;

        // ── Fast drive scoring ──────────────────────────────────
        private const double FAST_DRIVE_THRESHOLD = 100.0;
        private const double FAST_DRIVE_POINTS = 8.0;

        // ComboMultiplier gain PER SECOND for "fast driving" level 1 (see GetFastDriveLevel) —
        // each next level adds the same extra amount again (level 1 = 0.025/s, level 2 =
        // 0.05/s, ..., level 5 = 0.125/s).
        private const double FAST_DRIVE_COMBO_STEP_PER_SEC = 0.01;

        /// <summary>"Fast driving" level (1-5) based on speed relative to FAST_DRIVE_THRESHOLD —
        /// the same thresholds that used to set ComboMultiplier directly now only scale its
        /// per-second growth rate (see Update()). Assumes speedKmh >= FAST_DRIVE_THRESHOLD
        /// (guaranteed by the caller's `speeding` condition), otherwise always returns level 1.</summary>
        private static int GetFastDriveLevel(double speedKmh)
        {
            if (speedKmh >= FAST_DRIVE_THRESHOLD * 2.2) return 5;
            if (speedKmh >= FAST_DRIVE_THRESHOLD * 1.9) return 4;
            if (speedKmh >= FAST_DRIVE_THRESHOLD * 1.6) return 3;
            if (speedKmh >= FAST_DRIVE_THRESHOLD * 1.3) return 2;
            return 1;
        }



        // ── State ───────────────────────────────────────────────
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

        // ── Driver ──────────────────────────────────────────────
        public string CurrentDriver { get; private set; } = "default_driver";

        // All persisted driver data (stats + lap records) lives in one file — see DriverData.
        private readonly string _driverDataFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "driver_data.json");
        private Dictionary<string, DriverData> _driverData = new();

        // Old per-purpose files — read once to migrate into _driverDataFile if that file
        // doesn't exist yet, then never touched again.
        private static readonly string LegacyStatsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "driver_stats.json");
        private static readonly string LegacyLapRecordsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "driver_lap_records.json");
        private static readonly string LegacyLastLapRecordsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "driver_lastlap_records.json");

        private string _currentTrackCode = "";
        private string _currentLayoutName = "";
        private string _currentTrackKey = "";

        public bool HasActiveLapContext { get; private set; } = false;   // false until the first lap checkpoint is seen

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

        // ── Entry angle (into the first corner of a drift sequence) ─────────────
        // Evaluated EXACTLY at T+1.5s from the start of the drift sequence (not earlier, not
        // averaged) — see EvaluateDriftEntry(). If the drift ends before 1.5s elapses, the
        // evaluation never happens (no bonus is awarded).
        private const double DRIFT_ENTRY_EVAL_SEC = 1.5;
        private const double DRIFT_ENTRY_GOOD_DEG = 60.0;
        private const double DRIFT_ENTRY_HIGH_DEG = 70.0;
        private const double DRIFT_ENTRY_EXTREME_DEG = 80.0;
        private const double DRIFT_ENTRY_ULTRAEXTREME_DEG = 90.0;
        private const double DRIFT_ENTRY_BACKWARD_DEG = 100.0;

        private DateTime _driftEntryStartTime = DateTime.MinValue;
        private bool _driftEntryEvaluated = false;


        // ── Events ──────────────────────────────────────────────

        public event Action<long, double, string, string, string, DriftLabelKind>? DriftScored;
        public event Action? DriftStarted;
        public event Action<long>? DriftEnded;
        public event Action<long, long>? LapCompleted;

        public DriftEngine()
        {
            LoadDriverData();
        }


        // ────────────────────────────────────────────────────────
        //  Driver handling
        // ────────────────────────────────────────────────────────
        public void SetDriver(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                driverName = "default_driver";

            CurrentDriver = driverName;

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
            if (!_driverData.TryGetValue(driver, out var d))
            {
                d = new DriverData();
                _driverData[driver] = d;
            }
            return d;
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

            // Migrates "default" records (from before per-layout keys existed) to the real
            // layout name, for both best lap and last lap.
            MigrateDefaultLapRecordIfNeeded();
            MigrateDefaultLastLapRecordIfNeeded();

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LastLapScore = GetLastLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LapScore = 0;
            HasActiveLapContext = false;
        }

        private void MigrateDefaultLapRecordIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_currentLayoutName)) return;
            var trackMap = GetOrCreateDriverData(CurrentDriver).LapRecords;
            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap)) return;
            if (!layoutMap.TryGetValue("default", out var defaultScore)) return;
            if (layoutMap.ContainsKey(_currentLayoutName)) return;

            layoutMap[_currentLayoutName] = defaultScore;
            layoutMap.Remove("default");
            SaveDriverDataRaw();
        }

        public void OnLapCompleted()
        {
            // KISS bonuses don't count as a penalty — only actual HIT penalties do
            // (see RegisterCollisionPenalty, called from MainForm.HandleObjectHit). Also
            // requires the same score threshold used to show LastLapScore in the overlay
            // (see MainForm's LapCompleted handler) — no bonus for a trivial/empty lap.
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

        // ────────────────────────────────────────────────────────
        //  Update
        // ────────────────────────────────────────────────────────

        int driftTimeMultipler = 0;
        double driftTotalTime= 0;
        int deepAngleCount = 0;
        int eBrakeCount = 0;
        bool eDriftActive = false;

        // ── Blokada wykrywania podczas przerwy w danych OutGauge ────────────────────
        // OutGauge (RPM/throttle/gear) is a SEPARATE UDP stream from InSim/MCI — MCI (and so
        // Update(car) below) can keep flowing even when OutGauge goes quiet (car sits on track
        // but the player is e.g. in a settings menu/garage, or OutGauge UDP packets just get
        // lost). Without this gate, drift/speeding would keep detecting from InSim alone, and
        // burnout (which needs OutGauge RPM/throttle anyway) would simply freeze on its last
        // values — both misleading. Call SetOutGaugeDataFresh(false/true) from the outside (see
        // MainForm.OnCarData) BEFORE every Update(car).
        private bool _outGaugeDataFresh = true;

        /// <summary>
        /// Tells the engine whether OutGauge telemetry is currently "fresh". On the
        /// fresh→stale transition, the ENTIRE current run (combo, points, drift/speeding/burnout
        /// state) is CANCELLED, not just frozen on old values, and while data stays stale,
        /// Update(car) doesn't detect any new drift/speeding/burnout at all (see the early
        /// return in Update()).
        /// </summary>
        public void SetOutGaugeDataFresh(bool isFresh)
        {
            if (isFresh == _outGaugeDataFresh) return;
            _outGaugeDataFresh = isFresh;
            if (!isFresh)
                CancelActiveRun();
        }

        /// <summary>
        /// Cancels the current run/combo/activity state WITHOUT counting it as finished —
        /// deliberately does NOT call DriftEnded/RegisterRunEnd, since this isn't a normal
        /// end of run, just an interruption from data loss.
        /// </summary>
        /// <summary>Public wrapper for CancelActiveRun — call when the driver being tracked
        /// changes (e.g. Tab-switching who's observed), so leftover combo/run points from
        /// whoever was watched before don't get misattributed to the new driver.</summary>
        public void ResetCurrentRun() => CancelActiveRun();

        private void CancelActiveRun()
        {
            IsDrifting = false;
            IsSpeeding = false;
            IsBurnout = false;
            ComboMultiplier = 1;
            CurrentRunPoints = 0;
            _previouslyDrifting = false;

            // Combo-continuity markers for drift↔speeding↔burnout (see Update()) — reset so
            // nothing "remembers" the interrupted run once data resumes.
            _lastDriftTime = DateTime.MinValue;
            _lastSpeedingTime = DateTime.MinValue;
            _lastBurnoutTime = DateTime.MinValue;

            // Burnout spin-tracking state — reset so a sudden heading jump from before the
            // interruption doesn't get counted as a false angular-speed spike once data resumes.
            _burnoutConditionStart = DateTime.MinValue;
            _lastBurnoutHeadingDeg = null;
            _burnoutSpinAccumDeg = 0;
            _burnoutActiveDuringSpin = false;

            _burnoutSpinComboCount = 0;
            _lastBurnoutSpinBonusTime = DateTime.MinValue;
            _donutComboCount = 0;
            _lastDonutBonusTime = DateTime.MinValue;
            _stationaryBurnoutStart = DateTime.MinValue;
            _stationaryBurnoutTierAwarded = 0;
            _driftSpinAccumDeg = 0;
            _driftSpinLastHeadingDeg = null;
            _driftSpinStartSet = false;
            _driftSpinStartWasBurnout = false;
            _donutPrevIsBurnout = null;
            _wasDriftingPrev = false;
            _lastTransitionBonusTime = DateTime.MinValue;
            _burnoutActiveStartTime = DateTime.MinValue;
            _pendingBurnoutToDrift = false;
            _pendingDriftToBurnout = false;
        }

        // Long-drift duration milestones (driftTimeMultipler ticks, each = 500ms of continuous
        // drift): threshold reached -> ComboMultiplier bonus factor (times angleScore). See Update().
        private static readonly (int Threshold, double ComboFactor)[] LongDriftMilestones =
        {
            (5, 0.15), (10, 0.25), (15, 0.35), (20, 0.45)
        };

        // Deep-angle (>=55°) streak milestones: threshold reached -> ComboMultiplier bonus
        // factor (times angleScore). Also pays a flat 50 points per streak tick reached. See Update().
        private static readonly (int Threshold, double ComboFactor)[] DeepAngleMilestones =
        {
            (4, 0.35), (8, 0.45), (12, 0.55), (16, 0.65)
        };

        public void Update(InSim.CompCar car)
        {
            // Gated — see SetOutGaugeDataFresh. State was already cleared by CancelActiveRun()
            // at the fresh→stale transition, so it's enough to just do nothing until data returns.
            if (!_outGaugeDataFresh) return;

            var now = DateTime.UtcNow;

            _dtSec = (now - _lastUpdateTime).TotalSeconds;
            if (_dtSec > 0.5)
                _dtSec = 0.5;

            _lastUpdateTime = now;

            SpeedKmh = car.SpeedKmh;

            double motionDeg = car.Direction / 65536.0 * 360.0;
            double headingDeg = car.Heading / 65536.0 * 360.0;

            double diff = NormalizeAngleDelta(headingDeg - motionDeg);

            DriftSideRight = diff >= 0;
            DriftAngleDeg = Math.Abs(diff);

            bool speeding = SpeedKmh >= FAST_DRIVE_THRESHOLD;

            bool drifting = SpeedKmh >= MIN_SPEED_KMH
                         && DriftAngleDeg >= MIN_DRIFT_ANGLE_DEG
                         && DriftAngleDeg <= MAX_DRIFT_ANGLE_DEG;

            // Smooth burnout->drift transition bonus — checked BEFORE UpdateBurnout(), which
            // only touches _lastBurnoutTime while burnout is active this same frame.
            // Awarded only once BOTH sides held >= TRANSITION_MIN_ACTIVITY_SEC: the ending
            // burnout (checked here, at the edge) AND the new drift (checked every tick below,
            // cancelled if drift breaks before reaching the threshold).
            bool driftJustStarted = drifting && !_wasDriftingPrev;
            _wasDriftingPrev = drifting;

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
                    _pendingBurnoutToDrift = false;   // broke off before reaching the threshold
                }
                else if ((now - _pendingBurnoutToDriftStart).TotalSeconds >= TRANSITION_MIN_ACTIVITY_SEC)
                {
                    _pendingBurnoutToDrift = false;
                    if ((now - _lastTransitionBonusTime).TotalSeconds >= TRANSITION_BONUS_COOLDOWN_SEC)
                    {
                        _lastTransitionBonusTime = now;
                        AwardMilestoneBonus(Localization.T("bonus.burnout_to_drift"));
                    }
                }
            }

            UpdateBurnout(now, drifting, speeding, headingDeg);
            UpdateStationaryBurnoutBonus(now);

            // Donuts also count during burnout (a burnout with real radius can loop back to a
            // point same as a drift can) — IsBurnout here already reflects this tick, UpdateBurnout()
            // ran just above. InSim.txt: CompCar.X/Y — 65536 = 1 metre.
            UpdateDonutTracking(now, drifting, IsBurnout, headingDeg, car.X / 65536f, car.Y / 65536f);

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
                // LastAwardedText clearing is handled uniformly below (bonusgapCLR, time-based) —
                // used to also clear it instantly here, which raced with any bonus that fires
                // while not actively speeding/drifting/burnout (e.g. burnout spin, clean lap).

                // Combo/CurrentRunPoints survive a short gap (COMBO_TIMEOUT_SEC) between
                // drift↔speeding↔burnout — reset only happens once ALL THREE activity sources
                // have been silent longer than the limit (none of them is "holding" combo anymore).
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
                // ── "Fast driving" level (1-5) and combo growth PER SECOND ──────────
                // ComboMultiplier used to be OVERWRITTEN here with a fixed value based SOLELY on
                // current speed, so it flickered up/down with it instead of growing over time
                // like drift/burnout do (where something is always ADDED to ComboMultiplier every
                // frame). Now it works the same way: each speed level adds more per second than
                // the last (FAST_DRIVE_COMBO_STEP_PER_SEC per level), hard-capped at 10.0.
                int speedLevel = GetFastDriveLevel(SpeedKmh);
                double comboGainPerSec = speedLevel * FAST_DRIVE_COMBO_STEP_PER_SEC;
                ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + comboGainPerSec * _dtSec, 10.0), 2);

                long fastPts = (long)(FAST_DRIVE_POINTS * _dtSec * ComboMultiplier);



                CurrentRunPoints += (long)fastPts;
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
                if (!drifting)
                {
                    // speeding is always true inside this block (see the entry condition
                    // `!drifting && speeding` above) — an "if (!speeding)" branch that used to be
                    // here could never execute.
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
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

                if (!_previouslyDrifting)
                {
                    _driftStartTime = now;
                    _driftSessionStart = now;
                    driftTimeMultipler = 0;

                    if (!_isHandBrakeON) { eBrakeCount--; }
                    eDriftActive = false;

                    IsDrifting = true;

                    // ── entry angle: start measuring for a new drift sequence ──
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
                                if (SpeedKmh >= 35 && DriftAngleDeg >= MIN_DRIFT_ANGLE_DEG)
                                {


                                    if (!_isHandBrakeON) { eBrakeCount = 0; }

                                    var labelKindT = DriftLabelKind.TransitionDrift;
                                    string bonusText = Localization.T(LabelKindToKey(labelKindT));
                                    _lastBonusTime = now;
                                    ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.11 * speedScore), 10.0), 2);
                                    bonusScore = Math.Round(bonusScore, 0) * Math.Round(ComboMultiplier, 2);

                                    // Rounded ONCE to long and used identically everywhere — CurrentRunPoints
                                    // used to round bonusScore (Math.Round) while TotalScore/LapScore
                                    // TRUNCATED it (double→long casting truncates, doesn't round), so for a
                                    // non-integer bonusScore (typical, since it's multiplied by ComboMultiplier)
                                    // CurrentRunPoints could differ by 1 point from Total/Lap for the EXACT
                                    // same event.
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

                    DriftStarted?.Invoke();
                }

                // ── entry angle: evaluated exactly at T+1.5s from the start of the drift sequence ──
                // (not at the start, not averaged — just DriftAngleDeg's value in the exact
                // frame where 1.5s of uninterrupted drift is reached)
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

                CurrentRunPoints += (long)framePts;
                TotalScore += (long)framePts;
                LapScore += (long)framePts;
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

                // end of drift sequence — clear entry-evaluation state so the next
                // sequence starts measuring from zero
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

        // ────────────────────────────────────────────────────────
        //  Persistence — everything lives in one file, driver_data.json
        // ────────────────────────────────────────────────────────
        private void LoadDriverData()
        {
            try
            {
                if (File.Exists(_driverDataFile))
                {
                    string json = File.ReadAllText(_driverDataFile);
                    _driverData = JsonSerializer.Deserialize<Dictionary<string, DriverData>>(json)
                                  ?? new Dictionary<string, DriverData>();
                    return;
                }
            }
            catch
            {
                _driverData = new Dictionary<string, DriverData>();
            }

            // First run on the new format — pull in the old 3-file layout if present.
            MigrateLegacyFiles();
        }

        // One-time import from the pre-consolidation files (driver_stats.json,
        // driver_lap_records.json, driver_lastlap_records.json). Leaves the old files on
        // disk untouched; only driver_data.json is written going forward.
        private void MigrateLegacyFiles()
        {
            try
            {
                if (File.Exists(LegacyStatsFile))
                {
                    string json = File.ReadAllText(LegacyStatsFile);
                    Dictionary<string, DriverStats> old;
                    try
                    {
                        old = JsonSerializer.Deserialize<Dictionary<string, DriverStats>>(json)
                              ?? new Dictionary<string, DriverStats>();
                    }
                    catch
                    {
                        // even older format: Dictionary<string, long>
                        var older = JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? new();
                        old = new Dictionary<string, DriverStats>();
                        foreach (var kv in older)
                            old[kv.Key] = new DriverStats { TotalScore = kv.Value };
                    }
                    foreach (var kv in old)
                        GetOrCreateDriverData(kv.Key).Stats = kv.Value;
                }

                if (File.Exists(LegacyLapRecordsFile))
                {
                    string json = File.ReadAllText(LegacyLapRecordsFile);
                    var old = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, long>>>>(json);
                    if (old != null)
                        foreach (var kv in old)
                            GetOrCreateDriverData(kv.Key).LapRecords = kv.Value;
                }

                if (File.Exists(LegacyLastLapRecordsFile))
                {
                    string json = File.ReadAllText(LegacyLastLapRecordsFile);
                    var old = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, long>>>>(json);
                    if (old != null)
                        foreach (var kv in old)
                            GetOrCreateDriverData(kv.Key).LastLapRecords = kv.Value;
                }

                if (_driverData.Count > 0)
                    SaveDriverDataRaw(force: true);
            }
            catch { }
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

        private void MigrateDefaultLastLapRecordIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_currentLayoutName)) return;
            var trackMap = GetOrCreateDriverData(CurrentDriver).LastLapRecords;
            if (!trackMap.TryGetValue(_currentTrackCode, out var layoutMap)) return;
            if (!layoutMap.TryGetValue("default", out var defaultScore)) return;
            if (layoutMap.ContainsKey(_currentLayoutName)) return;

            layoutMap[_currentLayoutName] = defaultScore;
            layoutMap.Remove("default");
            SaveDriverDataRaw();
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
        private static readonly TimeSpan StatsSaveInterval = TimeSpan.FromSeconds(2);

        // Called multiple times/sec during active scoring (fastPts/framePts/burnout) as well
        // as once per lap (records) — the in-memory dictionary is always kept current, but the
        // disk write (JSON serialize + File.WriteAllText of the whole file) is throttled so it
        // doesn't stall the caller's thread every frame.
        private void SaveDriverDataRaw(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastStatsSaveUtc) < StatsSaveInterval)
                return;

            _lastStatsSaveUtc = DateTime.UtcNow;

            try
            {
                string json = JsonSerializer.Serialize(
                    _driverData,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_driverDataFile, json);
            }
            catch
            {
            }
        }

        // Updates the in-memory stats for CurrentDriver and writes driver_data.json (throttled
        // unless force is set).
        private void SaveCurrentDriver(bool force = false)
        {
            var stats = GetOrCreateDriverData(CurrentDriver).Stats;
            stats.TotalScore = TotalScore;
            stats.BestRunScore = BestRunScore;
            stats.BestDriftDurationMs = BestDriftDurationMs;
            stats.BestDeepDriftDurationMs = BestDeepDriftDurationMs;

            SaveDriverDataRaw(force);
        }

        /// <summary>Forces an immediate save, bypassing the throttle — call on app shutdown.</summary>
        public void FlushStats() => SaveCurrentDriver(force: true);

        // ────────────────────────────────────────────────────────
        //  Records: best run / longest drift / deep-angle drift
        // ────────────────────────────────────────────────────────
        private void RegisterRunEnd(long runPoints)
        {
            if (runPoints > BestRunScore)
            {
                BestRunScore = runPoints;
            }

            // Force-save (bypass the 2s throttle) whenever a drift/speeding/burnout run ends —
            // without this, TotalScore on disk could lag behind by up to StatsSaveInterval and
            // then sit stale until the next active run starts saving again. A stale on-disk
            // value matters more now that legitimate driver-switch reloads read straight from it.
            SaveCurrentDriver(force: true);
        }

        /// <summary>
        /// Adds/subtracts points for an object hit (bonus/penalty) or a drift-entry angle bonus.
        /// Adds the EXACT same value to CurrentRunPoints, TotalScore, and LapScore — it used to
        /// deliberately skip CurrentRunPoints (the comment even said so), so a collision during
        /// an active drift made LapScore/TotalScore jump while "RUN SCORE"/combo on the HUD
        /// (computed from CurrentRunPoints) stayed unchanged, looking like LapScore was "ahead of"
        /// the current run. Both counters now always move together.
        /// </summary>
        public void ApplyPostPoints(long delta, string awardLabel)
        {
            CurrentRunPoints += delta;
            TotalScore += delta;
            LapScore += delta;
            if (ComboMultiplier > 0) ComboMultiplier += 0.1;
            LastAwardedText = awardLabel;
            SaveCurrentDriver();
        }

        // Shared payout for the milestone bonuses (stationary burnout / donut / smooth
        // transitions / spin streaks): 100 pts * current combo, +0.1 combo via ApplyPostPoints.
        // Sets _lastBonusTime itself so the tick-based bonusgapCLR clear (see Update()) doesn't
        // wipe the text before it reaches the overlay.
        private void AwardMilestoneBonus(string label)
        {
            long pts = (long)Math.Round(MILESTONE_BONUS_POINTS * ComboMultiplier);
            _lastBonusTime = DateTime.UtcNow;
            ApplyPostPoints(pts, $"{label} +{pts}");
        }

        // Shared by burnout-spin and donut streaks: bumps a consecutive-hit counter (reset to 0
        // first if more than SPIN_COMBO_GAP_SEC passed since its own last award), then awards a
        // milestone bonus labeled "baseLabel" or "baseLabel xN" once N>1.
        private void AwardStreakBonus(string baseLabel, ref int comboCount, ref DateTime lastBonusTime, DateTime now)
        {
            if ((now - lastBonusTime).TotalSeconds > SPIN_COMBO_GAP_SEC)
                comboCount = 0;
            comboCount++;
            lastBonusTime = now;

            string label = baseLabel + (comboCount > 1 ? $" x{comboCount}" : "");
            AwardMilestoneBonus(label);
        }

        // Same as AwardMilestoneBonus but a flat point value, not combo-scaled (e.g. clean lap).
        private void AwardFixedBonus(long points, string label)
        {
            _lastBonusTime = DateTime.UtcNow;
            ApplyPostPoints(points, $"{label} +{points}");
        }

        /// <summary>Call when a collision ends in an actual penalty (HIT), not a KISS bonus —
        /// suppresses the clean-lap bonus for the lap in progress.</summary>
        public void RegisterCollisionPenalty() => _lapHadPenalty = true;

        /// <summary>Fast-driving hit that didn't cost speed — no penalty, bonus instead.
        /// See MainForm.HandleObjectHit.</summary>
        private const double UNSTOPPABLE_COOLDOWN_SEC = 0.5;
        private DateTime _lastUnstoppableBonusTime = DateTime.MinValue;
        public void AwardUnstoppableBonus()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUnstoppableBonusTime).TotalSeconds < UNSTOPPABLE_COOLDOWN_SEC) return;
            _lastUnstoppableBonusTime = now;
            AwardMilestoneBonus(Localization.T("bonus.unstoppable"));
        }

        // One-time entry-angle evaluation — called exactly once per drift sequence, on the frame
        // where DRIFT_ENTRY_EVAL_SEC elapses since it started. An angle below the "good"
        // threshold (50°) gets no bonus at all — not a penalty, the entry just wasn't deep
        // enough to stand out.
        private void EvaluateDriftEntry(double angleAtEval)
        {
            DriftLabelKind kind;
            long bonus;

            if (angleAtEval > DRIFT_ENTRY_BACKWARD_DEG) { kind = DriftLabelKind.EntryBackward; bonus = 1100; }
            else if (angleAtEval > DRIFT_ENTRY_ULTRAEXTREME_DEG) { kind = DriftLabelKind.EntryUltraExtreme; bonus = 750; }
            else if (angleAtEval > DRIFT_ENTRY_EXTREME_DEG) { kind = DriftLabelKind.EntryExtreme; bonus = 500; }
            else if (angleAtEval > DRIFT_ENTRY_HIGH_DEG) { kind = DriftLabelKind.EntryHigh; bonus = 300; }
            else if (angleAtEval > DRIFT_ENTRY_GOOD_DEG) { kind = DriftLabelKind.EntryGood; bonus = 150; }
            else return;   // entry angle below the "good" threshold — no bonus

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

        // Wraps an angle difference into (-180, 180] — shared by heading/motion drift angle,
        // burnout spin tracking, and donut heading tracking.
        private static double NormalizeAngleDelta(double delta)
        {
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;
            return delta;
        }

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
        }

        // Shared angle "level" (0-6) behind both indicator-flash formatters below — level 0
        // means the angle doesn't clear MIN_DRIFT_ANGLE_DEG at all (no text on either side).
        private static int GetDriftIndicatorLevel(double angle)
        {
            if (angle > 90) return 6;
            if (angle > MIN_DRIFT_ANGLE_DEG + 45) return 5;
            if (angle > MIN_DRIFT_ANGLE_DEG + 35) return 4;
            if (angle > MIN_DRIFT_ANGLE_DEG + 25) return 3;
            if (angle > MIN_DRIFT_ANGLE_DEG + 15) return 2;
            if (angle > MIN_DRIFT_ANGLE_DEG) return 1;
            return 0;
        }

        // Right/left indicator-flash text, and the matching-width blanks used on whichever side
        // the car ISN'T drifting toward, so the HUD layout doesn't shift when the active side
        // switches. Indexed by GetDriftIndicatorLevel(angle).
        private static readonly string[] IndicatorTextRight = { "", " >", " >>", " >>>", " >>>>", " >>>>>", " ???" };
        private static readonly string[] IndicatorTextLeft = { "", "< ", "<< ", "<<< ", "<<<< ", "<<<<< ", "??? " };
        private static readonly string[] IndicatorBlank = { "", "  ", "   ", "    ", "     ", "      ", "    " };

        private static string GetDriftValueINDR(double angle, bool side)
        {
            int level = GetDriftIndicatorLevel(angle);
            return side ? IndicatorTextRight[level] : IndicatorBlank[level];
        }

        private static string GetDriftValueINDL(double angle, bool side)
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
                if (angle > MIN_DRIFT_ANGLE_DEG + 45) return DriftLabelKind.AngleUltraExtreme;
                if (angle > MIN_DRIFT_ANGLE_DEG + 35) return DriftLabelKind.AngleExtreme;
                if (angle > MIN_DRIFT_ANGLE_DEG + 25) return DriftLabelKind.AngleHigh;
                if (angle > MIN_DRIFT_ANGLE_DEG + 15) return DriftLabelKind.AngleGood;
                if (angle < MIN_DRIFT_ANGLE_DEG + 15 && angle > MIN_DRIFT_ANGLE_DEG) return DriftLabelKind.Generic;
            }
            else
            {
                if (angle > 90 && speed > 30) return DriftLabelKind.AngleBackwardE;
                if (angle > MIN_DRIFT_ANGLE_DEG + 45) return DriftLabelKind.AngleUltraExtremeE;
                if (angle > MIN_DRIFT_ANGLE_DEG + 35) return DriftLabelKind.AngleExtremeE;
                if (angle > MIN_DRIFT_ANGLE_DEG + 25) return DriftLabelKind.AngleHighE;
                if (angle > MIN_DRIFT_ANGLE_DEG + 15) return DriftLabelKind.AngleGoodE;
                if (angle < MIN_DRIFT_ANGLE_DEG + 15 && angle > MIN_DRIFT_ANGLE_DEG) return DriftLabelKind.GenericE;
            }

            if (speed > 120 && angle > MIN_DRIFT_ANGLE_DEG) return DriftLabelKind.FastDrift;

            return DriftLabelKind.Generic;
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

        // ────────────────────────────────────────────────────────
        //  Burnout detection — driven wheels spinning much faster than the car is
        //  actually moving (typically standing still with the throttle down).
        // ────────────────────────────────────────────────────────

        // Real speed (SpeedKmh, from InSim MCI) must be below this threshold — otherwise
        // it's normal driving/drifting, not standing still.
        private const double BURNOUT_MAX_REAL_SPEED_KMH = 25.0;

        // RPM must be CLEARLY above a typical idle (usually 800-1200 RPM in LFS) — otherwise
        // an engine idling with the car stationary and the driver doing nothing false-positived
        // as a "burnout" (see the reported bug: detection while idling). Together with the
        // throttle threshold and the gear requirement below, this is now the ONLY burnout signal
        // — no wheel-speed estimation (see the comment at UpdateEngineTelemetry: attempts to
        // compute it from RPM+gear ratio, then from OutSim AngVel, proved unreliable in
        // practice and were removed).
        private const float BURNOUT_MIN_RPM = 3000f;

        // Throttle must be pressed clearly hard — high RPM alone (e.g. coasting down from the
        // redline after lifting off) shouldn't count as a burnout.
        private const float BURNOUT_MIN_THROTTLE = 0.45f;

        // Conditions (speed + RPM + throttle + gear) must hold continuously for at least this
        // long before a burnout is considered active and starts scoring points.
        private const double BURNOUT_ACTIVATE_SEC = 0.5;

        // Base points per second AFTER activation (i.e. once BURNOUT_ACTIVATE_SEC has passed),
        // multiplied by the growing ComboMultiplier (see UpdateBurnout).
        private const double BURNOUT_POINTS_PER_SECOND = 5.0;

        // Bonus for every full rotation (360°) during an active burnout (a "spin").
        private const long BURNOUT_SPIN_BONUS_POINTS = 250;

        // Milestone bonuses (stationary burnout / donut / smooth transitions): base points
        // times current combo, same as ApplyPostPoints' automatic +0.1 combo growth.
        private const long MILESTONE_BONUS_POINTS = 100;

        // Clean lap: flat 250 (not combo-scaled) if no collision penalty (HIT) happened this
        // lap AND LapScore reached this threshold — see RegisterCollisionPenalty / _lapHadPenalty.
        // Same value as MainForm's LastLapScore display threshold — keep both in sync.
        private const long CLEAN_LAP_BONUS_POINTS = 250;
        private const long CLEAN_LAP_MIN_SCORE = 1500;
        private bool _lapHadPenalty = false;

        // Stationary burnout: speed threshold + time tiers, one bonus per tier crossed.
        private const double STATIONARY_BURNOUT_MAX_SPEED_KMH = 1.5;
        private static readonly double[] STATIONARY_BURNOUT_TIERS_SEC = { 5, 10, 15, 20 };

        // Donut: full 360° while drifting, counted only if the car ends up back near where
        // the lap started (otherwise it's just a long drift, not a loop around a point).
        private const double DONUT_RADIUS_METERS = 20.0;
        private const double DRIFT_SPIN_ABANDON_SEC = 1.0;

        // Max gap (s) between one activity ending and the other starting to count as a
        // smooth burnout<->drift transition.
        private const double TRANSITION_BONUS_GRACE_SEC = 1.0;

        // Cooldown between transition bonuses (either direction) — stops rapid flicker
        // (e.g. brief speed dips at the drifting/burnout boundary) from spamming the popup.
        private const double TRANSITION_BONUS_COOLDOWN_SEC = 2.0;
        private DateTime _lastTransitionBonusTime = DateTime.MinValue;

        // The ending activity must have actually lasted this long — filters out brief
        // flicker-y bursts from counting as a "smooth transition". Same threshold applies
        // to the NEW activity too (see _pendingBurnoutToDrift/_pendingDriftToBurnout) —
        // the bonus only fires once both sides held it.
        private const double TRANSITION_MIN_ACTIVITY_SEC = 2.0;
        private DateTime _burnoutActiveStartTime = DateTime.MinValue;

        private bool _pendingBurnoutToDrift = false;
        private DateTime _pendingBurnoutToDriftStart = DateTime.MinValue;
        private bool _pendingDriftToBurnout = false;
        private DateTime _pendingDriftToBurnoutStart = DateTime.MinValue;

        public bool IsBurnout { get; private set; }

        // Engine telemetry fed in from the outside (see UpdateEngineTelemetry) — InSim MCI only
        // gives the car's real speed, so RPM/throttle/gear (from OutGauge) have to be supplied
        // separately. OutGauge.Gear: 0=reverse, 1=neutral, 2=1st gear, 3=2nd gear...
        private float _lastRpm = 0f;
        private float _lastThrottle = 0f;
        private int _lastGear = 0;

        // Moment since which the burnout conditions (speed<BURNOUT_MAX_REAL_SPEED_KMH, gear≥2,
        // throttle≥BURNOUT_MIN_THROTTLE, RPM≥BURNOUT_MIN_RPM — see UpdateBurnout) have held
        // continuously. DateTime.MinValue = conditions currently not met.
        private DateTime _burnoutConditionStart = DateTime.MinValue;

        // Last moment points were added for an active burnout — same role as
        // _lastDriftTime/_lastSpeedingTime: protects CurrentRunPoints/ComboMultiplier from being
        // zeroed by the general combo-reset block (see Update()), which would otherwise reset the
        // score every frame right after awarding it, since a burnout by definition has SpeedKmh
        // below the drift/speeding threshold. Thanks to this marker (and the shared
        // ComboMultiplier property), combo earned during a burnout survives the cooldown and
        // continues smoothly into drift/fast driving — exactly like drift↔speeding combo.
        private DateTime _lastBurnoutTime = DateTime.MinValue;

        // Tracks the car's heading rotation to detect full 360° spins. Based SOLELY on angular
        // speed (change in headingDeg / time), not SpeedKmh — a spin can have a real radius and
        // real linear speed, so requiring "the car is stationary" rejected most real spins.
        private double? _lastBurnoutHeadingDeg = null;
        private double _burnoutSpinAccumDeg = 0;
        private DateTime _lastSpinActivityTime = DateTime.MinValue;

        // The car's angular speed must exceed this threshold to count as "spinning" rather than
        // just taking a corner on track (even a tight hairpin rarely reaches this rate of
        // heading change). Value may need further tuning.
        private const double BURNOUT_SPIN_MIN_YAW_RATE_DEG_PER_SEC = 85.0;

        // If angular speed drops below the threshold for longer than this many seconds, treat it
        // as not a continuous spin (e.g. normal driving) and reset progress so far — but a SINGLE
        // slower frame (e.g. reading noise) doesn't clear progress.
        private const double BURNOUT_SPIN_ABANDON_SEC = 1.0;

        // Whether a burnout was active at ANY point during the current, still-incomplete spin —
        // not just exactly on the frame where the accumulator crosses 360°. Without this, a
        // single frame of IsBurnout flicker (from wheelDiff noise) right at spin completion could
        // silently "eat" the bonus that was due.
        private bool _burnoutActiveDuringSpin = false;

        // Consecutive spin/donut counters — tracked separately, each resets if more than
        // SPIN_COMBO_GAP_SEC passes since its own last award (also reset with combo).
        private const double SPIN_COMBO_GAP_SEC = 7.5;
        private int _burnoutSpinComboCount = 0;
        private DateTime _lastBurnoutSpinBonusTime = DateTime.MinValue;
        private int _donutComboCount = 0;
        private DateTime _lastDonutBonusTime = DateTime.MinValue;

        // Stationary burnout tiers — see UpdateStationaryBurnoutBonus.
        private DateTime _stationaryBurnoutStart = DateTime.MinValue;
        private int _stationaryBurnoutTierAwarded = 0;

        // Donut tracking — heading accumulation while drifting + lap start position.
        private double? _driftSpinLastHeadingDeg = null;
        private double _driftSpinAccumDeg = 0;
        private DateTime _lastDriftSpinActivityTime = DateTime.MinValue;
        private float _driftSpinStartX, _driftSpinStartY;
        private bool _driftSpinStartSet = false;
        private bool _driftSpinStartWasBurnout = false;
        private bool? _donutPrevIsBurnout = null;

        // Previous frame's drifting state — detects the burnout->drift transition edge.
        private bool _wasDriftingPrev = false;

        /// <summary>
        /// Updates the engine telemetry (RPM, throttle, gear) used for burnout detection — see
        /// MainForm.OnRevData (OutGauge data). Call this independently of Update(car); the
        /// latest value is taken into account on the next call to Update().
        /// </summary>
        public void UpdateEngineTelemetry(float rpm, float throttle, int gear)
        {
            _lastRpm = Math.Max(0, rpm);
            _lastThrottle = Math.Clamp(throttle, 0f, 1f);
            _lastGear = gear;
        }

        private void UpdateBurnout(DateTime now, bool drifting, bool speeding, double headingDeg)
        {
            // ── Spin tracking ───────────────────────────────────────
            // Based on angular speed (deg/s), not SpeedKmh — a spin with a real radius has a real
            // linear speed, so requiring "the car is stationary" rejected most real spins.
            // Angular speed naturally tells a spin apart from normal driving on track, since even
            // a tight corner rarely reaches this rate of heading change for more than a fraction
            // of a second.
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
                    // too long without a fast rotation — this wasn't a continuous spin, just
                    // normal driving; clear the incomplete progress so far
                    _burnoutSpinAccumDeg = 0;
                    _burnoutActiveDuringSpin = false;
                }
                // otherwise: a single slower frame mid-spin (e.g. a gear change moment) —
                // don't clear progress, wait for the timeout instead
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
                }

                _burnoutActiveDuringSpin = false;   // reset for the next, new rotation
            }

            // Neutral/reverse gear (Gear<2) rules out a burnout regardless of RPM/throttle —
            // in neutral the engine physically doesn't drive the wheels, so even holding full
            // throttle at a standstill is NOT wheelspin.
            bool conditionNow = SpeedKmh < BURNOUT_MAX_REAL_SPEED_KMH
                              && _lastGear >= 2
                              && _lastThrottle >= BURNOUT_MIN_THROTTLE
                              && _lastRpm >= BURNOUT_MIN_RPM;

            if (!conditionNow)
            {
                if (IsBurnout)
                {
                    // burnout was active and just ended (car moved off, or the wheels stopped spinning)
                    DriftEnded?.Invoke(CurrentRunPoints);
                    RegisterRunEnd(CurrentRunPoints);
                }
                _burnoutConditionStart = DateTime.MinValue;
                IsBurnout = false;
                _pendingDriftToBurnout = false;   // broke off before reaching the threshold

                return;
            }

            if (_burnoutConditionStart == DateTime.MinValue)
                _burnoutConditionStart = now;

            double heldSec = (now - _burnoutConditionStart).TotalSeconds;

            if (heldSec < BURNOUT_ACTIVATE_SEC)
                return;   // conditions met, but not for long enough yet

            bool justActivated = !IsBurnout;
            IsBurnout = true;

            if (justActivated)
            {
                _burnoutActiveStartTime = now;
                DriftStarted?.Invoke();

                // Awarded only once BOTH sides held >= TRANSITION_MIN_ACTIVITY_SEC: the ending
                // drift (checked here) AND the new burnout (checked below every tick, cancelled
                // above in the !conditionNow branch if burnout breaks before reaching it).
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

            // the higher the RPM above the activation threshold, the faster ComboMultiplier
            // grows — same pattern as drifting (ComboMultiplier grows with angleScore every
            // frame, capped at 10.0). Since ComboMultiplier is the same SHARED property as for
            // drift/speeding, combo earned during a burnout naturally continues into drift or
            // fast driving (and vice versa), protected through COMBO_TIMEOUT_SEC by the
            // combo-reset block in Update() via _lastBurnoutTime.
            double rpmFactor = Math.Clamp(_lastRpm / (BURNOUT_MIN_RPM ), 1.0, 5.0);
            ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.01 * rpmFactor * _dtSec), 10.0), 2);

            long framePts = (long)Math.Round(BURNOUT_POINTS_PER_SECOND * ComboMultiplier * _dtSec * (_lastGear / 2));

            CurrentRunPoints += framePts;
            TotalScore += framePts;
            LapScore += framePts;
            _lastBurnoutTime = now;
            SaveCurrentDriver();

            var burnoutKind = GetBurnoutLabelKind(_lastRpm, heldSec, _lastGear);
            DriftScored?.Invoke(
                CurrentRunPoints,
                ComboMultiplier,
                Localization.T(LabelKindToKey(burnoutKind)),
                "",
                "",
                burnoutKind
            );
        }

        // Stationary burnout: SpeedKmh under STATIONARY_BURNOUT_MAX_SPEED_KMH while IsBurnout,
        // held continuously — one bonus per time tier crossed (5s/10s/15s/20s).
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

        // Donut: accumulates heading change while drifting OR burning out (same idea as the
        // burnout spin accumulator) and, on each full 360°, checks whether the car is back near
        // where that loop started — otherwise it's just a long drift, not a loop around a point.
        // A drift<->burnout switch cancels whatever was accumulated so far (leftover rotation
        // from a rejected stationary burnout spin was bleeding into the next drift and awarding
        // a "phantom" donut a moment later) — a loop must complete within one unbroken state-run.
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

                if (positionOk && crossedState)
                {
                    AwardStreakBonus(Localization.T("bonus.donut"),
                        ref _donutComboCount, ref _lastDonutBonusTime, now);
                }

                _driftSpinStartWasBurnout = isBurnout;   // start state for the next loop

                // start point for the next loop
                _driftSpinStartX = carXMeters;
                _driftSpinStartY = carYMeters;
            }
        }

        // Burnout tiering, modeled after GetLabelKind (drift/speed) — combines RPM above the
        // activation threshold with hold duration into one "intensity" scale, the same way that
        // one combines angle and speed. The divisor of 100 is chosen so that solidly holding high
        // RPM (a few thousand above the threshold) gives a noticeable severity right away, not
        // only after tens of seconds — value may need further tuning.
        private DriftLabelKind GetBurnoutLabelKind(float rpm, double heldSec, float _lastGear)
        {
            double rpmAboveThreshold = Math.Max(0, rpm - BURNOUT_MIN_RPM);
            double severity = (rpmAboveThreshold / 100.0) + ((heldSec - BURNOUT_ACTIVATE_SEC) * _lastGear);

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
            _lapHadPenalty = false;
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

            _burnoutSpinComboCount = 0;
            _lastBurnoutSpinBonusTime = DateTime.MinValue;
            _donutComboCount = 0;
            _lastDonutBonusTime = DateTime.MinValue;
            _stationaryBurnoutStart = DateTime.MinValue;
            _stationaryBurnoutTierAwarded = 0;
            _driftSpinAccumDeg = 0;
            _driftSpinLastHeadingDeg = null;
            _driftSpinStartSet = false;
            _driftSpinStartWasBurnout = false;
            _donutPrevIsBurnout = null;
            _wasDriftingPrev = false;
            _lastTransitionBonusTime = DateTime.MinValue;
            _burnoutActiveStartTime = DateTime.MinValue;
            _pendingBurnoutToDrift = false;
            _pendingDriftToBurnout = false;

            BestRunScore = 0;
            BestDriftDurationMs = 0;
            BestDeepDriftDurationMs = 0;

            SaveCurrentDriver();
        }
        public void ResetLapScore()
        {
            LapScore = 0;
        }

        /// <summary>Activates lap-point counting after crossing InSim's first checkpoint.
        /// If counting is already active, ignores the call and returns false.</summary>
        public bool ActivateLapCounting()
        {
            if (HasActiveLapContext) return false;

            LapScore = 0;
            HasActiveLapContext = true;
            return true;
        }

        /// <summary>Entering a restricted area — resets and deactivates lap-point counting
        /// until the next pass through the first checkpoint.</summary>
        public void DeactivateLapCounting()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }

        /// <summary>Race restart — zeroes LapScore and hides the HUD frame until the 1st completed lap.</summary>
        public void OnRaceRestarted()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }


    }
}