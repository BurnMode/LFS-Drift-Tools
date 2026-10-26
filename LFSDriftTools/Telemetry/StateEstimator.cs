using System;
using System.Numerics;

namespace LFSDriftTools
{
    public class CarState
    {
        public bool DataFresh;
        public float DataAgeSeconds;
        public DateTime SampleReceivedUtc;
        public uint SampleTimeMs;
        public bool Moving;

        public float SpeedKmh;
        public float YawRateRadS;
        public float RearSlipAngleRad;
        public float FrontSlipAngleRad;
        public float FrontSteerRad;
        public float FrontSlipWeightedRad;
        public bool FrontSlipWeightedValid;
        public float BodySlipAngleRad;
        public float AccelMagG;
        public float LateralAccelG;

        public readonly float[] SuspDeflect = new float[4];
        public readonly float[] WheelSlipRad = new float[4];
        public readonly float[] WheelLoadN = new float[4];
        public readonly float[] WheelSlipRatio = new float[4];
        public readonly bool[] WheelTouching = new bool[4];

        public float InputSteerRad;
        public float SteerTorqueNm;
        public bool SteerTorqueValid;
        public float Throttle;
        public float Brake;
        public byte Gear;

        public void Reset()
        {
            DataFresh = false;
            DataAgeSeconds = 0;
            SampleReceivedUtc = DateTime.MinValue;
            SampleTimeMs = 0;
            Moving = false;
            SpeedKmh = 0;
            YawRateRadS = 0;
            RearSlipAngleRad = 0;
            FrontSlipAngleRad = 0;
            FrontSteerRad = 0;
            FrontSlipWeightedRad = 0;
            FrontSlipWeightedValid = false;
            BodySlipAngleRad = 0;
            AccelMagG = 0;
            LateralAccelG = 0;
            SuspDeflect[0] = SuspDeflect[1] = SuspDeflect[2] = SuspDeflect[3] = 0;
            for (int w = 0; w < 4; w++)
            {
                WheelSlipRad[w] = 0;
                WheelLoadN[w] = 0;
                WheelSlipRatio[w] = 0;
                WheelTouching[w] = false;
            }
            InputSteerRad = 0;
            SteerTorqueNm = 0;
            SteerTorqueValid = false;
            Throttle = 0;
            Brake = 0;
            Gear = 0;
        }
    }

    public class StateEstimator
    {
        private const double MaxExtrapolationSeconds = 0.15;
        private const double StaleAfterSeconds = 0.30;
        private const double MovingThresholdKmh = 3.0;

        private readonly object _lock = new();
        private OutSimSample? _last;
        private DateTime _lastUpdateUtc = DateTime.MinValue;

        private readonly CarState _state = new();

        public void Ingest(OutSimSample sample)
        {
            lock (_lock)
            {
                _last = sample;
                _lastUpdateUtc = DateTime.UtcNow;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _last = null;
                _lastUpdateUtc = DateTime.MinValue;
            }
        }

        public CarState Sample()
        {
            OutSimSample? last;
            DateTime lastUpdateUtc;
            lock (_lock)
            {
                last = _last;
                lastUpdateUtc = _lastUpdateUtc;
            }

            CarState state = _state;
            state.Reset();
            if (last == null || !last.HasMain)
                return state;

            double age = (DateTime.UtcNow - lastUpdateUtc).TotalSeconds;
            state.DataAgeSeconds = (float)age;
            state.DataFresh = age < StaleAfterSeconds;
            if (!state.DataFresh)
                return state;

            state.SampleReceivedUtc = last.ReceivedAtUtc;
            state.SampleTimeMs = last.TimeMs;

            float dt = (float)Math.Min(age, MaxExtrapolationSeconds);

            float extrapolatedHeading = last.Heading + last.AngVel.Z * dt;
            Vector3 extrapolatedVel = last.Vel + last.Accel * dt;

            float cos = MathF.Cos(-extrapolatedHeading);
            float sin = MathF.Sin(-extrapolatedHeading);

            state.YawRateRadS = last.AngVel.Z;
            state.SpeedKmh = extrapolatedVel.Length() * 3.6f;
            state.RearSlipAngleRad = last.RearSlipAngleRad();
            state.FrontSlipAngleRad = last.FrontSlipAngleRad();
            state.FrontSteerRad = last.FrontSteerRad();
            state.FrontSlipWeightedValid = last.TryFrontSlipAngleRadLoadWeighted(out float frontSlipWeighted);
            state.FrontSlipWeightedRad = frontSlipWeighted;

            state.AccelMagG = MathF.Sqrt(last.Accel.X * last.Accel.X + last.Accel.Y * last.Accel.Y) / 9.80665f;
            state.LateralAccelG = (last.Accel.X * cos - last.Accel.Y * sin) / 9.80665f;

            if (last.HasWheels)
            {
                for (int w = 0; w < 4 && w < last.Wheels.Length; w++)
                {
                    state.SuspDeflect[w] = last.Wheels[w].SuspDeflect;
                    state.WheelSlipRad[w] = last.Wheels[w].SlipAngleRad;
                    state.WheelLoadN[w] = last.Wheels[w].VerticalLoad;
                    state.WheelSlipRatio[w] = last.Wheels[w].SlipRatio;
                    state.WheelTouching[w] = last.Wheels[w].Touching;
                }
            }

            float right = extrapolatedVel.X * cos - extrapolatedVel.Y * sin;
            float fwd = extrapolatedVel.X * sin + extrapolatedVel.Y * cos;
            state.BodySlipAngleRad = right * right + fwd * fwd < 0.25f ? 0f : MathF.Atan2(right, fwd);

            if (last.HasInputs)
            {
                state.InputSteerRad = last.InputSteerRad;
                state.Throttle = last.Throttle;
                state.Brake = last.Brake;
            }

            state.SteerTorqueValid = last.HasExtra1;
            state.SteerTorqueNm = last.HasExtra1 ? last.SteerTorqueNm : 0f;

            if (last.HasDrive)
                state.Gear = last.Gear;

            state.Moving = state.SpeedKmh > MovingThresholdKmh;
            return state;
        }
    }
}
