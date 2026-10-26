using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LFSDriftTools
{
    public static class LfsInputGuard
    {
        private const int ErrorAccessDenied = 5;
        private static int _reported;

        public static event Action? KeysBlocked;

        public static bool KeysWereBlocked => Volatile.Read(ref _reported) != 0;

        public static bool Check(bool posted)
        {
            if (posted) return true;
            if (Marshal.GetLastPInvokeError() == ErrorAccessDenied && Interlocked.Exchange(ref _reported, 1) == 0)
                KeysBlocked?.Invoke();
            return false;
        }
    }
}
