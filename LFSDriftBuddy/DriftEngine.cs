using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LFSDriftBuddy
{
    /// <summary>
    /// Calculates drift angle from CompCar data and awards points
    /// like Forza Horizon — rewards speed, angle, and sustained drifts.
    /// 
    /// InSim Direction/Heading: word, 0 = world Y axis, 32768 = 180 degrees.
    /// Drift angle = angular difference between motion direction and car heading.
    /// </summary>
    public class DriftEngine
    {
        // ── Drift scoring ───────────────────────────────────────
        private const double MIN_SPEED_KMH = 20.0;
        private const double MIN_DRIFT_ANGLE_DEG = 20.0;
        private const double MAX_DRIFT_ANGLE_DEG = 110.0;
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
        public long TotalScore { get; private set; }
        public double ComboMultiplier { get; private set; } = 1;
        public double SpeedKmh { get; private set; }
        public string LastAwardedText { get; private set; } = "";

        // ── Driver ──────────────────────────────────────────────
        public string CurrentDriver { get; private set; } = "default_driver";

        private readonly string _statsFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "driver_stats.json");

        private Dictionary<string, long> _driverScores = new();
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
        private double _dtSec = 0;
        private bool _previouslyDrifting = false;
        private bool _previouslyDriftSideRight = true;


        // ── Events ──────────────────────────────────────────────
        
        public event Action<long, double, string, string, string, DriftLabelKind>? DriftScored;
        public event Action? DriftStarted;
        public event Action<long>? DriftEnded;
        public event Action<long, long>? LapCompleted;

        public DriftEngine()
        {
            LoadStats();
            LoadLapRecords();
        }

        //

        // ────────────────────────────────────────────────────────
        //  Driver handling
        // ────────────────────────────────────────────────────────
        public void SetDriver(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                driverName = "default_driver";

            CurrentDriver = driverName;

            if (!_driverScores.ContainsKey(CurrentDriver))
                _driverScores[CurrentDriver] = 0;

            TotalScore = _driverScores[CurrentDriver];
            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);   // ← poprawione
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

        public void SetLayout(string layoutName)   // ← NOWE
        {
            _currentLayoutName = layoutName ?? "";
            ApplyTrackKey();
        }

        private void ApplyTrackKey()
        {
            string newKey = CombineTrackKey(_currentTrackCode, _currentLayoutName);
            if (_currentTrackKey == newKey) return;

            _currentTrackKey = newKey;

            MigrateDefaultLapRecordIfNeeded();   // ← NOWE

            BestLapScore = GetBestLapForDriver(CurrentDriver, _currentTrackCode, _currentLayoutName);
            LapScore = 0;
            HasActiveLapContext = false;
        }

        /// <summary>
        /// Jeśli wcześniej (przez pusty LName z InSim) najlepszy wynik dla tej trasy zapisał się
        /// pod kluczem "default", a teraz mamy poprawną nazwę layoutu bez własnego zapisu —
        /// przenieś wynik pod właściwy klucz zamiast zaczynać od zera.
        /// </summary>
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
            if (!string.IsNullOrEmpty(_currentTrackKey) && LapScore > BestLapScore)
            {
                BestLapScore = LapScore;
                SaveLapRecords();
            }

            LapCompleted?.Invoke(LapScore, BestLapScore);
            LapScore = 0;
            HasActiveLapContext = true;   // ← NOWE: od teraz ramka może być widoczna
        }

        // ────────────────────────────────────────────────────────
        //  Update
        // ────────────────────────────────────────────────────────

        int driftTimeMultipler = 0;
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

            // ── FAST DRIVE SCORING ─────────────────────────────

            if (_isHandBrakeON) { 
                eBrakeCount += 1;
                eDriftActive = true;
            }



            if (!speeding && !drifting) 
            { 
                IsSpeeding = false;
                LastAwardedText = $"";
                //IsDrifting = false;
                //_driftStartTime = now;
                double gap = (now - _lastDriftTime).TotalSeconds;
                double gap2 = (now - _lastSpeedingTime).TotalSeconds;

                if (gap <= COMBO_TIMEOUT_SEC)
                   {  }
                else
                {
                    if (gap2 <= COMBO_TIMEOUT_SEC)
                    { }
                    else
                    {
                        CurrentRunPoints = 0;
                        ComboMultiplier = 1;
                        eBrakeCount--;
                        if (eBrakeCount <= 0)
                        {
                            eDriftActive = false;
                            eBrakeCount = 0;
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
                else { 
                ComboMultiplier = 1; }
                

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
                double angleScore = AngleScore(DriftAngleDeg);
                double speedScore = SpeedScore(SpeedKmh);

                //if (_isHandBrakeON) { eBrakeCount+=2; }
                

                if (!_previouslyDrifting)
                {
                    _driftStartTime = now;
                    driftTimeMultipler = 0;
                    //CurrentRunPoints = 0;
                    if (!_isHandBrakeON) { eBrakeCount--; }
                    eDriftActive = false;
                    //
                    //

                    IsDrifting = true;
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
                                if(SpeedKmh >= 35 && DriftAngleDeg >= MIN_DRIFT_ANGLE_DEG) 
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
                        if (!speeding) {
                            
                            //CurrentRunPoints = 0; 
                        }
                    }
                    

                    DriftStarted?.Invoke();
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

                    
                    driftTimeMultipler++;

                    ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.036 * angleScore), 10.0), 2);
                    _driftStartTime = now;

                    if (driftTimeMultipler == 5) 
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.10 * angleScore), 10.0), 2);
                        
                    }
                    else if (driftTimeMultipler == 10)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.20 * angleScore), 10.0), 2);
                    }
                    else if (driftTimeMultipler == 15)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.30 * angleScore), 10.0), 2);
                    }
                    else if (driftTimeMultipler == 20)
                    {
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.40 * angleScore), 10.0), 2);
                    }

                    labelKindT = DriftLabelKind.DeepLongDrift;
                   bonusText = Localization.T(LabelKindToKey(labelKindT));


                    if (DriftAngleDeg >= 55)
                    {
                        deepAngleCount++;
                    } else deepAngleCount = 0;
                    if (deepAngleCount == 4) 
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.10 * angleScore), 10.0), 2);

                    }
                    else if (deepAngleCount == 8) 
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.20 * angleScore), 10.0), 2);
                    }
                    else if (deepAngleCount == 12) 
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.30 * angleScore), 10.0), 2);
                    }
                    else if (deepAngleCount == 16) 
                    {
                        TotalScore = TotalScore + (50 * deepAngleCount);
                        LapScore = LapScore + (50 * deepAngleCount);
                        LastAwardedText = $"{bonusText} - {Math.Round((double)500 * driftTimeMultipler / 1000, 1)}s + {50 * deepAngleCount}";
                        ComboMultiplier = Math.Round(Math.Min(ComboMultiplier + (0.40 * angleScore), 10.0), 2);
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
                

                //
                

                SaveCurrentDriver();
                

                
                
                if (!speeding) 
                {

                    
                    //LastAwardedText = FormatRunResult(CurrentRunPoints, ComboMultiplier);
                    //_driftStartTime = now;
                    //_lastDriftTime = now;
                    DriftEnded?.Invoke(CurrentRunPoints);



                    IsSpeeding = false;


                }
                
            }
            if (!drifting && _previouslyDrifting)
            {
                _lastDriftTime = now;


            }

            if (!speeding && !drifting && _previouslyDrifting)
            {

                

                if (ComboMultiplier>1) { }
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
                    _driverScores = new Dictionary<string, long>();
                    return;
                }

                string json = File.ReadAllText(_statsFile);

                _driverScores = JsonSerializer.Deserialize<Dictionary<string, long>>(json)
                                ?? new Dictionary<string, long>();
            }
            catch
            {
                _driverScores = new Dictionary<string, long>();
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
                _driverScores[CurrentDriver] = TotalScore;

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
            LongDrift
        }
        private DriftLabelKind GetLabelKind(double angle, double speed, bool brake)
        {
            if (speed >= FAST_DRIVE_THRESHOLD && angle < 8 && speed < FAST_DRIVE_THRESHOLD * 1.5)
                return DriftLabelKind.Fast1;
            if (speed >= FAST_DRIVE_THRESHOLD * 1.5 && angle < 8 && speed < FAST_DRIVE_THRESHOLD * 2.0)
                return DriftLabelKind.Fast2;
            if (speed >= FAST_DRIVE_THRESHOLD * 2.0 && angle < 8)
                return DriftLabelKind.Fast3;
            if (!brake) { 
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

        public void ResetTotal()
        {
            TotalScore = 0;
            LapScore = 0;
            ComboMultiplier = 1;
            CurrentRunPoints = 0;
            IsDrifting = false;
            IsSpeeding = false;
            _previouslyDrifting = false;
            _lastDriftTime = DateTime.MinValue;
            _lastSpeedingTime = DateTime.MinValue;

            SaveCurrentDriver();
        }
        public void ResetLapScore()
        {
            LapScore = 0;
        }

        /// <summary>Restart wyścigu — zeruje LapScore i chowa ramkę HUD do czasu 1. zaliczonego okrążenia.</summary>
        public void OnRaceRestarted()
        {
            LapScore = 0;
            HasActiveLapContext = false;
        }


    }
}
