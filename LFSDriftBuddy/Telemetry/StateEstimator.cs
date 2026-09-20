using System;
using System.Numerics;

namespace LFSDriftTools
{
    public class CarState
    {
        public bool DataFresh;
        public float DataAgeSeconds;
        public bool Moving;

        public float SpeedKmh;
        public float YawRateRadS;
        public float RearSlipAngleRad;
        public float BodySlipAngleRad;
        public float AccelMagG;
        public float LateralAccelG;

        public readonly float[] SuspDeflect = new float[4];

        public float InputSteerRad;
        public float Throttle;
        public float Brake;
        public byte Gear;

        public void Reset()
        {
            DataFresh = false;
            DataAgeSeconds = 0;
            Moving = false;
            SpeedKmh = 0;
            YawRateRadS = 0;
            RearSlipAngleRad = 0;
            BodySlipAngleRad = 0;
            AccelMagG = 0;
            LateralAccelG = 0;
            SuspDeflect[0] = SuspDeflect[1] = SuspDeflect[2] = SuspDeflect[3] = 0;
            InputSteerRad = 0;
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

            float dt = (float)Math.Min(age, MaxExtrapolationSeconds);

            float extrapolatedHeading = last.Heading + last.AngVel.Z * dt;
            Vector3 extrapolatedVel = last.Vel + last.Accel * dt;

            float cos = MathF.Cos(-extrapolatedHeading);
            float sin = MathF.Sin(-extrapolatedHeading);

            state.YawRateRadS = last.AngVel.Z;
            state.SpeedKmh = extrapolatedVel.Length() * 3.6f;
            state.RearSlipAngleRad = last.RearSlipAngleRad();

            state.AccelMagG = MathF.Sqrt(last.Accel.X * last.Accel.X + last.Accel.Y * last.Accel.Y) / 9.80665f;
            state.LateralAccelG = (last.Accel.X * cos - last.Accel.Y * sin) / 9.80665f;

            if (last.HasWheels)
            {
                for (int w = 0; w < 4 && w < last.Wheels.Length; w++)
                    state.SuspDeflect[w] = last.Wheels[w].SuspDeflect;
            }

            float right = extrapolatedVel.X * cos - extrapolatedVel.Y * sin;
            float fwd = extrapolatedVel.X * sin + extrapolatedVel.Y * cos;
            state.BodySlipAngleRad = MathF.Abs(fwd) < 0.5f ? 0f : MathF.Atan2(right, fwd);

            if (last.HasInputs)
            {
                state.InputSteerRad = last.InputSteerRad;
                state.Throttle = last.Throttle;
                state.Brake = last.Brake;
            }

            if (last.HasDrive)
                state.Gear = last.Gear;

            state.Moving = state.SpeedKmh > MovingThresholdKmh;
            return state;
        }
    }
}
