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
        private const double FAST_DRIVE_THRESHOLD = 100.0;
        private const double FAST_DRIVE_POINTS = 8.0;

        // Przyrost ComboMultiplier na SEKUNDĘ dla poziomu 1 "szybkiej jazdy" (patrz
        // GetFastDriveLevel) — każdy kolejny poziom dodaje kolejne tyle samo więcej
        // (poziom 1 = 0.025/s, poziom 2 = 0.05/s, ..., poziom 5 = 0.125/s).
        private const double FAST_DRIVE_COMBO_STEP_PER_SEC = 0.01;

        /// <summary>Poziom "szybkiej jazdy" (1-5) na podstawie prędkości względem
        /// FAST_DRIVE_THRESHOLD — te same progi, które wcześniej ustawiały ComboMultiplier
        /// wprost, teraz tylko skalują tempo jego przyrostu na sekundę (patrz Update()).
        /// Zakłada, że speedKmh >= FAST_DRIVE_THRESHOLD (gwarantowane przez warunek
        /// `speeding` u wywołującego) — inaczej zawsze zwróci poziom 1.</summary>
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

            // Migracja rekordów "default" (sprzed wprowadzenia kluczy per layout) — dla
            // najlepszego okrążenia I dla ostatniego. Druga z tych metod istniała już
            // wcześniej jako dokładny odpowiednik pierwszej, ale nigdy nie była wywoływana
            // (martwy kod) — rekordy "last lap" zapisane pod starym kluczem "default" nigdy
            // by się nie migrowały do właściwego layoutu, w odróżnieniu od "best lap".
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
            // KISS bonuses don't count as a penalty — only actual HIT penalties do
            // (see RegisterCollisionPenalty, called from MainForm.HandleObjectHit). Also
            // requires the same score threshold used to show LastLapScore in the overlay
            // (see MainForm's LapCompleted handler) — no bonus for a trivial/empty lap.
            if (!_lapHadPenalty && LapScore >= CLEAN_LAP_MIN_SCORE)
                AwardFixedBonus(CLEAN_LAP_BONUS_POINTS, Localization.T("bonus.clean_lap"));
            _lapHadPenalty = false;

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

        // ── Blokada wykrywania podczas przerwy w danych OutGauge ────────────────────
        // OutGauge (RPM/gaz/bieg) to OSOBNY strumień UDP od InSim/MCI — MCI (a więc
        // Update(car) poniżej) potrafi nadal napływać nawet gdy OutGauge ucichnie
        // (auto stoi na torze, ale gracz jest np. w menu ustawień/garażu, albo pakiety
        // UDP OutGauge po prostu giną). Bez tej blokady drift/speeding wykrywałyby się
        // dalej z samego InSim, a burnout (który i tak wymaga RPM/gazu z OutGauge)
        // zostałby po prostu zamrożony na ostatnich wartościach — oba przypadki
        // mylące. Wołaj SetOutGaugeDataFresh(false/true) z zewnątrz (patrz
        // MainForm.OnCarData) PRZED każdym Update(car).
        private bool _outGaugeDataFresh = true;

        /// <summary>
        /// Informuje silnik, czy telemetria OutGauge jest aktualnie "świeża". Przy
        /// przejściu świeże→martwe CAŁY bieżący przejazd (combo, punkty, stan
        /// drift/speeding/burnout) jest ANULOWANY — nie tylko zamrożony na starych
        /// wartościach — a dopóki dane pozostają martwe, Update(car) w ogóle nie
        /// wykrywa nowego driftu/speedingu/burnoutu (patrz early-return w Update()).
        /// </summary>
        public void SetOutGaugeDataFresh(bool isFresh)
        {
            if (isFresh == _outGaugeDataFresh) return;
            _outGaugeDataFresh = isFresh;
            if (!isFresh)
                CancelActiveRun();
        }

        /// <summary>
        /// Anuluje bieżący przejazd/combo/stan aktywności BEZ zaliczania go jako
        /// zakończony — celowo NIE woła DriftEnded/RegisterRunEnd, bo to nie jest
        /// normalne zakończenie przejazdu, tylko przerwanie z powodu utraty danych.
        /// </summary>
        private void CancelActiveRun()
        {
            IsDrifting = false;
            IsSpeeding = false;
            IsBurnout = false;
            ComboMultiplier = 1;
            CurrentRunPoints = 0;
            _previouslyDrifting = false;

            // Znaczniki continuity combo drift↔speeding↔burnout (patrz Update()) —
            // reset, żeby po wznowieniu danych nic "nie pamiętało" przerwanego przejazdu.
            _lastDriftTime = DateTime.MinValue;
            _lastSpeedingTime = DateTime.MinValue;
            _lastBurnoutTime = DateTime.MinValue;

            // Stan śledzenia burnoutu (obrót/bączek) — reset, żeby po wznowieniu
            // danych nagła zmiana headingu sprzed przerwy nie policzyła się jako
            // fałszywy skok prędkości kątowej.
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

        public void Update(InSim.CompCar car)
        {
            // Zablokowane — patrz SetOutGaugeDataFresh. Stan już wyzerowany przez
            // CancelActiveRun() w momencie przejścia świeże→martwe, więc tu wystarczy
            // po prostu nic nie robić, dopóki dane nie wrócą.
            if (!_outGaugeDataFresh) return;

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

                // Combo/CurrentRunPoints przeżywają krótką przerwę (COMBO_TIMEOUT_SEC) między
                // drift↔speeding↔burnout — reset następuje dopiero gdy WSZYSTKIE trzy źródła
                // aktywności milczą dłużej niż limit (żadne z nich nie "trzyma" jeszcze combo).
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
                // ── Poziom "szybkiej jazdy" (1-5) i przyrost combo NA SEKUNDĘ ──────────
                // Wcześniej ComboMultiplier był tu NADPISYWANY sztywną wartością zależną
                // WYŁĄCZNIE od bieżącej prędkości — więc migał w górę/w dół razem z nią
                // zamiast rosnąć w czasie, inaczej niż przy drifcie/burnoucie (tam zawsze
                // DOKŁADA się coś do ComboMultiplier co klatkę). Teraz działa tak samo:
                // każdy poziom prędkości dodaje co sekundę więcej niż poprzedni
                // (FAST_DRIVE_COMBO_STEP_PER_SEC na poziom), z twardym limitem 10.0.
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
                if (!drifting)
                {
                    // Wewnątrz tego bloku speeding jest zawsze true (patrz warunek wejściowy
                    // `!drifting && speeding` powyżej) — gałąź "if (!speeding)", która tu
                    // wcześniej była, nigdy nie mogła się wykonać.
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

                    // ── kąt entry: start pomiaru dla nowej sekwencji driftu ──
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

                                    // Zaokrąglone RAZ do long i użyte wszędzie identycznie — wcześniej
                                    // CurrentRunPoints zaokrąglał bonusScore (Math.Round), a TotalScore/
                                    // LapScore go OBCINALI (rzutowanie double→long ucina, nie zaokrągla),
                                    // więc przy niecałkowitym bonusScore (typowe, bo mnożone przez
                                    // ComboMultiplier) CurrentRunPoints mógł się różnić o 1 punkt od
                                    // Total/Lap dla DOKŁADNIE tego samego zdarzenia.
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
                    // (driftTime jest lokalną zmienną przeliczaną od nowa z _driftStartTime przy
                    // każdym wejściu w ten blok — przypisanie jej tu do 0 nigdy nie było odczytywane)
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


        private DateTime _lastStatsSaveUtc = DateTime.MinValue;
        private static readonly TimeSpan StatsSaveInterval = TimeSpan.FromSeconds(2);

        // Called multiple times/sec during active scoring (fastPts/framePts/burnout) — the
        // in-memory stats dict is always kept current, but the disk write (JSON serialize +
        // File.WriteAllText) is throttled so it doesn't stall the caller's thread every frame.
        private void SaveCurrentDriver(bool force = false)
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

            if (!force && (DateTime.UtcNow - _lastStatsSaveUtc) < StatsSaveInterval)
                return;

            _lastStatsSaveUtc = DateTime.UtcNow;

            try
            {
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

        /// <summary>Forces an immediate stats save, bypassing the throttle — call on app shutdown.</summary>
        public void FlushStats() => SaveCurrentDriver(force: true);

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

        /// <summary>
        /// Dodaje/odejmuje punkty za uderzenie w obiekt (bonus/kara) albo za bonus kąta wejścia
        /// w drift. Dokłada DOKŁADNIE tę samą wartość do CurrentRunPoints, TotalScore i LapScore —
        /// wcześniej celowo pomijał CurrentRunPoints (i komentarz to zapowiadał), przez co po
        /// kolizji z obiektem podczas aktywnego driftu LapScore/TotalScore skakały w górę, a
        /// "RUN SCORE"/combo na HUD-zie (liczone z CurrentRunPoints) zostawały bez zmian —
        /// wyglądało to jak LapScore "wyprzedzający" bieżący przejazd. Teraz oba liczniki
        /// zawsze poruszają się razem.
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
        private const float BURNOUT_MIN_THROTTLE = 0.45f;

        // Warunki (prędkość + RPM + gaz + bieg) muszą trzymać się nieprzerwanie co
        // najmniej tyle sekund, zanim burnout zostanie uznany za aktywny i zacznie
        // liczyć punkty.
        private const double BURNOUT_ACTIVATE_SEC = 0.35;

        // Bazowe punkty na sekundę PO aktywacji (czyli już po upływie progu 3s),
        // mnożone przez rosnący ComboMultiplier (patrz UpdateBurnout).
        private const double BURNOUT_POINTS_PER_SECOND = 5.0;

        // Bonus za każdy pełny obrót (360°) podczas aktywnego burnoutu ("bączek").
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
        private const double STATIONARY_BURNOUT_MAX_SPEED_KMH = 1.0;
        private static readonly double[] STATIONARY_BURNOUT_TIERS_SEC = { 5, 10, 15, 20 };

        // Donut: full 360° while drifting, counted only if the car ends up back near where
        // the lap started (otherwise it's just a long drift, not a loop around a point).
        private const double DONUT_RADIUS_METERS = 15.0;
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

        // Telemetria silnika karmiona z zewnątrz (patrz UpdateEngineTelemetry) — InSim MCI
        // daje tylko rzeczywistą prędkość auta, więc RPM/gaz/bieg (z OutGauge) trzeba
        // dostarczyć osobno. OutGauge.Gear: 0=wsteczny, 1=jałowy, 2=1.bieg, 3=2.bieg...
        private float _lastRpm = 0f;
        private float _lastThrottle = 0f;
        private int _lastGear = 0;

        // Moment od którego warunki burnoutu (prędkość<BURNOUT_MAX_REAL_SPEED_KMH, bieg≥2,
        // gaz≥BURNOUT_MIN_THROTTLE, RPM≥BURNOUT_MIN_RPM — patrz UpdateBurnout) trzymają się
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
                    if ((now - _lastBurnoutSpinBonusTime).TotalSeconds > SPIN_COMBO_GAP_SEC)
                        _burnoutSpinComboCount = 0;
                    _burnoutSpinComboCount++;
                    _lastBurnoutSpinBonusTime = now;

                    string spinLabel = Localization.T("drift.label.burnout_spin")
                        + (_burnoutSpinComboCount > 1 ? $" x{_burnoutSpinComboCount}" : "");
                    AwardMilestoneBonus(spinLabel);
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
                _pendingDriftToBurnout = false;   // broke off before reaching the threshold

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
                double delta = headingDeg - _driftSpinLastHeadingDeg.Value;
                while (delta > 180) delta -= 360;
                while (delta < -180) delta += 360;
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
                    if ((now - _lastDonutBonusTime).TotalSeconds > SPIN_COMBO_GAP_SEC)
                        _donutComboCount = 0;
                    _donutComboCount++;
                    _lastDonutBonusTime = now;

                    string label = Localization.T("bonus.donut") + (_donutComboCount > 1 ? $" x{_donutComboCount}" : "");
                    AwardMilestoneBonus(label);
                }

                _driftSpinStartWasBurnout = isBurnout;   // start state for the next loop

                // start point for the next loop
                _driftSpinStartX = carXMeters;
                _driftSpinStartY = carYMeters;
            }
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