using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LFSDriftTools
{
    public sealed class HighResolutionWaiter : IDisposable
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;
        private const uint Infinite = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private IntPtr _timer;

        public HighResolutionWaiter()
        {
            try
            {
                _timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
            }
            catch
            {
                _timer = IntPtr.Zero;
            }
        }

        public bool IsHighResolution => _timer != IntPtr.Zero;

        public static bool IsSupported
        {
            get
            {
                using var probe = new HighResolutionWaiter();
                return probe.IsHighResolution;
            }
        }

        public void Wait(double milliseconds)
        {
            if (milliseconds <= 0) return;
            if (_timer == IntPtr.Zero)
            {
                Thread.Sleep((int)Math.Ceiling(milliseconds));
                return;
            }

            long dueTime = -(long)(milliseconds * 10000.0);
            if (!SetWaitableTimer(_timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                Thread.Sleep((int)Math.Ceiling(milliseconds));
                return;
            }
            WaitForSingleObject(_timer, Infinite);
        }

        [ThreadStatic] private static HighResolutionWaiter? _threadWaiter;

        public static void Sleep(double milliseconds)
        {
            _threadWaiter ??= new HighResolutionWaiter();
            _threadWaiter.Wait(milliseconds);
        }

        public void Dispose()
        {
            if (_timer != IntPtr.Zero)
            {
                CloseHandle(_timer);
                _timer = IntPtr.Zero;
            }
        }
    }
}
