using System;

namespace LFSDriftBuddy
{
        public sealed class DataFreshnessGate
    {
        private readonly TimeSpan _staleThreshold;
        private DateTime _lastPingUtc = DateTime.MinValue;

        public DataFreshnessGate(TimeSpan staleThreshold)
        {
            _staleThreshold = staleThreshold;
        }

                public void Ping() => _lastPingUtc = DateTime.UtcNow;

                public void Reset() => _lastPingUtc = DateTime.MinValue;

                public bool IsFresh => (DateTime.UtcNow - _lastPingUtc) < _staleThreshold;
    }
}
