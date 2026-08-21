using System;

namespace LFSDriftBuddy
{
    /// <summary>
    /// Śledzi "świeżość" ostatnio odebranej próbki telemetrii (np. pakietu OutGauge) —
    /// wspólna logika znacznika czasu używana w kilku miejscach (widoczność HUD-u
    /// prędkościomierza i blokada trybu active w OverlayForm, blokada wykrywania
    /// drift/speeding/burnout w DriftEngine), żeby każde z nich nie duplikowało tej
    /// samej arytmetyki na DateTime.
    /// </summary>
    public sealed class DataFreshnessGate
    {
        private readonly TimeSpan _staleThreshold;
        private DateTime _lastPingUtc = DateTime.MinValue;

        public DataFreshnessGate(TimeSpan staleThreshold)
        {
            _staleThreshold = staleThreshold;
        }

        /// <summary>Zarejestruj odebranie świeżej próbki (np. pakietu OutGauge).</summary>
        public void Ping() => _lastPingUtc = DateTime.UtcNow;

        /// <summary>Wymuś stan "martwe" natychmiast, bez czekania na upłynięcie progu
        /// (np. przy jawnym rozłączeniu z LFS).</summary>
        public void Reset() => _lastPingUtc = DateTime.MinValue;

        /// <summary>Czy ostatni Ping() był w oknie staleThreshold.</summary>
        public bool IsFresh => (DateTime.UtcNow - _lastPingUtc) < _staleThreshold;
    }
}
