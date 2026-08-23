using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LFSDriftBuddy
{
    /// <summary>Tire Temperature Limiter for LFS 0.8C — patches LFS.exe memory to prevent
    /// tire temperature increase during drift.</summary>
    public class TireTemperatureLimiter
    {
        // ──────────────────────────────────────────────────────
        // P/Invoke declarations
        // ──────────────────────────────────────────────────────

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint nSize,
            out uint lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint nSize,
            out uint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll",
     CharSet = CharSet.Unicode,
     SetLastError = true)]
        private static extern bool Process32First(
     IntPtr hSnapshot,
     ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll",
    CharSet = CharSet.Unicode,
    SetLastError = true)]
        private static extern bool Process32Next(
    IntPtr hSnapshot,
    ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll",
     CharSet = CharSet.Unicode,
     SetLastError = true)]
        static extern bool Module32First(
     IntPtr hSnapshot,
     ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        static extern bool Module32Next(
            IntPtr hSnapshot,
            ref MODULEENTRY32 lpme);

        // ──────────────────────────────────────────────────────
        // Constants
        // ──────────────────────────────────────────────────────

        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint TH32CS_SNAPMODULE = 0x00000008;

        // Byte pattern for LFS 0.8C tire temperature code
        // For 0.7F it was: 0xD8, 0x4D, 0xDC, 0xD8, 0x65, 0x14, 0xD8
        // 0.8C i don't know what pattern is for 0.8C
        private readonly byte[] _toFind = { 0xD8, 0x4D, 0xDC, 0xD8, 0x65, 0x14, 0xD8 };
        private readonly byte[] _toReplace = { 0x90, 0x90, 0x90, 0xD8, 0x65, 0x14, 0xD8 }; // NOP out first 3 bytes

        // ──────────────────────────────────────────────────────
        // State
        // ──────────────────────────────────────────────────────

        private IntPtr _lfsHandle = IntPtr.Zero;
        private IntPtr _lfsBaseAddress = IntPtr.Zero;
        private uint _patchOffset = 0;
        private bool _isPatched = false;

        // ──────────────────────────────────────────────────────
        // Events
        // ──────────────────────────────────────────────────────

        public event Action<string>? Log;
        public event Action<bool>? PatchStatusChanged;

        // ──────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────

        public bool IsPatched => _isPatched;
        public bool IsConnected => _lfsHandle != IntPtr.Zero;

        /// <summary>Initialize connection to LFS process.</summary>
        public bool Connect()
        {
            try
            {
                if (_lfsHandle != IntPtr.Zero)
                    Disconnect();

                // Find LFS.exe process
                uint lfsProcessId = FindLFSProcess();
                if (lfsProcessId == 0)
                {
                    Log?.Invoke("❌ LFS.exe not found");
                    return false;
                }

                // Open process
                _lfsHandle = OpenProcess(PROCESS_ALL_ACCESS, false, lfsProcessId);
                if (_lfsHandle == IntPtr.Zero)
                {
                    Log?.Invoke("❌ Failed to open LFS process (requires admin)");
                    return false;
                }

                // Get base address
                _lfsBaseAddress = FindLFSBaseAddress(lfsProcessId);
                if (_lfsBaseAddress == IntPtr.Zero)
                {
                    Log?.Invoke("❌ Failed to find LFS.exe base address");
                    CloseHandle(_lfsHandle);
                    _lfsHandle = IntPtr.Zero;
                    return false;
                }

                Log?.Invoke($"✅ Connected to LFS (PID: {lfsProcessId})");
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"❌ Connection error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Disconnect from LFS process.</summary>
        public void Disconnect()
        {
            try
            {
                if (_isPatched)
                    Unpatch();

                if (_lfsHandle != IntPtr.Zero)
                {
                    CloseHandle(_lfsHandle);
                    _lfsHandle = IntPtr.Zero;
                }

                _lfsBaseAddress = IntPtr.Zero;
                Log?.Invoke("Disconnected from LFS");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"❌ Disconnect error: {ex.Message}");
            }
        }

        /// <summary>Apply tire temperature limiting patch.</summary>
        public bool Patch()
        {
            if (_isPatched)
            {
                Log?.Invoke("ℹ️ Already patched");
                return true;
            }

            try
            {
                if (!IsConnected)
                {
                    Log?.Invoke("❌ Not connected to LFS");
                    return false;
                }

                // Find the pattern
                if (!FindPattern())
                {
                    Log?.Invoke("❌ Pattern not found in LFS memory");
                    return false;
                }

                // Verify before patching
                IntPtr targetAddress = IntPtr.Add(_lfsBaseAddress, (int)_patchOffset);
                byte[] buffer = new byte[_toFind.Length];

                if (!ReadProcessMemory(_lfsHandle, targetAddress, buffer, (uint)buffer.Length, out uint bytesRead))
                {
                    Log?.Invoke("❌ Failed to read memory for verification");
                    return false;
                }

                // Check if already patched
                bool alreadyPatched = true;
                for (int i = 0; i < _toFind.Length; i++)
                {
                    if (buffer[i] != _toReplace[i])
                    {
                        alreadyPatched = false;
                        break;
                    }
                }

                if (alreadyPatched)
                {
                    Log?.Invoke("ℹ️ Memory already patched, skipping write");
                    _isPatched = true;
                    PatchStatusChanged?.Invoke(true);
                    return true;
                }

                // Check if original pattern matches
                bool patternMatches = true;
                for (int i = 0; i < _toFind.Length; i++)
                {
                    if (buffer[i] != _toFind[i])
                    {
                        patternMatches = false;
                        break;
                    }
                }

                if (!patternMatches)
                {
                    Log?.Invoke("⚠️ Original pattern doesn't match expected bytes (version mismatch?)");
                    return false;
                }

                // Write patch
                if (!WriteProcessMemory(_lfsHandle, targetAddress, _toReplace, (uint)_toReplace.Length, out uint bytesWritten))
                {
                    Log?.Invoke("❌ Failed to write patch to memory");
                    return false;
                }

                if (bytesWritten != _toReplace.Length)
                {
                    Log?.Invoke($"❌ Partial write: {bytesWritten}/{_toReplace.Length} bytes");
                    return false;
                }

                _isPatched = true;
                Log?.Invoke($"✅ Tire temperature patch applied at offset 0x{_patchOffset:X}");
                PatchStatusChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"❌ Patch error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Remove tire temperature limiting patch (restore original code).</summary>
        public bool Unpatch()
        {
            if (!_isPatched)
            {
                Log?.Invoke("ℹ️ Not patched");
                return true;
            }

            try
            {
                if (!IsConnected)
                {
                    Log?.Invoke("❌ Not connected to LFS");
                    return false;
                }

                IntPtr targetAddress = IntPtr.Add(_lfsBaseAddress, (int)_patchOffset);

                if (!WriteProcessMemory(_lfsHandle, targetAddress, _toFind, (uint)_toFind.Length, out uint bytesWritten))
                {
                    Log?.Invoke("❌ Failed to restore original code");
                    return false;
                }

                _isPatched = false;
                Log?.Invoke($"✅ Tire temperature patch removed");
                PatchStatusChanged?.Invoke(false);
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"❌ Unpatch error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Try alternative find/replace byte patterns for other LFS versions.</summary>
        public bool TryAlternativePattern(byte[] findPattern, byte[] replacePattern)
        {
            if (findPattern == null || replacePattern == null || findPattern.Length != replacePattern.Length)
            {
                Log?.Invoke("❌ Invalid pattern");
                return false;
            }

            // Replace current patterns
            Array.Copy(findPattern, _toFind, Math.Min(findPattern.Length, _toFind.Length));
            Array.Copy(replacePattern, _toReplace, Math.Min(replacePattern.Length, _toReplace.Length));

            Log?.Invoke("🔄 Trying alternative pattern...");
            return Patch();
        }

        // ──────────────────────────────────────────────────────
        // Private Helpers
        // ──────────────────────────────────────────────────────

        private uint FindLFSProcess()
        {
            try
            {
                IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

                if (snapshot == new IntPtr(-1))
                    return 0;

                PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };

                if (!Process32First(snapshot, ref entry))
                {
                    CloseHandle(snapshot);
                    return 0;
                }

                do
                {
                    if (entry.szExeFile.Equals("LFS.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        uint pid = entry.th32ProcessID;
                        CloseHandle(snapshot);
                        return pid;
                    }
                } while (Process32Next(snapshot, ref entry));

                CloseHandle(snapshot);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private IntPtr FindLFSBaseAddress(uint processId)
        {
            try
            {
                IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, processId);
                if (snapshot == new IntPtr(-1))
                    return IntPtr.Zero;

                MODULEENTRY32 entry = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32)) };

                if (!Module32First(snapshot, ref entry))
                {
                    CloseHandle(snapshot);
                    return IntPtr.Zero;
                }

                do
                {
                    if (entry.szModule.Equals("LFS.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        IntPtr baseAddr = entry.modBaseAddr;
                        CloseHandle(snapshot);
                        return baseAddr;
                    }
                } while (Module32Next(snapshot, ref entry));

                CloseHandle(snapshot);
                return IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private bool FindPattern()
        {
            const uint CHUNK_SIZE = 0x10000; // 64KB chunks
            const uint MAX_SCAN = 0x1000000; // Scan up to 16MB

            try
            {
                byte[] buffer = new byte[CHUNK_SIZE];

                for (uint offset = 0; offset < MAX_SCAN; offset += CHUNK_SIZE)
                {
                    IntPtr readAddr = IntPtr.Add(_lfsBaseAddress, (int)offset);

                    if (!ReadProcessMemory(_lfsHandle, readAddr, buffer, CHUNK_SIZE, out uint bytesRead))
                        continue;

                    // Search within this chunk
                    for (int i = 0; i < bytesRead - _toFind.Length; i++)
                    {
                        // Bug fix: previous version never set match=false on mismatch,
                        // so it always "matched" at the first scanned offset.
                        bool match = true;
                        for (int j = 0; j < _toFind.Length; j++)
                        {
                            if (buffer[i + j] != _toFind[j])
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            _patchOffset = offset + (uint)i;
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    // ──────────────────────────────────────────────────────
    // P/Invoke Structures
    // ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }
}
