using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace LFSDriftTools
{
    internal static class InstallerSignatureTrust
    {
        private const string ExpectedPublisher = "Nefarius Software Solutions";

        private const int WTD_UI_NONE = 2;
        private const int WTD_REVOKE_NONE = 0;
        private const int WTD_CHOICE_FILE = 1;
        private const int WTD_STATEACTION_VERIFY = 1;
        private const int WTD_STATEACTION_CLOSE = 2;
        private const int WTD_REVOCATION_CHECK_NONE = 0x10;

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private static Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public int dwUIChoice;
            public int fdwRevocationChecks;
            public int dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags;
            public int dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

        public static bool IsTrusted(string path)
        {
            try
            {
                return VerifySignature(path) && HasExpectedPublisher(path);
            }
            catch
            {
                return false;
            }
        }

        private static bool VerifySignature(string path)
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = path,
            };

            IntPtr filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, filePtr, false);

                var data = new WinTrustData
                {
                    cbStruct = Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = filePtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE,
                };

                int result = WinVerifyTrust(InvalidHandle, ref GenericVerifyV2, ref data);

                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(InvalidHandle, ref GenericVerifyV2, ref data);

                return result == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(filePtr);
            }
        }

        private static bool HasExpectedPublisher(string path)
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string subject = cert.Subject ?? "";
            return subject.Contains("CN=" + ExpectedPublisher, StringComparison.OrdinalIgnoreCase);
        }
    }
}
