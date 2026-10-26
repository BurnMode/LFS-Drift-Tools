using System;

namespace LFSDriftTools
{
    public sealed class AchievementUnlockedInfo
    {
        public AchievementUnlockedInfo(AchievementDefinition definition, InputMethod method)
        {
            Definition = definition;
            Method = method;
        }

        public AchievementDefinition Definition { get; }
        public InputMethod Method { get; }
    }

    public sealed class AchievementTracker
    {
        private const int SpeedConfirmSamples = 3;
        private const double AirborneUnlockSec = 1.5;
        private const double AirborneMinSpeedMs = 3.0;
        private const double AirborneMaxRollRad = 1.0;
        private const double RoofUnlockSec = 1.0;
        private const double KissMaxClosingKmh = 3.0;
        private const double TBoneMinClosingKmh = 130.0;
        private const int NightOwlFromHour = 2;
        private const int NightOwlToHour = 5;
        private const int DonutStreakTarget = 15;
        private const double SmokehouseUnlockSec = 15.0;
        private const double MarathonUnlockSec = 45.0;
        private const int CarouselStreakTarget = 15;
        private const double SidewaysMinDeg = 90.0;
        private const double SidewaysMaxDeg = 160.0;
        private const double SidewaysMinSpeedMs = 20.0 / 3.6;
        private const double SidewaysUnlockSec = 3.0;
        private const double ReverseMinSpeedMs = 60.0 / 3.6;
        private const double ReverseUnlockSec = 1.5;
        private const double WallMinAccel = 147.0;
        private const int WallConfirmSamples = 2;
        private const double WallContactWindowSec = 0.5;
        private const double HandbrakeMinKmh = 250.0;
        private const double HandbrakeSurviveSec = 5.0;
        private const double RevHoldGapSec = 0.6;
        private const double RevHoldUnlockSec = 15.0;
        private const double BlinkerGapSec = 1.2;
        private const double BlinkerUnlockMeters = 3000.0;
        private const double LaunchStandstillKmh = 1.0;
        private const double LaunchTargetKmh = 100.0;
        private const double LaunchMaxSec = 3.5;
        private const double LaunchStandstillHoldSec = 0.5;
        private const double LaunchMaxDropKmh = 5.0;
        private const double LaunchMaxAccel = 25.0;
        private const double LaunchImpactAccel = 30.0;
        private const double BarrelRollRad = 2.0 * Math.PI * 0.95;
        private const double BarrelLandedSec = 2.0;
        private const double BarrelResetGroundedSec = 1.0;
        private const double Air3UnlockSec = 4.0;
        private const double RoofLandingWindowSec = 2.0;
        private const double RoofSpawnWindowSec = 10.0;
        private const double WrongWayMinSpeedMs = 10.0 / 3.6;
        private const double WrongWayGapSec = 1.0;
        private const double WrongWayUnlockMeters = 1000.0;
        private const double StrikeWindowSec = 2.0;
        private const int StrikeObjects = 10;
        private const long Run100kPoints = 150_000;
        private const long Run250kPoints = 250_000;
        private const double ComboMaxValue = 9.99;
        private const double ComboHoldSec = 45.0;
        private const int BonusBoxesOnScreen = 6;
        private const double PileupWindowSec = 3.0;
        private const int PileupCars = 3;
        private const uint LightHandbrake = 0x0004;
        private const uint LightSignalLeft = 0x0020;
        private const uint LightSignalRight = 0x0040;
        private const int GearReverse = 0;
        private const int GearFirst = 2;

        private readonly AchievementStore _store;
        private readonly InputMethodDetector _input;
        private readonly object _sync = new();

        private int _speed200Samples;
        private int _speed300Samples;
        private double _airborneSec;
        private double _roofSec;
        private DateTime _lastOutSimUtc = DateTime.MinValue;
        private int _sessionResets;
        private volatile int _gear = 1;

        private double _sidewaysSec;
        private double _reverseSec;
        private int _highGSamples;
        private DateTime? _wallCandidateUtc;
        private DateTime _lastContactUtc = DateTime.MinValue;

        private DateTime? _handbrakeWindowUtc;
        private bool _handbrakeSpoiled;

        private DateTime _lastRevCutUtc = DateTime.MinValue;
        private DateTime _revHoldStartUtc = DateTime.MinValue;

        private DateTime _lastOutGaugeUtc = DateTime.MinValue;
        private int _blinkerSide;
        private DateTime _blinkerLastOnUtc = DateTime.MinValue;
        private double _blinkerMeters;

        private bool _launchArmed;
        private DateTime? _launchStartUtc;
        private DateTime? _launchStandstillSinceUtc;
        private double _launchPeakKmh;
        private double _launchPrevKmh;

        private DateTime _lastFlightUtc = DateTime.MinValue;
        private DateTime _lastResetUtc = DateTime.MinValue;
        private double _airTotalFraction;
        private double _wrongWayMeters;
        private DateTime _wrongWayLastUtc = DateTime.MinValue;
        private readonly System.Collections.Generic.Queue<DateTime> _recentObjectHits = new();
        private readonly System.Collections.Generic.List<(byte Plid, DateTime Utc)> _recentContacts = new();
        private DateTime? _comboMaxSinceUtc;
        private volatile bool _inPitLane;

        private double? _lastRoll;
        private double _rollAccum;
        private bool _rolledFull;
        private double _groundedSec;

        public AchievementTracker(AchievementStore store, InputMethodDetector input)
        {
            _store = store;
            _input = input;
        }

        public bool Enabled { get; set; } = true;
        public Func<bool>? OwnCarInView { get; set; }
        public Func<bool>? IsUpsideDown { get; set; }
        public Func<string>? CurrentCar { get; set; }
        public Func<string>? CurrentTrack { get; set; }

        public event Action<AchievementUnlockedInfo>? Unlocked;

        private bool Active => Enabled && (OwnCarInView?.Invoke() ?? true);

        public void OnOutGauge(double speedKmh, int gear, uint showLights)
        {
            if (!Active) return;
            _gear = gear;

            bool speed200 = false, speed300 = false, launch = false, handbrake = false, blinker = false;
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                double dt = _lastOutGaugeUtc == DateTime.MinValue ? 0 : (now - _lastOutGaugeUtc).TotalSeconds;
                _lastOutGaugeUtc = now;
                if (dt < 0 || dt > 0.25) dt = 0;

                _speed200Samples = speedKmh >= 200.0 ? _speed200Samples + 1 : 0;
                _speed300Samples = speedKmh >= 300.0 ? _speed300Samples + 1 : 0;
                speed200 = _speed200Samples >= SpeedConfirmSamples;
                speed300 = _speed300Samples >= SpeedConfirmSamples;

                if (speedKmh < LaunchStandstillKmh)
                {
                    _launchStartUtc = null;
                    _launchStandstillSinceUtc ??= now;
                    if ((now - _launchStandstillSinceUtc.Value).TotalSeconds >= LaunchStandstillHoldSec)
                        _launchArmed = true;
                }
                else
                {
                    _launchStandstillSinceUtc = null;
                    if (_launchArmed && _launchStartUtc == null)
                    {
                        _launchStartUtc = now;
                        _launchPeakKmh = speedKmh;
                    }
                }
                if (_launchStartUtc != null)
                {
                    double t = (now - _launchStartUtc.Value).TotalSeconds;
                    double accel = dt > 0 ? (speedKmh - _launchPrevKmh) / 3.6 / dt : 0;
                    _launchPeakKmh = Math.Max(_launchPeakKmh, speedKmh);
                    bool spoiled = gear < GearFirst
                        || speedKmh < _launchPeakKmh - LaunchMaxDropKmh
                        || accel > LaunchMaxAccel;
                    if (spoiled)
                    {
                        CancelLaunch();
                    }
                    else if (speedKmh >= LaunchTargetKmh)
                    {
                        launch = t <= LaunchMaxSec;
                        CancelLaunch();
                    }
                    else if (t > LaunchMaxSec)
                    {
                        CancelLaunch();
                    }
                }
                _launchPrevKmh = speedKmh;

                bool handbrakeOn = (showLights & LightHandbrake) != 0;
                if (handbrakeOn && speedKmh >= HandbrakeMinKmh && _handbrakeWindowUtc == null)
                {
                    _handbrakeWindowUtc = now;
                    _handbrakeSpoiled = false;
                }
                if (_handbrakeWindowUtc != null && (now - _handbrakeWindowUtc.Value).TotalSeconds >= HandbrakeSurviveSec)
                {
                    handbrake = !_handbrakeSpoiled;
                    _handbrakeWindowUtc = null;
                }

                bool left = (showLights & LightSignalLeft) != 0;
                bool right = (showLights & LightSignalRight) != 0;
                int side = left && !right ? 1 : right && !left ? 2 : 0;
                if (left && right)
                {
                    _blinkerSide = 0;
                    _blinkerMeters = 0;
                }
                else if (side != 0)
                {
                    if (side != _blinkerSide)
                    {
                        _blinkerSide = side;
                        _blinkerMeters = 0;
                    }
                    _blinkerLastOnUtc = now;
                }
                if (_blinkerSide != 0)
                {
                    if ((now - _blinkerLastOnUtc).TotalSeconds > BlinkerGapSec)
                    {
                        _blinkerSide = 0;
                        _blinkerMeters = 0;
                    }
                    else
                    {
                        _blinkerMeters += speedKmh / 3.6 * dt;
                        blinker = _blinkerMeters >= BlinkerUnlockMeters;
                    }
                }
            }

            if (speed200) Unlock(AchievementCatalog.Speed200);
            if (speed300) Unlock(AchievementCatalog.Speed300);
            if (launch) Unlock(AchievementCatalog.Launch);
            if (handbrake) Unlock(AchievementCatalog.Handbrake250);
            if (blinker) Unlock(AchievementCatalog.Blinker2Km);
        }

        private void CancelLaunch()
        {
            _launchArmed = false;
            _launchStartUtc = null;
        }

        private void ResetLaunch()
        {
            CancelLaunch();
            _launchStandstillSinceUtc = null;
        }

        public void OnOutSim(OutSimSample sample)
        {
            if (!Active) return;

            bool airUnlock = false, roofUnlock = false, sidewaysUnlock = false, reverseUnlock = false, wallUnlock = false, barrelUnlock = false;
            bool air3Unlock = false, roofLandingUnlock = false, roofSpawnUnlock = false, wrongWayUnlock = false;
            int airSecondsGained = 0;
            lock (_sync)
            {
                DateTime now = sample.ReceivedAtUtc;
                double dt = _lastOutSimUtc == DateTime.MinValue ? 0 : (now - _lastOutSimUtc).TotalSeconds;
                _lastOutSimUtc = now;
                if (dt < 0 || dt > 0.25) dt = 0;

                bool upsideDown = IsUpsideDown?.Invoke() ?? false;
                bool hasWheels = sample.HasWheels && sample.Wheels != null && sample.Wheels.Length >= 4;
                bool allTouching = hasWheels;
                bool noneTouching = hasWheels;
                if (hasWheels)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (sample.Wheels![i].Touching) noneTouching = false;
                        else allTouching = false;
                    }
                }

                bool airborne = noneTouching && !upsideDown && Math.Abs(sample.Roll) < AirborneMaxRollRad && sample.Vel.Length() > AirborneMinSpeedMs;
                _airborneSec = airborne ? _airborneSec + dt : 0;
                airUnlock = _airborneSec >= AirborneUnlockSec;
                air3Unlock = _airborneSec >= Air3UnlockSec;
                if (_airborneSec >= AirborneUnlockSec) _lastFlightUtc = now;
                if (airborne)
                {
                    _airTotalFraction += dt;
                    while (_airTotalFraction >= 1.0)
                    {
                        _airTotalFraction -= 1.0;
                        airSecondsGained++;
                    }
                }

                _roofSec = upsideDown ? _roofSec + dt : 0;
                roofUnlock = _roofSec >= RoofUnlockSec;
                if (upsideDown)
                {
                    _handbrakeSpoiled = true;
                    roofLandingUnlock = (now - _lastFlightUtc).TotalSeconds <= RoofLandingWindowSec;
                    roofSpawnUnlock = (now - _lastResetUtc).TotalSeconds <= RoofSpawnWindowSec;
                }

                float c = MathF.Cos(-sample.Heading);
                float s = MathF.Sin(-sample.Heading);
                float rightVel = sample.Vel.X * c - sample.Vel.Y * s;
                float forwardVel = sample.Vel.X * s + sample.Vel.Y * c;
                double planarSpeed = Math.Sqrt(rightVel * rightVel + forwardVel * forwardVel);
                double slipDeg = planarSpeed > 0.5 ? Math.Abs(Math.Atan2(rightVel, forwardVel)) * 180.0 / Math.PI : 0;

                bool sideways = planarSpeed > SidewaysMinSpeedMs && slipDeg >= SidewaysMinDeg && slipDeg <= SidewaysMaxDeg;
                _sidewaysSec = sideways ? _sidewaysSec + dt : 0;
                sidewaysUnlock = _sidewaysSec >= SidewaysUnlockSec;

                bool reversing = _gear == GearReverse && forwardVel < -ReverseMinSpeedMs;
                _reverseSec = reversing ? _reverseSec + dt : 0;
                reverseUnlock = _reverseSec >= ReverseUnlockSec;

                if (_gear == GearReverse && forwardVel < -WrongWayMinSpeedMs)
                {
                    _wrongWayMeters += -forwardVel * dt;
                    _wrongWayLastUtc = now;
                }
                else if ((now - _wrongWayLastUtc).TotalSeconds > WrongWayGapSec)
                {
                    _wrongWayMeters = 0;
                }
                wrongWayUnlock = _wrongWayMeters >= WrongWayUnlockMeters;

                double horizontalAccel = Math.Sqrt(sample.Accel.X * sample.Accel.X + sample.Accel.Y * sample.Accel.Y);
                _highGSamples = horizontalAccel >= WallMinAccel ? _highGSamples + 1 : 0;
                if (horizontalAccel >= LaunchImpactAccel) ResetLaunch();
                if (_highGSamples >= WallConfirmSamples && _wallCandidateUtc == null)
                {
                    _wallCandidateUtc = now;
                    _handbrakeSpoiled = true;
                }
                if (_wallCandidateUtc != null && (now - _wallCandidateUtc.Value).TotalSeconds >= WallContactWindowSec)
                {
                    wallUnlock = Math.Abs((_lastContactUtc - _wallCandidateUtc.Value).TotalSeconds) > WallContactWindowSec;
                    _wallCandidateUtc = null;
                }

                double roll = sample.Roll;
                if (_lastRoll.HasValue)
                {
                    double delta = roll - _lastRoll.Value;
                    while (delta > Math.PI) delta -= 2.0 * Math.PI;
                    while (delta < -Math.PI) delta += 2.0 * Math.PI;
                    _rollAccum += delta;
                }
                _lastRoll = roll;
                if (Math.Abs(_rollAccum) >= BarrelRollRad) _rolledFull = true;

                bool grounded = allTouching && !upsideDown;
                if (grounded)
                {
                    _groundedSec += dt;
                    if (_rolledFull && _groundedSec >= BarrelLandedSec)
                    {
                        barrelUnlock = true;
                        _rolledFull = false;
                        _rollAccum = 0;
                    }
                    else if (!_rolledFull && _groundedSec >= BarrelResetGroundedSec)
                    {
                        _rollAccum = 0;
                    }
                }
                else
                {
                    _groundedSec = 0;
                }
            }
            if (airUnlock) Unlock(AchievementCatalog.Air1);
            if (roofUnlock) Unlock(AchievementCatalog.Roof);
            if (sidewaysUnlock) Unlock(AchievementCatalog.Drift90);
            if (reverseUnlock) Unlock(AchievementCatalog.Reverse60);
            if (wallUnlock) Unlock(AchievementCatalog.Wall);
            if (barrelUnlock) Unlock(AchievementCatalog.BarrelRoll);
            if (air3Unlock) Unlock(AchievementCatalog.Air3);
            if (roofLandingUnlock) Unlock(AchievementCatalog.RoofLanding);
            if (roofSpawnUnlock) Unlock(AchievementCatalog.RoofSpawn);
            if (wrongWayUnlock) Unlock(AchievementCatalog.Reverse500m);
            for (int i = 0; i < airSecondsGained; i++) Count(AchievementCatalog.AirTotal60);
        }

        public void OnDriftStarted()
        {
            if (!Active) return;
            int hour = DateTime.Now.Hour;
            if (hour >= NightOwlFromHour && hour < NightOwlToHour) Unlock(AchievementCatalog.NightOwl);
        }

        public void OnDriftEnded(long points)
        {
            if (!Active || points <= 0) return;
            Unlock(AchievementCatalog.DriftFirst);
            if (points >= Run100kPoints) Unlock(AchievementCatalog.Run100k);
            if (points >= Run250kPoints) Unlock(AchievementCatalog.Run250k);
        }

        public void OnDonut(int streak)
        {
            if (!Active) return;
            if (streak >= DonutStreakTarget) Unlock(AchievementCatalog.Donut10);
        }

        public void OnBurnoutState(double activeSeconds, double stationarySeconds)
        {
            if (!Active) return;
            if (stationarySeconds >= SmokehouseUnlockSec) Unlock(AchievementCatalog.BurnoutSmokehouse);
            if (activeSeconds >= MarathonUnlockSec) Unlock(AchievementCatalog.BurnoutMarathon);
        }

        public void OnBurnoutSpin(int streak)
        {
            if (!Active) return;
            if (streak >= CarouselStreakTarget) Unlock(AchievementCatalog.BurnoutCarousel);
        }

        public void OnBurnoutToDrift()
        {
            if (!Active) return;
            Unlock(AchievementCatalog.BurnoutToDrift);
        }

        public void OnRevCut()
        {
            if (!Active) return;
            bool hold;
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastRevCutUtc).TotalSeconds > RevHoldGapSec) _revHoldStartUtc = now;
                _lastRevCutUtc = now;
                hold = (now - _revHoldStartUtc).TotalSeconds >= RevHoldUnlockSec;
            }
            Count(AchievementCatalog.RevCuts1000);
            if (hold) Unlock(AchievementCatalog.RevHold10);
        }

        public void OnObjectHit(string objectName)
        {
            if (!Active || string.IsNullOrEmpty(objectName)) return;
            bool strike;
            lock (_sync)
            {
                ResetLaunch();
                _handbrakeSpoiled = true;
                DateTime now = DateTime.UtcNow;
                _recentObjectHits.Enqueue(now);
                while (_recentObjectHits.Count > 0 && (now - _recentObjectHits.Peek()).TotalSeconds > StrikeWindowSec)
                    _recentObjectHits.Dequeue();
                strike = _recentObjectHits.Count >= StrikeObjects;
            }

            string name = objectName.ToUpperInvariant();
            if (name.Contains("CONE"))
            {
                Count(AchievementCatalog.Cones100);
                Count(AchievementCatalog.Cones1000);
            }
            else if (name.Contains("TYRE"))
                Count(AchievementCatalog.Tyres500);
            else if (IsBarrier(name))
                Count(AchievementCatalog.Barriers250);
            else if (IsSign(name))
                Count(AchievementCatalog.Signs200);
            else if (name == "BIN")
                Count(AchievementCatalog.Bins50);
            else if (name == "HAY BALE")
                Count(AchievementCatalog.Hay100);
            else if (name == "AMBULANCE")
                Unlock(AchievementCatalog.AmbulanceHit);

            if (strike) Unlock(AchievementCatalog.Strike);
        }

        private static bool IsBarrier(string name) =>
            name.Contains("BARRIER") || name == "RAILING" || name.Contains("WALL") || name == "CONCRETE PILLAR";

        private static bool IsSign(string name) =>
            name.Contains("SIGN") || name.Contains("MARKER") || name == "POST" || name == "LETTER BOARD" || name == "BANNER";

        public void OnCarContact(double closingSpeedKmh, byte otherPlid)
        {
            if (!Active) return;
            bool pileup;
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                _lastContactUtc = now;
                _handbrakeSpoiled = true;
                ResetLaunch();
                _recentContacts.RemoveAll(c => (now - c.Utc).TotalSeconds > PileupWindowSec);
                if (otherPlid != 0) _recentContacts.Add((otherPlid, now));
                var distinct = new System.Collections.Generic.HashSet<byte>();
                foreach (var c in _recentContacts) distinct.Add(c.Plid);
                pileup = distinct.Count >= PileupCars;
            }
            if (closingSpeedKmh >= 0 && closingSpeedKmh < KissMaxClosingKmh)
                Unlock(AchievementCatalog.Kiss);
            if (closingSpeedKmh >= TBoneMinClosingKmh)
                Unlock(AchievementCatalog.TBone);
            if (_inPitLane)
                Unlock(AchievementCatalog.PitContact);
            if (pileup)
                Unlock(AchievementCatalog.Pileup3);
        }

        public void SetInPitLane(bool inPitLane) => _inPitLane = inPitLane;

        public void OnCombo(double combo)
        {
            if (!Active) return;
            bool maxed = combo >= ComboMaxValue;
            bool hold = false;
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                if (!maxed) _comboMaxSinceUtc = null;
                else
                {
                    _comboMaxSinceUtc ??= now;
                    hold = (now - _comboMaxSinceUtc.Value).TotalSeconds >= ComboHoldSec;
                }
            }
            if (maxed) Unlock(AchievementCatalog.ComboMaxed);
            if (hold) Unlock(AchievementCatalog.ComboHold30);
        }

        public void OnBonusesOnScreen(int count)
        {
            if (!Active) return;
            if (count >= BonusBoxesOnScreen) Unlock(AchievementCatalog.Bonus5);
        }

        public void OnCarReset()
        {
            if (!Active) return;
            int resets;
            lock (_sync)
            {
                resets = ++_sessionResets;
                _handbrakeSpoiled = true;
                _lastResetUtc = DateTime.UtcNow;
                ResetLaunch();
                _rolledFull = false;
                _rollAccum = 0;
            }
            var def = AchievementCatalog.Find(AchievementCatalog.Resets20);
            if (def != null && resets >= def.Target) Unlock(AchievementCatalog.Resets20);
        }

        public void ResetSession()
        {
            lock (_sync)
            {
                _sessionResets = 0;
                _speed200Samples = 0;
                _speed300Samples = 0;
                _airborneSec = 0;
                _roofSec = 0;
                _sidewaysSec = 0;
                _reverseSec = 0;
                _highGSamples = 0;
                _wallCandidateUtc = null;
                _handbrakeWindowUtc = null;
                _blinkerSide = 0;
                _blinkerMeters = 0;
                ResetLaunch();
                _lastRoll = null;
                _rollAccum = 0;
                _rolledFull = false;
                _groundedSec = 0;
                _wrongWayMeters = 0;
                _recentObjectHits.Clear();
                _recentContacts.Clear();
                _comboMaxSinceUtc = null;
            }
        }

        private void Count(string id)
        {
            var def = AchievementCatalog.Find(id);
            if (def == null) return;
            var method = _input.Current;
            if (_store.IsUnlocked(id, method)) return;
            long value = _store.AddCounter(id, method, 1);
            if (def.Target > 0 && value >= def.Target) Unlock(id, method);
        }

        private void Unlock(string id) => Unlock(id, _input.Current);

        private void Unlock(string id, InputMethod method)
        {
            var def = AchievementCatalog.Find(id);
            if (def == null) return;
            if (_store.IsUnlocked(id, method)) return;
            string car = CurrentCar?.Invoke() ?? "";
            string track = CurrentTrack?.Invoke() ?? "";
            if (_store.TryUnlock(id, method, car, track))
                Unlocked?.Invoke(new AchievementUnlockedInfo(def, method));
        }
    }
}
