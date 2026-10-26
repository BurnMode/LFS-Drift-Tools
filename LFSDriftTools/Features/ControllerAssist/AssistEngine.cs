using System;

namespace LFSDriftTools
{
    public class AssistTelemetrySnapshot
    {
        public double RawSteerPercent;
        public double OutputSteerPercent;
        public double SpeedKmh;
        public double RearSlipDeg;
        public double AssistContribution;
        public double RumbleLeft01;
        public double RumbleRight01;
        public double SurfaceChange01;
        public bool CarDataFresh;
    }

    public class AssistTrace
    {
        public double DtMs;
        public bool SampleNew;
        public double DataAgeMs;
        public bool DataFresh;
        public double SpeedKmh;
        public double RawPercent;
        public double PlayerTargetPercent;
        public double StickAngleDeg;
        public double TargetAngleDeg;
        public double FrontSlipRawDeg;
        public double FrontSlipDeg;
        public double RearSlipDeg;
        public double BodySlipDeg;
        public double YawRateDegS;
        public double FrontSteerDeg;
        public double InputSteerDeg;
        public double Blend;
        public double Handover;
        public double HoldWeight;
        public double AngleErrorDeg;
        public double ServoRawPercent;
        public double ServoSmoothedPercent;
        public double DesiredOffsetPercent;
        public double AssistOffsetPercent;
        public double TargetPercent;
        public double OutputPercent;
        public double BodySlipGrowthDegS;
        public double SpinCatchPercent;
        public readonly double[] WheelSlipDeg = new double[4];
        public readonly double[] WheelLoadN = new double[4];
        public readonly double[] WheelSlipRatio = new double[4];
        public readonly bool[] WheelTouching = new bool[4];
        public double SteerTorqueNm;
        public bool SteerTorqueValid;
        public double ThrottlePercent;
        public double BrakePercent;
        public int Gear;
        public double PadLeftTriggerPercent;
        public double PadRightTriggerPercent;
        public double LimitedPlayerPercent;
        public double StraightStabilityPercent;
        public double ThrottleCutPercent;
        public double ThrottleOutPercent;
        public double TractionSlipRatio;
        public double TractionCutPercent;
    }

    public class AssistEngine
    {
        public AssistTrace Trace { get; } = new();

        public AssistSettings Settings { get; set; } = AssistSettings.ForPreset(AssistPreset.Street);
        public RumbleSettings Rumble { get; set; } = new();

        public bool MasterEnabled { get; set; } = true;
        public bool RumbleEnabled { get; set; } = true;

        public double AssistCharacterPercent { get; set; } = 0.0;

        private double DriftBias => Math.Max(0, Clamp(AssistCharacterPercent, -100, 100) / 100.0);
        private double StabilityBias => Math.Max(0, -Clamp(AssistCharacterPercent, -100, 100) / 100.0);

        private volatile AssistTelemetrySnapshot _lastSnapshot = new();

        public AssistTelemetrySnapshot LastSnapshot
        {
            get => _lastSnapshot;
            private set => _lastSnapshot = value;
        }

        private AssistSettings _settings = null!;
        private RumbleSettings _rumble = null!;
        private bool _needsSync = true;

        public void Reset()
        {
            _needsSync = true;
            _smoothedOutput = 0;
            _servoSmoothed = 0;
            _servoInitialized = false;
            _limitedTargetInitialized = false;
            _targetAngleInitialized = false;
            _handoverInitialized = false;
            _rateLimitedPlayerInitialized = false;
            _lastTickUtc = DateTime.MinValue;
            _frontSlipEma = 0;
            ResetWeightedFrontSlip();
            ResetBodySlipGrowth();

            _hasPrevMotionSample = false;
            _lastAccelMagG = 0;
            _lastYawRateRadS = 0;
            _loadEnvelope = 0;

            _hasPrevSuspSample = false;
            for (int w = 0; w < 4; w++)
            {
                _lastSuspDeflect[w] = 0;
                _roadEma[w] = 0;
                _roadEmaSlow[w] = 0;
                _impactEnvelope[w] = 0;
                _surfaceChangeEnvelope[w] = 0;
                _lastRate[w] = 0;
            }

            _collisionEnvelopeLeft = 0;
            _collisionEnvelopeRight = 0;
            _lastGear = -999;
            _shiftEnvelope = 0;

            _prevSampleUtc = DateTime.MinValue;
            _prevSampleTimeMs = 0;
            _sampleIsNew = false;
            _sampleDt = 0;
        }

        private double _smoothedOutput;
        private double _servoSmoothed;
        private bool _servoInitialized;
        private DateTime _lastTickUtc = DateTime.MinValue;

        private double _frontSlipEma;
        private double _limitedTarget;
        private bool _limitedTargetInitialized;
        private double _targetAngleLimited;
        private bool _targetAngleInitialized;
        private double _handoverSmoothed;
        private bool _handoverInitialized;

        private double _lastWeightedFrontSlip;
        private bool _hasWeightedFrontSlip;

        private double LoadWeightedFrontSlip(CarState car)
        {
            if (car.FrontSlipWeightedValid)
            {
                _lastWeightedFrontSlip = car.FrontSlipWeightedRad;
                _hasWeightedFrontSlip = true;
                return _lastWeightedFrontSlip;
            }
            return _hasWeightedFrontSlip ? _lastWeightedFrontSlip : car.FrontSlipAngleRad;
        }

        private void ResetWeightedFrontSlip()
        {
            _hasWeightedFrontSlip = false;
            _lastWeightedFrontSlip = 0;
        }

        private bool _hasPrevMotionSample;
        private double _lastAccelMagG;
        private double _lastYawRateRadS;
        private double _loadEnvelope;

        private DateTime _prevSampleUtc = DateTime.MinValue;
        private uint _prevSampleTimeMs;
        private bool _sampleIsNew;
        private double _sampleDt;
        private readonly double[] _lastRate = new double[4];

        private bool SampleDtValid => _sampleDt >= 0.001 && _sampleDt <= 0.25;

        private void UpdateSampleInfo(CarState car)
        {
            _sampleIsNew = false;
            _sampleDt = 0;

            if (!car.DataFresh)
            {
                _prevSampleUtc = DateTime.MinValue;
                _prevSampleTimeMs = 0;
                return;
            }

            if (car.SampleReceivedUtc == _prevSampleUtc) return;

            if (car.SampleTimeMs != 0 && _prevSampleTimeMs != 0 &&
                car.SampleTimeMs > _prevSampleTimeMs && car.SampleTimeMs - _prevSampleTimeMs <= 250)
                _sampleDt = (car.SampleTimeMs - _prevSampleTimeMs) / 1000.0;
            else if (_prevSampleUtc != DateTime.MinValue)
                _sampleDt = (car.SampleReceivedUtc - _prevSampleUtc).TotalSeconds;

            _prevSampleUtc = car.SampleReceivedUtc;
            _prevSampleTimeMs = car.SampleTimeMs;
            _sampleIsNew = true;
        }

        private bool _hasPrevSuspSample;
        private readonly double[] _lastSuspDeflect = new double[4];
        private readonly double[] _roadEma = new double[4];
        private readonly double[] _roadEmaSlow = new double[4];
        private readonly double[] _impactEnvelope = new double[4];
        private readonly double[] _surfaceChangeEnvelope = new double[4];

        private double _collisionEnvelopeLeft;
        private double _collisionEnvelopeRight;

        private int _lastGear = -999;
        private double _shiftEnvelope;

        public double Process(double rawSteeringPercent, CarState car)
        {
            _settings = Settings;
            _rumble = Rumble;

            DateTime now = DateTime.UtcNow;
            double dt = _lastTickUtc == DateTime.MinValue
                ? 0.004
                : Math.Max(0.0005, Math.Min(0.1, (now - _lastTickUtc).TotalSeconds));
            _lastTickUtc = now;
            _lastDt = dt;

            double raw = Clamp(rawSteeringPercent / 100.0, -1, 1);

            if (_needsSync)
            {
                _smoothedOutput = raw;
                _needsSync = false;
            }

            UpdateSampleInfo(car);
            UpdateBodySlipGrowth(car);

            var (loadIntensity, accelJerkGPerSec) = ComputeLoadIntensity(car, dt);
            var (roadL, roadR, impactL, impactR, surfaceL, surfaceR) = ComputeSuspensionChannels(car, dt);
            var (collisionL, collisionR) = ComputeCollisionBoost(accelJerkGPerSec, car.LateralAccelG, dt);
            impactL = Math.Max(impactL, collisionL);
            impactR = Math.Max(impactR, collisionR);
            var (shiftL, shiftR) = ComputeShiftChannel(car, dt);

            if (!MasterEnabled || _settings.Preset == AssistPreset.Off)
            {
                _smoothedOutput = raw;
                _servoInitialized = false;
                _limitedTargetInitialized = false;
                _targetAngleInitialized = false;
                _handoverInitialized = false;
                _rateLimitedPlayerInitialized = false;
                ClearAssistTrace();
                FillCommonTrace(dt, car, raw, raw, 0, 0, raw, raw);
                Trace.LimitedPlayerPercent = raw * 100.0;
                Trace.StraightStabilityPercent = 0;
                var (rl, rr) = CombineRumble(loadIntensity, car.LateralAccelG, roadL, roadR, impactL, impactR, surfaceL, surfaceR, shiftL, shiftR);
                PublishSnapshot(raw, raw, car, 0, rl, rr, Math.Max(surfaceL, surfaceR));
                return raw * 100.0;
            }

            double dz = Clamp(_settings.DeadzonePercent / 100.0, 0, 0.5);
            double deadzoned = Math.Abs(raw) <= dz ? 0 : Math.Sign(raw) * (Math.Abs(raw) - dz) / (1 - dz);
            double shaped = Math.Sign(deadzoned) * Math.Pow(Math.Abs(deadzoned), Math.Max(0.2, _settings.ResponseCurve));

            double driftBias = DriftBias;
            double stabilityBias = StabilityBias;
            double gainScale = 1.0 + 0.5 * driftBias;
            double strengthScale = 1.0 + 0.5 * driftBias;
            double baseHighSpeedFraction = Clamp(_settings.MinHighSpeedSteerFraction, 0, 1);
            double minHighSpeedFraction = Clamp(baseHighSpeedFraction
                + (1.0 - baseHighSpeedFraction) * 0.3 * driftBias
                - baseHighSpeedFraction * 0.4 * stabilityBias, 0, 1);
            double slipActivationDeg = Math.Max(0, _settings.SlipActivationDeg) * (1.0 - 0.5 * stabilityBias);
            double slipFullDeg = _settings.SlipFullDeg * (1.0 - 0.3 * stabilityBias);
            double outputSmoothingMs = Math.Max(0, _settings.SmoothingMs) * (1.0 + 0.5 * stabilityBias);
            double angleMaxDeg = Clamp(Math.Max(1.0, _settings.TargetAngleMaxDeg) * (1.0 + 0.25 * driftBias - 0.4 * stabilityBias), 1.0, 135.0);
            double angleSmoothingMs = Math.Max(0, _settings.TargetAngleSmoothingMs) * (1.0 - 0.4 * driftBias + 0.8 * stabilityBias);

            double speedFactor = car.DataFresh
                ? Clamp(car.SpeedKmh / Math.Max(1, _settings.SpeedSensitivityRefKmh), 0, 1)
                : 0;
            double maxSteerFraction = 1.0 - speedFactor * (1.0 - minHighSpeedFraction);
            double playerTarget = shaped * maxSteerFraction;

            const double FrontSlipEmaTauMs = 35.0;
            if (car.DataFresh)
            {
                double slipAlpha = 1.0 - Math.Exp(-dt * 1000.0 / FrontSlipEmaTauMs);
                _frontSlipEma += (LoadWeightedFrontSlip(car) - _frontSlipEma) * slipAlpha;
            }
            else
            {
                _frontSlipEma = 0;
                ResetWeightedFrontSlip();
            }

            double frontSlipDeg = car.DataFresh ? _frontSlipEma * 180.0 / Math.PI : 0;
            double activationDeg = slipActivationDeg;
            double fullDeg = Math.Max(activationDeg + 0.1, slipFullDeg);
            double blend = Clamp((Math.Abs(frontSlipDeg) - activationDeg) / (fullDeg - activationDeg), 0, 1);

            double frontSlipRad = frontSlipDeg * Math.PI / 180.0;
            double countersteer = -Math.Sign(frontSlipRad) * Math.Min(1.0, Math.Abs(frontSlipRad) * _settings.CountersteerGain * gainScale);

            const double hardZeroKmh = 1.0;
            double rampSpanKmh = Math.Max(0.1, _settings.MinActivationSpeedKmh - hardZeroKmh);
            double lowSpeedGate = car.DataFresh
                ? Clamp((car.SpeedKmh - hardZeroKmh) / rampSpanKmh, 0, 1)
                : 0;

            const double FreshnessGraceSeconds = 0.05;
            const double FreshnessFadeSeconds = 0.2;
            double freshnessFade = Clamp(1.0 - (car.DataAgeSeconds - FreshnessGraceSeconds) / FreshnessFadeSeconds, 0, 1);

            double target;
            double assistContribution;

            if (_settings.Mode == SteeringAssistMode.TargetAngle)
            {
                _rateLimitedPlayerInitialized = false;

                double angleShaped = Math.Sign(deadzoned) * Math.Pow(Math.Abs(deadzoned), Math.Max(0.2, _settings.TargetAngleCurve));
                double targetAngleGain = Math.Max(0.05, _settings.TargetAngleGain) * gainScale;
                double stickTargetRad = angleShaped * maxSteerFraction * angleMaxDeg * Math.PI / 180.0;
                double stickRateRad = Math.Max(0, _settings.TargetAngleStickRateDegPerSec) * Math.PI / 180.0;
                if (stickRateRad <= 0 || !_targetAngleInitialized)
                {
                    _targetAngleLimited = stickTargetRad;
                    _targetAngleInitialized = true;
                }
                else
                {
                    double stickStep = stickRateRad * dt;
                    _targetAngleLimited += Clamp(stickTargetRad - _targetAngleLimited, -stickStep, stickStep);
                }
                double targetAngleRad = _targetAngleLimited;

                double releaseMs = Math.Max(0, _settings.TargetAngleHandoverReleaseMs);
                if (releaseMs <= 0 || !_handoverInitialized || blend >= _handoverSmoothed)
                {
                    _handoverSmoothed = blend;
                    _handoverInitialized = true;
                }
                else
                {
                    double releaseAlpha = 1.0 - Math.Exp(-dt * 1000.0 / releaseMs);
                    _handoverSmoothed += (blend - _handoverSmoothed) * releaseAlpha;
                }
                double angleError = targetAngleRad - frontSlipRad;
                double saturationKnee = 1.0 - 0.7 * Clamp(_settings.TargetAngleSoftSaturation / 100.0, 0, 1);
                double servoRaw = SoftClamp(angleError * targetAngleGain, saturationKnee);

                double servoTauMs = angleSmoothingMs;
                if (!_servoInitialized)
                {
                    _servoSmoothed = servoRaw;
                    _servoInitialized = true;
                }
                else
                {
                    double servoAlpha = servoTauMs <= 0 ? 1.0 : 1.0 - Math.Exp(-dt * 1000.0 / servoTauMs);
                    _servoSmoothed += (servoRaw - _servoSmoothed) * servoAlpha;
                }

                double holdWeight = _handoverSmoothed
                    * Clamp(_settings.AssistStrength * strengthScale, 0, 1)
                    * lowSpeedGate * freshnessFade;
                double spinCatch = ComputeSpinCatch(car);
                double servoCombined = Clamp(_servoSmoothed + spinCatch, -1, 1);
                double desiredOffset = (servoCombined - playerTarget) * holdWeight;
                double blendedTarget = playerTarget + desiredOffset;
                double correctionRate = Math.Max(0, _settings.TargetAngleCorrectionRatePerSec) / 100.0;
                if (correctionRate <= 0 || !_limitedTargetInitialized)
                {
                    _limitedTarget = blendedTarget;
                    _limitedTargetInitialized = true;
                }
                else
                {
                    double correctionStep = correctionRate * dt / Math.Max(0.05, holdWeight);
                    _limitedTarget += Clamp(blendedTarget - _limitedTarget, -correctionStep, correctionStep);
                }
                target = _limitedTarget;
                assistContribution = holdWeight;

                Trace.StickAngleDeg = stickTargetRad * 180.0 / Math.PI;
                Trace.TargetAngleDeg = targetAngleRad * 180.0 / Math.PI;
                Trace.Handover = _handoverSmoothed;
                Trace.HoldWeight = holdWeight;
                Trace.AngleErrorDeg = angleError * 180.0 / Math.PI;
                Trace.ServoRawPercent = servoRaw * 100.0;
                Trace.ServoSmoothedPercent = _servoSmoothed * 100.0;
                Trace.SpinCatchPercent = spinCatch * 100.0;
                Trace.DesiredOffsetPercent = desiredOffset * 100.0;
                Trace.AssistOffsetPercent = (_limitedTarget - playerTarget) * 100.0;
            }
            else
            {
                _servoInitialized = false;
                _limitedTargetInitialized = false;
                _targetAngleInitialized = false;
                _handoverInitialized = false;

                assistContribution = blend * Clamp(_settings.AssistStrength * strengthScale, 0, 1) * lowSpeedGate * freshnessFade;

                Trace.StickAngleDeg = 0;
                Trace.TargetAngleDeg = 0;
                Trace.Handover = blend;
                Trace.HoldWeight = assistContribution;
                Trace.AngleErrorDeg = 0;
                Trace.ServoRawPercent = countersteer * 100.0;
                Trace.ServoSmoothedPercent = countersteer * 100.0;
                Trace.DesiredOffsetPercent = countersteer * assistContribution * 100.0;
                Trace.AssistOffsetPercent = countersteer * assistContribution * 100.0;

                double limitedPlayer = ApplySpeedSteerRate(playerTarget, speedFactor, dt);
                limitedPlayer = ApplyFrontGripLimit(limitedPlayer, frontSlipDeg, lowSpeedGate * freshnessFade);
                limitedPlayer = ApplyBrakeSteerReduction(limitedPlayer, car, speedFactor);
                double straightStability = ComputeStraightStability(car, shaped, lowSpeedGate * freshnessFade);
                double raceSpinCatch = ComputeSpinCatch(car, RaceSpinProfile, _settings.TargetAngleSpinCatch, AllowedSlipDeg) * lowSpeedGate * freshnessFade;
                Trace.LimitedPlayerPercent = limitedPlayer * 100.0;
                Trace.StraightStabilityPercent = straightStability * 100.0;
                Trace.SpinCatchPercent = raceSpinCatch * 100.0;

                target = limitedPlayer + countersteer * assistContribution + straightStability + raceSpinCatch;
            }

            target = Clamp(target, -1, 1);

            double tauMs = outputSmoothingMs;
            double alpha = tauMs <= 0 ? 1.0 : 1.0 - Math.Exp(-dt * 1000.0 / tauMs);
            _smoothedOutput += (target - _smoothedOutput) * alpha;
            _smoothedOutput = Clamp(_smoothedOutput, -1, 1);

            FillCommonTrace(dt, car, raw, playerTarget, frontSlipDeg, blend, target, _smoothedOutput);

            var (rl2, rr2) = CombineRumble(loadIntensity, car.LateralAccelG, roadL, roadR, impactL, impactR, surfaceL, surfaceR, shiftL, shiftR);
            PublishSnapshot(raw, _smoothedOutput, car, assistContribution, rl2, rr2, Math.Max(surfaceL, surfaceR));
            return _smoothedOutput * 100.0;
        }

        private const double SteerRateMinSpeedFactor = 0.1;
        private const double FrontGripLimitGainPerRad = 3.0;

        private double _rateLimitedPlayer;
        private bool _rateLimitedPlayerInitialized;

        private double ApplySpeedSteerRate(double playerTarget, double speedFactor, double dt)
        {
            double rateAtSpeed = Math.Max(0, _settings.SteerRateAtSpeedPercentPerSec) / 100.0;
            if (rateAtSpeed <= 0 || !_rateLimitedPlayerInitialized)
            {
                _rateLimitedPlayer = playerTarget;
                _rateLimitedPlayerInitialized = true;
                return playerTarget;
            }

            double step = rateAtSpeed / Math.Max(SteerRateMinSpeedFactor, speedFactor) * dt;
            _rateLimitedPlayer += Clamp(playerTarget - _rateLimitedPlayer, -step, step);
            return _rateLimitedPlayer;
        }

        private double ApplyFrontGripLimit(double player, double frontSlipDeg, double weight)
        {
            double limitDeg = _settings.FrontGripLimitDeg;
            if (limitDeg <= 0 || weight <= 0 || player == 0 || Math.Sign(frontSlipDeg) != Math.Sign(player))
                return player;

            double excessDeg = Math.Abs(frontSlipDeg) - limitDeg;
            if (excessDeg <= 0) return player;

            double reduction = excessDeg * Math.PI / 180.0 * FrontGripLimitGainPerRad * Clamp(weight, 0, 1);
            return Math.Sign(player) * Math.Max(0, Math.Abs(player) - reduction);
        }

        private const double BrakeSteerMaxReduction = 0.8;
        private const double StraightYawGainPerRadS = 0.3;
        private const double StraightMaxCorrection = 0.15;
        private const double StraightStickFadeFraction = 0.1;
        private const double StraightMaxBodySlipDeg = 5.0;
        private const double StraightMinSpeedKmh = 30.0;
        private const double StraightFullSpeedKmh = 50.0;

        private double ApplyBrakeSteerReduction(double player, CarState car, double speedFactor)
        {
            double strength = Clamp(_settings.BrakeSteerReductionPercent / 100.0, 0, BrakeSteerMaxReduction);
            if (strength <= 0 || !car.DataFresh) return player;

            double reduction = strength * Clamp(car.Brake, 0, 1) * Clamp(speedFactor, 0, 1);
            return player * (1.0 - reduction);
        }

        private double ComputeStraightStability(CarState car, double shaped, double weight)
        {
            double strength = Clamp(_settings.StraightStabilityPercent / 100.0, 0, 1);
            if (strength <= 0 || !car.DataFresh || weight <= 0) return 0;
            if (Math.Abs(car.BodySlipAngleRad) * 180.0 / Math.PI >= StraightMaxBodySlipDeg) return 0;

            double stickFade = Clamp(1.0 - Math.Abs(shaped) / StraightStickFadeFraction, 0, 1);
            double speedFade = Clamp((car.SpeedKmh - StraightMinSpeedKmh) / (StraightFullSpeedKmh - StraightMinSpeedKmh), 0, 1);
            double correction = Clamp(strength * StraightYawGainPerRadS * car.YawRateRadS, -StraightMaxCorrection, StraightMaxCorrection);
            return correction * stickFade * speedFade * Clamp(weight, 0, 1);
        }

        private const double BodySlipGrowthTauMs = 50.0;
        private const double SpinCatchDeadbandDegS = 20.0;
        private const double SpinCatchFullExcessDegS = 60.0;
        private const double SpinCatchMinAngleDeg = 20.0;
        private const double SpinCatchFullAngleDeg = 35.0;
        private const double SpinCatchMinSpeedKmh = 15.0;
        private const double SpinCatchFullSpeedKmh = 25.0;
        private const double BodySlipGrowthLimitDegS = 400.0;

        private double _prevBodySlipRad;
        private bool _bodySlipGrowthInitialized;
        private double _bodySlipGrowthEma;

        private void ResetBodySlipGrowth()
        {
            _prevBodySlipRad = 0;
            _bodySlipGrowthInitialized = false;
            _bodySlipGrowthEma = 0;
        }

        private void UpdateBodySlipGrowth(CarState car)
        {
            if (!car.DataFresh)
            {
                ResetBodySlipGrowth();
                return;
            }
            if (!_sampleIsNew) return;

            double beta = car.BodySlipAngleRad;
            if (_bodySlipGrowthInitialized && SampleDtValid)
            {
                double change = Math.IEEERemainder(beta - _prevBodySlipRad, 2.0 * Math.PI);
                double growthLimit = BodySlipGrowthLimitDegS * Math.PI / 180.0;
                double growth = Clamp(Math.Sign(beta) * change / _sampleDt, -growthLimit, growthLimit);
                double alpha = 1.0 - Math.Exp(-_sampleDt * 1000.0 / BodySlipGrowthTauMs);
                _bodySlipGrowthEma += (growth - _bodySlipGrowthEma) * alpha;
            }
            _prevBodySlipRad = beta;
            _bodySlipGrowthInitialized = true;
        }

        private const double ThrottleCutReleaseTauSec = 0.2;
        private const double ThrottleRampMaxSec = 1.0;
        private const double ThrottleRampFullSteer = 0.4;
        private const double TractionTargetSlip = 0.08;
        private const double TractionSlipPerAllowedDeg = 0.01;
        private const double TractionFullRange = 0.12;
        private const double TractionMinSpeedKmh = 5.0;
        private const double TractionFullSpeedKmh = 15.0;

        private static double DrivenWheelSlipRatio(CarState car)
        {
            double slip = 0;
            for (int w = 0; w < 4; w++)
                if (car.WheelTouching[w]) slip = Math.Max(slip, car.WheelSlipRatio[w]);
            return slip;
        }

        private double TractionSeverity(CarState car, double slip)
        {
            double target = TractionTargetSlip + AllowedSlipDeg * TractionSlipPerAllowedDeg;
            double speedGate = Clamp((car.SpeedKmh - TractionMinSpeedKmh) / (TractionFullSpeedKmh - TractionMinSpeedKmh), 0, 1);
            return Clamp((slip - target) / TractionFullRange, 0, 1) * speedGate;
        }

        private double _lastDt = 0.004;
        private double _throttleCut;
        private double _throttleOut;
        private bool _throttleInitialized;

        public byte ProcessThrottle(byte rawTrigger, CarState car)
        {
            double raw = rawTrigger / 255.0;
            bool active = MasterEnabled && _settings != null && _settings.Preset != AssistPreset.Off
                && _settings.Mode == SteeringAssistMode.Countersteer && car.DataFresh
                && (_settings.ThrottleSpinCutPercent > 0 || _settings.ThrottleRampPercent > 0 || _settings.TractionControlPercent > 0);

            if (!active || _settings == null)
            {
                _throttleCut = 0;
                _throttleOut = raw;
                _throttleInitialized = false;
                Trace.ThrottleCutPercent = 0;
                Trace.TractionSlipRatio = DrivenWheelSlipRatio(car);
                Trace.TractionCutPercent = 0;
                Trace.ThrottleOutPercent = raw * 100.0;
                return rawTrigger;
            }

            double dt = _lastDt;
            double cutStrength = Clamp(_settings.ThrottleSpinCutPercent / 100.0, 0, 1);
            double tractionStrength = Clamp(_settings.TractionControlPercent / 100.0, 0, 1);
            double tractionSlip = DrivenWheelSlipRatio(car);
            double tractionSeverity = TractionSeverity(car, tractionSlip);
            double cutTarget = Math.Max(cutStrength * SpinSeverity(car, RaceSpinProfile, AllowedSlipDeg), tractionStrength * tractionSeverity);
            if (cutTarget >= _throttleCut)
                _throttleCut = cutTarget;
            else
                _throttleCut += (cutTarget - _throttleCut) * (1.0 - Math.Exp(-dt / ThrottleCutReleaseTauSec));

            double desired = raw * (1.0 - _throttleCut);

            double rampStrength = Clamp(_settings.ThrottleRampPercent / 100.0, 0, 1);
            double steerFactor = Clamp(Math.Abs(_smoothedOutput) / ThrottleRampFullSteer, 0, 1);
            double rampTime = rampStrength * ThrottleRampMaxSec * steerFactor;
            if (!_throttleInitialized || desired <= _throttleOut || rampTime <= 0.01)
                _throttleOut = desired;
            else
                _throttleOut = Math.Min(desired, _throttleOut + dt / rampTime);
            _throttleInitialized = true;

            Trace.ThrottleCutPercent = _throttleCut * 100.0;
            Trace.TractionSlipRatio = tractionSlip;
            Trace.TractionCutPercent = tractionStrength * tractionSeverity * 100.0;
            Trace.ThrottleOutPercent = _throttleOut * 100.0;
            return (byte)Math.Round(Clamp(_throttleOut, 0, 1) * 255.0);
        }

        private readonly record struct SpinProfile(double DeadbandDegS, double FullExcessDegS, double MinAngleDeg, double FullAngleDeg,
            double MinSpeedKmh, double FullSpeedKmh, double MaxOutput);

        private static readonly SpinProfile DriftSpinProfile = new(SpinCatchDeadbandDegS, SpinCatchFullExcessDegS,
            SpinCatchMinAngleDeg, SpinCatchFullAngleDeg, SpinCatchMinSpeedKmh, SpinCatchFullSpeedKmh, 1.0);

        private static readonly SpinProfile RaceSpinProfile = new(8.0, 25.0, 2.0, 5.0, 30.0, 50.0, 0.5);

        private double ComputeSpinCatch(CarState car) => ComputeSpinCatch(car, DriftSpinProfile, _settings.TargetAngleSpinCatch, 0);

        private double AllowedSlipDeg => Clamp(_settings.SpinCatchAllowedSlipDeg, 0, 45);

        private double SpinSeverity(CarState car, SpinProfile profile, double allowedSlipDeg)
        {
            if (!car.DataFresh) return 0;

            double growthDegS = _bodySlipGrowthEma * 180.0 / Math.PI;
            double excess = growthDegS - profile.DeadbandDegS;
            if (excess <= 0) return 0;

            double angleDeg = Math.Abs(car.BodySlipAngleRad) * 180.0 / Math.PI;
            double angleGate = Clamp((angleDeg - allowedSlipDeg - profile.MinAngleDeg) / (profile.FullAngleDeg - profile.MinAngleDeg), 0, 1);
            double speedGate = Clamp((car.SpeedKmh - profile.MinSpeedKmh) / (profile.FullSpeedKmh - profile.MinSpeedKmh), 0, 1);

            return Math.Min(1.0, excess / profile.FullExcessDegS) * angleGate * speedGate;
        }

        private double ComputeSpinCatch(CarState car, SpinProfile profile, double strengthPercent, double allowedSlipDeg)
        {
            double strength = Clamp(strengthPercent / 100.0, 0, 1);
            if (strength <= 0) return 0;

            return Math.Sign(car.BodySlipAngleRad) * strength * profile.MaxOutput * SpinSeverity(car, profile, allowedSlipDeg);
        }

        private void ClearAssistTrace()
        {
            Trace.SpinCatchPercent = 0;
            Trace.StickAngleDeg = 0;
            Trace.TargetAngleDeg = 0;
            Trace.Handover = 0;
            Trace.HoldWeight = 0;
            Trace.AngleErrorDeg = 0;
            Trace.ServoRawPercent = 0;
            Trace.ServoSmoothedPercent = 0;
            Trace.DesiredOffsetPercent = 0;
            Trace.AssistOffsetPercent = 0;
        }

        private void FillCommonTrace(double dt, CarState car, double raw, double playerTarget, double frontSlipDeg, double blend, double target, double output)
        {
            const double RadToDeg = 180.0 / Math.PI;
            Trace.DtMs = dt * 1000.0;
            Trace.SampleNew = _sampleIsNew;
            Trace.DataAgeMs = car.DataAgeSeconds * 1000.0;
            Trace.DataFresh = car.DataFresh;
            Trace.SpeedKmh = car.SpeedKmh;
            Trace.RawPercent = raw * 100.0;
            Trace.PlayerTargetPercent = playerTarget * 100.0;
            if (_settings.Mode != SteeringAssistMode.Countersteer)
            {
                Trace.LimitedPlayerPercent = playerTarget * 100.0;
                Trace.StraightStabilityPercent = 0;
            }
            Trace.FrontSlipRawDeg = car.FrontSlipAngleRad * RadToDeg;
            Trace.FrontSlipDeg = frontSlipDeg;
            Trace.RearSlipDeg = car.RearSlipAngleRad * RadToDeg;
            Trace.BodySlipDeg = car.BodySlipAngleRad * RadToDeg;
            Trace.YawRateDegS = car.YawRateRadS * RadToDeg;
            Trace.FrontSteerDeg = car.FrontSteerRad * RadToDeg;
            Trace.InputSteerDeg = car.InputSteerRad * RadToDeg;
            Trace.SteerTorqueNm = car.SteerTorqueNm;
            Trace.SteerTorqueValid = car.SteerTorqueValid;
            Trace.ThrottlePercent = car.Throttle * 100.0;
            Trace.BrakePercent = car.Brake * 100.0;
            Trace.Gear = car.Gear;
            Trace.Blend = blend;
            Trace.TargetPercent = target * 100.0;
            Trace.OutputPercent = output * 100.0;
            Trace.BodySlipGrowthDegS = _bodySlipGrowthEma * 180.0 / Math.PI;
            for (int w = 0; w < 4; w++)
            {
                Trace.WheelSlipDeg[w] = car.WheelSlipRad[w] * RadToDeg;
                Trace.WheelLoadN[w] = car.WheelLoadN[w];
                Trace.WheelSlipRatio[w] = car.WheelSlipRatio[w];
                Trace.WheelTouching[w] = car.WheelTouching[w];
            }
        }

        private const double LoadDecayTauMs = 55.0;

        private (double intensity, double accelJerkGPerSec) ComputeLoadIntensity(CarState car, double dt)
        {
            double decay = Math.Exp(-dt * 1000.0 / LoadDecayTauMs);

            if (!car.DataFresh)
            {
                _hasPrevMotionSample = false;
                _loadEnvelope *= decay;
                return (_loadEnvelope, 0);
            }

            double jerk01 = 0;
            double yawSnap01 = 0;
            double accelJerkGPerSec = 0;

            if (_sampleIsNew)
            {
                if (_hasPrevMotionSample && SampleDtValid)
                {
                    accelJerkGPerSec = Math.Abs(car.AccelMagG - _lastAccelMagG) / _sampleDt;
                    jerk01 = Clamp(accelJerkGPerSec / Math.Max(0.5, _rumble.RumbleJerkSensitivity), 0, 1);

                    double yawAccelRadS2 = Math.Abs(car.YawRateRadS - _lastYawRateRadS) / _sampleDt;
                    yawSnap01 = Clamp(yawAccelRadS2 / 8.0, 0, 1);
                }

                _lastAccelMagG = car.AccelMagG;
                _lastYawRateRadS = car.YawRateRadS;
                _hasPrevMotionSample = true;
            }

            double slipDeg = Math.Max(Math.Abs(LoadWeightedFrontSlip(car)), Math.Abs(car.BodySlipAngleRad)) * 180.0 / Math.PI;
            double slipActivation = Math.Max(0, _rumble.RumbleLoadSlipActivationDeg);
            double slipFull = Math.Max(slipActivation + 0.5, _rumble.RumbleLoadSlipFullDeg);
            double tractionLossGate = Clamp((slipDeg - slipActivation) / (slipFull - slipActivation), 0, 1);

            double suddenness = Math.Max(jerk01, yawSnap01) * tractionLossGate;
            _loadEnvelope = Math.Max(_loadEnvelope * decay, suddenness);
            return (_loadEnvelope, accelJerkGPerSec);
        }

        private const double RoadEmaTauMs = 45.0;
        private const double SlowRoadEmaTauMs = 500.0;
        private const double ImpactDecayTauMs = 35.0;
        private const double ImpactThresholdFraction = 0.15;
        private const double SurfaceChangeDecayTauMs = 110.0;
        private const double SurfaceChangeThresholdMultiplier = 0.3;
        private const double SurfaceChangeFullMultiplier = 1.3;

        private (double roadL, double roadR, double impactL, double impactR, double surfaceL, double surfaceR)
            ComputeSuspensionChannels(CarState car, double dt)
        {
            double roadAlpha = 1.0 - Math.Exp(-dt * 1000.0 / RoadEmaTauMs);
            double slowAlpha = 1.0 - Math.Exp(-dt * 1000.0 / SlowRoadEmaTauMs);
            double impactDecay = Math.Exp(-dt * 1000.0 / ImpactDecayTauMs);
            double surfaceDecay = Math.Exp(-dt * 1000.0 / SurfaceChangeDecayTauMs);

            double roadFull = 1.0 / Math.Max(0.02, _rumble.RumbleRoadGain);
            double impactFull = 1.0 / Math.Max(0.02, _rumble.RumbleImpactGain);

            if (!car.DataFresh)
            {
                _hasPrevSuspSample = false;
                for (int w = 0; w < 4; w++)
                {
                    _lastRate[w] = 0;
                    _roadEma[w] *= 1.0 - roadAlpha;
                    _roadEmaSlow[w] *= 1.0 - slowAlpha;
                    _impactEnvelope[w] *= impactDecay;
                    _surfaceChangeEnvelope[w] *= surfaceDecay;
                }
            }
            else
            {
                double impactThreshold = impactFull * ImpactThresholdFraction;
                double surfaceThreshold = roadFull * SurfaceChangeThresholdMultiplier;
                double surfaceFull = roadFull * SurfaceChangeFullMultiplier;

                for (int w = 0; w < 4; w++)
                {
                    if (_sampleIsNew)
                    {
                        _lastRate[w] = _hasPrevSuspSample && SampleDtValid
                            ? Math.Abs(car.SuspDeflect[w] - _lastSuspDeflect[w]) / _sampleDt
                            : 0;
                        _lastSuspDeflect[w] = car.SuspDeflect[w];
                    }

                    double rate = _lastRate[w];

                    _roadEma[w] += (rate - _roadEma[w]) * roadAlpha;
                    _roadEmaSlow[w] += (rate - _roadEmaSlow[w]) * slowAlpha;

                    double impactSpike01 = Clamp((rate - impactThreshold) / (impactFull - impactThreshold), 0, 1);
                    _impactEnvelope[w] = Math.Max(_impactEnvelope[w] * impactDecay, impactSpike01);

                    double divergence = Math.Abs(_roadEma[w] - _roadEmaSlow[w]);
                    double surfaceSpike01 = Clamp((divergence - surfaceThreshold) / (surfaceFull - surfaceThreshold), 0, 1);
                    _surfaceChangeEnvelope[w] = Math.Max(_surfaceChangeEnvelope[w] * surfaceDecay, surfaceSpike01);
                }
                if (_sampleIsNew) _hasPrevSuspSample = true;
            }

            double roadL = Clamp(Math.Max(_roadEma[0], _roadEma[2]) / roadFull, 0, 1);
            double roadR = Clamp(Math.Max(_roadEma[1], _roadEma[3]) / roadFull, 0, 1);
            double impactL = Math.Max(_impactEnvelope[0], _impactEnvelope[2]);
            double impactR = Math.Max(_impactEnvelope[1], _impactEnvelope[3]);
            double surfaceL = Math.Max(_surfaceChangeEnvelope[0], _surfaceChangeEnvelope[2]);
            double surfaceR = Math.Max(_surfaceChangeEnvelope[1], _surfaceChangeEnvelope[3]);

            return (roadL, roadR, impactL, impactR, surfaceL, surfaceR);
        }

        private const double CollisionDecayTauMs = 90.0;
        private const double CollisionJerkThresholdGPerSec = 12.0;
        private const double CollisionJerkFullGPerSec = 35.0;

        private (double left, double right) ComputeCollisionBoost(double accelJerkGPerSec, double lateralAccelG, double dt)
        {
            double decay = Math.Exp(-dt * 1000.0 / CollisionDecayTauMs);
            _collisionEnvelopeLeft *= decay;
            _collisionEnvelopeRight *= decay;

            double spike01 = Clamp((accelJerkGPerSec - CollisionJerkThresholdGPerSec) / (CollisionJerkFullGPerSec - CollisionJerkThresholdGPerSec), 0, 1);
            if (spike01 > 0)
            {
                double bias = Clamp(_rumble.RumbleDirectionalBias, 0, 1);
                double dir = Clamp(lateralAccelG / LoadDirectionFullG, -1, 1);
                _collisionEnvelopeLeft = Math.Max(_collisionEnvelopeLeft, spike01 * (1.0 - Math.Max(0, dir) * bias));
                _collisionEnvelopeRight = Math.Max(_collisionEnvelopeRight, spike01 * (1.0 - Math.Max(0, -dir) * bias));
            }

            return (_collisionEnvelopeLeft, _collisionEnvelopeRight);
        }

        private const double ShiftDecayTauMs = 85.0;

        private (double left, double right) ComputeShiftChannel(CarState car, double dt)
        {
            double decay = Math.Exp(-dt * 1000.0 / ShiftDecayTauMs);
            _shiftEnvelope *= decay;

            if (car.DataFresh)
            {
                if (_lastGear != -999 && car.Gear != _lastGear)
                    _shiftEnvelope = 1.0;
                _lastGear = car.Gear;
            }

            return (_shiftEnvelope, _shiftEnvelope);
        }

        private const double LoadDirectionFullG = 0.8;

        private (double left, double right) CombineRumble(
            double loadIntensity, double lateralAccelG,
            double roadL, double roadR,
            double impactL, double impactR,
            double surfaceL, double surfaceR,
            double shiftL, double shiftR)
        {
            if (!RumbleEnabled)
                return (0, 0);

            double bias = Clamp(_rumble.RumbleDirectionalBias, 0, 1);
            double dir = Clamp(lateralAccelG / LoadDirectionFullG, -1, 1);
            double loadL = loadIntensity * (1.0 - Math.Max(0, dir) * bias);
            double loadR = loadIntensity * (1.0 - Math.Max(0, -dir) * bias);

            double wLoad = Clamp(_rumble.RumbleLoadWeight, 0, 1);
            double wRoad = Clamp(_rumble.RumbleRoadWeight, 0, 1);
            double wImpact = Clamp(_rumble.RumbleImpactWeight, 0, 1);
            double wSurface = Clamp(_rumble.RumbleSurfaceChangeWeight, 0, 1);
            double wShift = Clamp(_rumble.RumbleShiftWeight, 0, 1);

            double left = Shape(loadL) * wLoad + Shape(roadL) * wRoad
                        + Shape(impactL) * wImpact + Shape(surfaceL) * wSurface + Shape(shiftL) * wShift;
            double right = Shape(loadR) * wLoad + Shape(roadR) * wRoad
                         + Shape(impactR) * wImpact + Shape(surfaceR) * wSurface + Shape(shiftR) * wShift;

            return (Clamp(left, 0, 1), Clamp(right, 0, 1));
        }

        private double Shape(double v) => Math.Pow(Clamp(v, 0, 1), Math.Max(0.2, _rumble.RumbleResponseCurve));

        private void PublishSnapshot(double raw, double output, CarState car, double assistContribution, double rumbleLeft, double rumbleRight, double surfaceChange01)
        {
            _snapshotIndex = (_snapshotIndex + 1) % _snapshotRing.Length;
            var snap = _snapshotRing[_snapshotIndex];
            snap.RawSteerPercent = raw * 100.0;
            snap.OutputSteerPercent = output * 100.0;
            snap.SpeedKmh = car.SpeedKmh;
            snap.RearSlipDeg = car.RearSlipAngleRad * 180.0 / Math.PI;
            snap.AssistContribution = assistContribution;
            snap.RumbleLeft01 = rumbleLeft;
            snap.RumbleRight01 = rumbleRight;
            snap.SurfaceChange01 = surfaceChange01;
            snap.CarDataFresh = car.DataFresh;
            LastSnapshot = snap;
        }

        private readonly AssistTelemetrySnapshot[] _snapshotRing = { new(), new(), new(), new() };
        private int _snapshotIndex;
        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private static double SoftClamp(double x, double knee)
        {
            double magnitude = Math.Abs(x);
            if (knee >= 1.0 || magnitude <= knee) return Math.Sign(x) * Math.Min(magnitude, 1.0);
            double span = 1.0 - knee;
            return Math.Sign(x) * (knee + span * Math.Tanh((magnitude - knee) / span));
        }
    }
}
