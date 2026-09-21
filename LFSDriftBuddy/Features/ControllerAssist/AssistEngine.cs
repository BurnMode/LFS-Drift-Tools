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

    public class AssistEngine
    {
        public AssistSettings Settings { get; set; } = AssistSettings.ForPreset(AssistPreset.Medium);
        public RumbleSettings Rumble { get; set; } = new();

        public bool MasterEnabled { get; set; } = true;
        public bool RumbleEnabled { get; set; } = true;

        public double CountersteerTrimPercent { get; set; } = 0.0;
        public double AssistStrengthTrimPercent { get; set; } = 0.0;

        private double CountersteerTrimMultiplier => 1.0 + Clamp(CountersteerTrimPercent, -50, 50) / 100.0;
        private double AssistStrengthTrimMultiplier => 1.0 + Clamp(AssistStrengthTrimPercent, -50, 50) / 100.0;

        public AssistTelemetrySnapshot LastSnapshot { get; private set; } = new();

        private AssistSettings _settings = null!;
        private RumbleSettings _rumble = null!;
        private bool _needsSync = true;

        public void Reset()
        {
            _needsSync = true;
            _smoothedOutput = 0;
            _lastTickUtc = DateTime.MinValue;
            _rearSlipAngleEma = 0;

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
        private DateTime _lastTickUtc = DateTime.MinValue;

        private double _rearSlipAngleEma;

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

            double raw = Clamp(rawSteeringPercent / 100.0, -1, 1);

            if (_needsSync)
            {
                _smoothedOutput = raw;
                _needsSync = false;
            }

            UpdateSampleInfo(car);

            var (loadIntensity, accelJerkGPerSec) = ComputeLoadIntensity(car, dt);
            var (roadL, roadR, impactL, impactR, surfaceL, surfaceR) = ComputeSuspensionChannels(car, dt);
            var (collisionL, collisionR) = ComputeCollisionBoost(accelJerkGPerSec, car.LateralAccelG, dt);
            impactL = Math.Max(impactL, collisionL);
            impactR = Math.Max(impactR, collisionR);
            var (shiftL, shiftR) = ComputeShiftChannel(car, dt);

            if (!MasterEnabled || _settings.Preset == AssistPreset.Off)
            {
                _smoothedOutput = raw;
                var (rl, rr) = CombineRumble(loadIntensity, car.LateralAccelG, roadL, roadR, impactL, impactR, surfaceL, surfaceR, shiftL, shiftR);
                PublishSnapshot(raw, raw, car, 0, rl, rr, Math.Max(surfaceL, surfaceR));
                return raw * 100.0;
            }

            double dz = Clamp(_settings.DeadzonePercent / 100.0, 0, 0.5);
            double shaped = Math.Abs(raw) <= dz ? 0 : Math.Sign(raw) * (Math.Abs(raw) - dz) / (1 - dz);
            shaped = Math.Sign(shaped) * Math.Pow(Math.Abs(shaped), Math.Max(0.2, _settings.ResponseCurve));

            double speedFactor = car.DataFresh
                ? Clamp(car.SpeedKmh / Math.Max(1, _settings.SpeedSensitivityRefKmh), 0, 1)
                : 0;
            double maxSteerFraction = 1.0 - speedFactor * (1.0 - Clamp(_settings.MinHighSpeedSteerFraction, 0, 1));
            double playerTarget = shaped * maxSteerFraction;

            const double RearSlipEmaTauMs = 35.0;
            if (car.DataFresh)
            {
                double slipAlpha = 1.0 - Math.Exp(-dt * 1000.0 / RearSlipEmaTauMs);
                _rearSlipAngleEma += (car.RearSlipAngleRad - _rearSlipAngleEma) * slipAlpha;
            }
            else
            {
                _rearSlipAngleEma = 0;
            }

            double rearSlipDeg = car.DataFresh ? _rearSlipAngleEma * 180.0 / Math.PI : 0;
            double activationDeg = Math.Max(0, _settings.SlipActivationDeg);
            double fullDeg = Math.Max(activationDeg + 0.1, _settings.SlipFullDeg);
            double blend = Clamp((Math.Abs(rearSlipDeg) - activationDeg) / (fullDeg - activationDeg), 0, 1);

            double rearSlipRad = rearSlipDeg * Math.PI / 180.0;
            double countersteer = -Math.Sign(rearSlipRad) * Math.Min(1.0, Math.Abs(rearSlipRad) * _settings.CountersteerGain * CountersteerTrimMultiplier);

            const double hardZeroKmh = 1.0;
            double rampSpanKmh = Math.Max(0.1, _settings.MinActivationSpeedKmh - hardZeroKmh);
            double lowSpeedGate = car.DataFresh
                ? Clamp((car.SpeedKmh - hardZeroKmh) / rampSpanKmh, 0, 1)
                : 0;

            const double FreshnessFadeSeconds = 0.25;
            double freshnessFade = Clamp(1.0 - car.DataAgeSeconds / FreshnessFadeSeconds, 0, 1);

            double assistContribution = blend * Clamp(_settings.AssistStrength * AssistStrengthTrimMultiplier, 0, 1) * lowSpeedGate * freshnessFade;

            double target = playerTarget + countersteer * assistContribution;

            if (Math.Abs(shaped) < 0.02 && blend < 0.05 && _settings.SelfCenterStrength > 0)
            {
                double pull = Clamp(_settings.SelfCenterStrength, 0, 1) * dt * 6.0;
                target *= Math.Max(0, 1.0 - pull);
            }

            target = Clamp(target, -1, 1);

            double maxStep = Math.Max(0.1, _settings.MaxSteerRatePerSec) * dt;
            double delta = Clamp(target - _smoothedOutput, -maxStep, maxStep);
            double rateLimited = _smoothedOutput + delta;

            double tauMs = Math.Max(0, _settings.SmoothingMs);
            double alpha = tauMs <= 0 ? 1.0 : 1.0 - Math.Exp(-dt * 1000.0 / tauMs);
            _smoothedOutput += (rateLimited - _smoothedOutput) * alpha;
            _smoothedOutput = Clamp(_smoothedOutput, -1, 1);

            var (rl2, rr2) = CombineRumble(loadIntensity, car.LateralAccelG, roadL, roadR, impactL, impactR, surfaceL, surfaceR, shiftL, shiftR);
            PublishSnapshot(raw, _smoothedOutput, car, assistContribution, rl2, rr2, Math.Max(surfaceL, surfaceR));
            return _smoothedOutput * 100.0;
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

            double slipDeg = Math.Max(Math.Abs(car.RearSlipAngleRad), Math.Abs(car.BodySlipAngleRad)) * 180.0 / Math.PI;
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
            LastSnapshot = new AssistTelemetrySnapshot
            {
                RawSteerPercent = raw * 100.0,
                OutputSteerPercent = output * 100.0,
                SpeedKmh = car.SpeedKmh,
                RearSlipDeg = car.RearSlipAngleRad * 180.0 / Math.PI,
                AssistContribution = assistContribution,
                RumbleLeft01 = rumbleLeft,
                RumbleRight01 = rumbleRight,
                SurfaceChange01 = surfaceChange01,
                CarDataFresh = car.DataFresh,
            };
        }
        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
    }
}
