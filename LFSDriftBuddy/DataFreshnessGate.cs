using System;

namespace LFSDriftBuddy
{
    /// <summary>Tracks freshness of the last received telemetry sample (e.g. an OutGauge
    /// packet) — shared timestamp logic reused across OverlayForm (speedo HUD visibility,
    /// active-mode gating) and DriftEngine (drift/speeding/burnout detection gating).</summary>
    public sealed class DataFreshnessGate
    {
        private readonly TimeSpan _staleThreshold;
        private DateTime _lastPingUtc = DateTime.MinValue;

        public DataFreshnessGate(TimeSpan staleThreshold)
        {
            _staleThreshold = staleThreshold;
        }

        /// <summary>Register a fresh sample.</summary>
        public void Ping() => _lastPingUtc = DateTime.UtcNow;

        /// <summary>Force "stale" immediately, without waiting for the threshold to elapse.</summary>
        public void Reset() => _lastPingUtc = DateTime.MinValue;

        /// <summary>Whether the last Ping() was within staleThreshold.</summary>
        public bool IsFresh => (DateTime.UtcNow - _lastPingUtc) < _staleThreshold;
    }
}
