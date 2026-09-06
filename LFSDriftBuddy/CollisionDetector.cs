using System;
using System.Collections.Generic;

namespace LFSDriftBuddy
{
    public class CollisionDetector
    {
        public bool Enabled { get; set; } = false;

        private double[] _hitLevelThresholds = { 20.0, 40.0, 65.0, 95.0, 130.0 };
        public IReadOnlyList<double> HitLevelThresholds => _hitLevelThresholds;
        public void SetHitLevelThresholds(double[] values)
        {
            if (values == null || values.Length != 5) return;
            _hitLevelThresholds = (double[])values.Clone();
        }

        private const double PairCooldownSec = 1.5;
        private readonly Dictionary<byte, DateTime> _pairCooldown = new();

        public event Action<int, string>? ContactDetected;

        public void ProcessContact(InSim.CarContactEventArgs contact, byte viewedPlid)
        {
            if (!Enabled || viewedPlid == 0 || contact?.A == null || contact.B == null) return;

            InSim.DerbyCarContact self, other;
            if (contact.A.PLID == viewedPlid) { self = contact.A; other = contact.B; }
            else if (contact.B.PLID == viewedPlid) { self = contact.B; other = contact.A; }
            else return;

            if (other.PLID == 0 || other.PLID == viewedPlid) return;

            double closingKmh = contact.ClosingSpeedKmh;
            if (closingKmh < _hitLevelThresholds[0]) return;

            var now = DateTime.UtcNow;
            if (_pairCooldown.TryGetValue(other.PLID, out var last) && (now - last).TotalSeconds < PairCooldownSec)
                return;
            _pairCooldown[other.PLID] = now;

            int tier = 1;
            for (int i = 4; i >= 1; i--)
            {
                if (closingKmh >= _hitLevelThresholds[i]) { tier = i + 1; break; }
            }

            double headingDiff = Math.Abs(NormalizeAngleDelta(self.HeadingDeg - other.HeadingDeg));
            string hitTypeKey = headingDiff >= 120 ? "derby.hittype.headon"
                               : headingDiff >= 45 ? "derby.hittype.side"
                               : "derby.hittype.rear";

            ContactDetected?.Invoke(tier, hitTypeKey);
        }

        private static double NormalizeAngleDelta(double delta)
        {
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;
            return delta;
        }
    }
}
