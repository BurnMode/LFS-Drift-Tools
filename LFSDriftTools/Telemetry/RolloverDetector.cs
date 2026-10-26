using System;
using System.Diagnostics;

namespace LFSDriftTools
{
    public sealed class RolloverDetector
    {
        private const double UpsideDownUpZThreshold = -0.5;

        private const double TumbleRateRadS = 2.5;
        private const double TumbleMinDurationSec = 0.12;

        private long _tumbleStartTicks = -1;

        public bool IsUpsideDown { get; private set; }
        public bool IsTumbling { get; private set; }

        public bool Update(OutSimSample sample)
        {
            if (!sample.HasMain)
            {
                Reset();
                return false;
            }

            IsUpsideDown = Math.Cos(sample.Pitch) * Math.Cos(sample.Roll) < UpsideDownUpZThreshold;

            double tiltRate = Math.Sqrt(sample.AngVel.X * sample.AngVel.X + sample.AngVel.Y * sample.AngVel.Y);
            long nowTicks = Stopwatch.GetTimestamp();

            if (tiltRate >= TumbleRateRadS)
            {
                if (_tumbleStartTicks < 0)
                    _tumbleStartTicks = nowTicks;
                IsTumbling = (nowTicks - _tumbleStartTicks) >= (long)(TumbleMinDurationSec * Stopwatch.Frequency);
            }
            else
            {
                _tumbleStartTicks = -1;
                IsTumbling = false;
            }

            return IsUpsideDown || IsTumbling;
        }

        public void Reset()
        {
            _tumbleStartTicks = -1;
            IsUpsideDown = false;
            IsTumbling = false;
        }
    }
}
