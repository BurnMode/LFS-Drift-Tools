using System.Runtime.InteropServices;

namespace LFSDriftTools
{
    public static class TimerResolution
    {
        private const uint PeriodMs = 1;
        private static readonly object Sync = new();
        private static int _users;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint period);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint period);

        public static bool Acquire()
        {
            if (HighResolutionWaiter.IsSupported) return false;

            lock (Sync)
            {
                if (_users == 0 && TimeBeginPeriod(PeriodMs) != 0)
                    return false;

                _users++;
                return true;
            }
        }

        public static void Release()
        {
            lock (Sync)
            {
                if (_users == 0) return;

                _users--;
                if (_users == 0)
                    TimeEndPeriod(PeriodMs);
            }
        }
    }
}
