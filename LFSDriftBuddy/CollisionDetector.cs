using System;
using System.Collections.Generic;

namespace LFSDriftBuddy
{
    public class CollisionDetector
    {
        public bool Enabled { get; set; } = false;

        private double[] _hitLevelThresholds = { 5.0, 10.0, 30.0, 50.0, 100.0 };
        public IReadOnlyList<double> HitLevelThresholds => _hitLevelThresholds;
        public void SetHitLevelThresholds(double[] values)
        {
            if (values == null || values.Length != 5) return;
            _hitLevelThresholds = (double[])values.Clone();
        }

        private const double PairCooldownSec = 1.5;
        private readonly Dictionary<byte, DateTime> _pairCooldown = new();

        public event Action<int, byte>? ContactDetected;

        public void ProcessContact(InSim.CarContactEventArgs contact, byte viewedPlid)
        {
            if (!Enabled || viewedPlid == 0 || contact?.A == null || contact.B == null) return;

            InSim.DerbyCarContact other;
            if (contact.A.PLID == viewedPlid) other = contact.B;
            else if (contact.B.PLID == viewedPlid) other = contact.A;
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

            ContactDetected?.Invoke(tier, other.PLID);
        }
    }
}
