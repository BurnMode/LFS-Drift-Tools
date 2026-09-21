using System;
using System.Runtime.InteropServices;

namespace LFSDriftTools
{
    public static class LfsWindow
    {
        private static readonly string[] Titles = { "LFS", "Live for Speed", "LFS S3", "LFS S2", "LFS Demo" };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        public static IntPtr Find()
        {
            foreach (var title in Titles)
            {
                IntPtr handle = FindWindow(null, title);
                if (handle != IntPtr.Zero) return handle;
            }
            return IntPtr.Zero;
        }
    }
}
