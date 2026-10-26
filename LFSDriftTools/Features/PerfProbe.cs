using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public enum PerfSection
    {
        OverlayTick,
        OverlayClear,
        OverlayScore,
        OverlayScoreIndicator,
        OverlayLap,
        OverlaySpeedo,
        OverlaySpeedoBackground,
        OverlaySpeedoGlow,
        OverlayAssistBar,
        OverlayAssistNotice,
        OverlayFlush,
        OverlayScan,
        OverlayUpdateWindow,
        OverlayTrack,
        AssistTick,
        AssistPadRead,
        AssistEngine,
        AssistRecorder,
        AssistVirtualPad,
        AssistVibration,
        OutSimHandler,
        OutGaugeHandler,
        AssistDisplayTick,
        WheelPoll,
        UiPosted,
        UiOutGauge,
        UiCarData,
    }

    public enum PerfCounter
    {
        OverlayFramesPresented,
        OverlayFramesSkipped,
        OverlayDirtyPixels,
        OverlayDirtyRects,
        OverlayFullPresents,
        OverlayRenderSkipped,
    }

    public enum PerfThread
    {
        Ui,
        AssistLoop,
        OutSim,
        OutGauge,
    }

    public static class PerfProbe
    {
        public static readonly bool Enabled = DetectEnabled();

        private const int FlushIntervalMs = 5000;
        private const uint ThreadQueryLimitedInformation = 0x0800;

        private static readonly int SectionCount = Enum.GetValues<PerfSection>().Length;
        private static readonly int CounterCount = Enum.GetValues<PerfCounter>().Length;
        private static readonly int ThreadCount = Enum.GetValues<PerfThread>().Length;

        private static readonly long[] _ticks = new long[SectionCount];
        private static readonly long[] _calls = new long[SectionCount];
        private static readonly long[] _max = new long[SectionCount];
        private static readonly long[] _counters = new long[CounterCount];
        private static readonly IntPtr[] _threadHandles = new IntPtr[ThreadCount];
        private static readonly long[] _threadLastCpu = new long[ThreadCount];
        private static readonly ulong[] _threadLastCycles = new ulong[ThreadCount];
        private static ulong _lastProcessCycles;
        private static double _cyclesPerSecond;

        private static readonly object _flushLock = new();
        private static System.Threading.Timer? _timer;
        private static StreamWriter? _writer;
        private static long _lastFlushTimestamp;
        private static TimeSpan _lastProcessCpu;
        private static long _lastAllocated;
        private static int _lastGen0, _lastGen1, _lastGen2;
        private static readonly Stopwatch _clock = new();

        public static string? OutputPath { get; private set; }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
        [DllImport("kernel32.dll")] private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

        private static bool DetectEnabled()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("LFSDT_PERF") == "1") return true;
                foreach (var arg in Environment.GetCommandLineArgs())
                    if (string.Equals(arg, "--perf", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
            }
            return false;
        }

        public static void Initialize()
        {
            if (!Enabled || _timer != null) return;
            try
            {
                string dir = Path.Combine(Application.StartupPath, "PerfLogs");
                Directory.CreateDirectory(dir);
                LogRetention.PruneBeforeNewRecording(dir, "perf_*.csv", "wheels_*.csv");
                OutputPath = Path.Combine(dir, $"perf_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _writer = new StreamWriter(OutputPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                _writer.WriteLine("t_s,kind,name,calls_per_s,avg_us,max_us,core_pct,value_per_s");
                _clock.Start();
                _lastFlushTimestamp = Stopwatch.GetTimestamp();
                _cyclesPerSecond = MeasureCyclesPerSecond();
                QueryProcessCycleTime(GetCurrentProcess(), out _lastProcessCycles);
                _lastProcessCpu = Process.GetCurrentProcess().TotalProcessorTime;
                _lastAllocated = GC.GetTotalAllocatedBytes();
                _lastGen0 = GC.CollectionCount(0);
                _lastGen1 = GC.CollectionCount(1);
                _lastGen2 = GC.CollectionCount(2);
                _timer = new System.Threading.Timer(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);
            }
            catch
            {
                _writer = null;
            }
        }

        private static double MeasureCyclesPerSecond()
        {
            IntPtr self = GetCurrentThread();
            if (!QueryThreadCycleTime(self, out ulong start)) return 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 200)
            {
            }
            if (!QueryThreadCycleTime(self, out ulong end)) return 0;
            double seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? (end - start) / seconds : 0;
        }

        public static void Shutdown()
        {
            if (!Enabled) return;
            _timer?.Dispose();
            _timer = null;
            lock (_flushLock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        public static void RegisterThread(PerfThread thread)
        {
            if (!Enabled || _threadHandles[(int)thread] != IntPtr.Zero) return;
            IntPtr handle = OpenThread(ThreadQueryLimitedInformation, false, GetCurrentThreadId());
            if (handle == IntPtr.Zero) return;
            if (GetThreadTimes(handle, out _, out _, out long kernel, out long user))
                _threadLastCpu[(int)thread] = kernel + user;
            if (QueryThreadCycleTime(handle, out ulong cycles))
                _threadLastCycles[(int)thread] = cycles;
            _threadHandles[(int)thread] = handle;
        }

        public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

        public static void End(PerfSection section, long start)
        {
            if (!Enabled) return;
            long elapsed = Stopwatch.GetTimestamp() - start;
            int i = (int)section;
            Interlocked.Add(ref _ticks[i], elapsed);
            Interlocked.Increment(ref _calls[i]);
            long max = Volatile.Read(ref _max[i]);
            while (elapsed > max)
            {
                long seen = Interlocked.CompareExchange(ref _max[i], elapsed, max);
                if (seen == max) break;
                max = seen;
            }
        }

        private sealed class NamedStat
        {
            public long Ticks;
            public long Calls;
            public long Max;
        }

        private static readonly object _namedLock = new();
        private static readonly Dictionary<string, NamedStat> _named = new();

        public static void RecordNamed(string name, long elapsedTicks)
        {
            if (!Enabled) return;
            lock (_namedLock)
            {
                if (!_named.TryGetValue(name, out var stat))
                {
                    stat = new NamedStat();
                    _named[name] = stat;
                }
                stat.Ticks += elapsedTicks;
                stat.Calls++;
                if (elapsedTicks > stat.Max) stat.Max = elapsedTicks;
            }
        }

        public static void InstallMessageProbe()
        {
            if (!Enabled) return;
            var probe = new UiMessageProbe();
            Application.AddMessageFilter(probe);
            Application.Idle += (s, e) => probe.Close(Stopwatch.GetTimestamp());
        }

        private sealed class UiMessageProbe : IMessageFilter
        {
            private bool _pending;
            private int _msg;
            private IntPtr _hwnd;
            private long _start;
            private readonly Dictionary<(int, IntPtr), string> _names = new();

            public bool PreFilterMessage(ref Message m)
            {
                long now = Stopwatch.GetTimestamp();
                Close(now);
                _pending = true;
                _msg = m.Msg;
                _hwnd = m.HWnd;
                _start = now;
                return false;
            }

            public void Close(long now)
            {
                if (!_pending) return;
                _pending = false;
                RecordNamed(NameOf(_msg, _hwnd), now - _start);
            }

            private string NameOf(int msg, IntPtr hwnd)
            {
                if (_names.TryGetValue((msg, hwnd), out var name)) return name;
                string msgName = msg switch
                {
                    0x000F => "WM_PAINT",
                    0x0113 => "WM_TIMER",
                    0x0014 => "WM_ERASEBKGND",
                    0x0085 => "WM_NCPAINT",
                    >= 0x0200 and <= 0x020E => "WM_MOUSE",
                    >= 0x0100 and <= 0x0109 => "WM_KEY",
                    >= 0xC000 and <= 0xFFFF => "WM_REGISTERED",
                    _ => "WM_0x" + msg.ToString("X4"),
                };
                string target = "?";
                try
                {
                    var control = Control.FromHandle(hwnd);
                    if (control != null)
                        target = string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.GetType().Name + "." + control.Name;
                }
                catch
                {
                }
                name = msgName + ":" + target;
                if (_names.Count < 4096) _names[(msg, hwnd)] = name;
                return name;
            }
        }

        public static void Count(PerfCounter counter, long value = 1)
        {
            if (!Enabled) return;
            Interlocked.Add(ref _counters[(int)counter], value);
        }

        private static void Flush()
        {
            lock (_flushLock)
            {
                if (_writer == null) return;
                try
                {
                    var c = CultureInfo.InvariantCulture;
                    long now = Stopwatch.GetTimestamp();
                    double windowSec = (now - _lastFlushTimestamp) / (double)Stopwatch.Frequency;
                    _lastFlushTimestamp = now;
                    if (windowSec <= 0) return;
                    string t = _clock.Elapsed.TotalSeconds.ToString("F1", c);
                    double tickUs = 1_000_000.0 / Stopwatch.Frequency;

                    var process = Process.GetCurrentProcess();
                    TimeSpan cpu = process.TotalProcessorTime;
                    double processCore = (cpu - _lastProcessCpu).TotalSeconds / windowSec * 100.0;
                    _lastProcessCpu = cpu;
                    _writer.WriteLine($"{t},process,cpu,,,,{processCore.ToString("F2", c)},");
                    if (_cyclesPerSecond > 0 && QueryProcessCycleTime(GetCurrentProcess(), out ulong processCycles))
                    {
                        double cycleCore = (processCycles - _lastProcessCycles) / _cyclesPerSecond / windowSec * 100.0;
                        _lastProcessCycles = processCycles;
                        _writer.WriteLine($"{t},process,cycles,,,,{cycleCore.ToString("F2", c)},");
                    }

                    long allocated = GC.GetTotalAllocatedBytes();
                    _writer.WriteLine($"{t},gc,alloc_kb,,,,,{((allocated - _lastAllocated) / 1024.0 / windowSec).ToString("F1", c)}");
                    _lastAllocated = allocated;
                    int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                    _writer.WriteLine($"{t},gc,gen0,,,,,{((g0 - _lastGen0) / windowSec).ToString("F2", c)}");
                    _writer.WriteLine($"{t},gc,gen1,,,,,{((g1 - _lastGen1) / windowSec).ToString("F2", c)}");
                    _writer.WriteLine($"{t},gc,gen2,,,,,{((g2 - _lastGen2) / windowSec).ToString("F2", c)}");
                    _lastGen0 = g0; _lastGen1 = g1; _lastGen2 = g2;

                    for (int i = 0; i < ThreadCount; i++)
                    {
                        IntPtr handle = _threadHandles[i];
                        if (handle == IntPtr.Zero) continue;
                        if (!GetThreadTimes(handle, out _, out _, out long kernel, out long user)) continue;
                        long total = kernel + user;
                        double core = (total - _threadLastCpu[i]) / 1e7 / windowSec * 100.0;
                        _threadLastCpu[i] = total;
                        _writer.WriteLine($"{t},thread,{(PerfThread)i},,,,{core.ToString("F2", c)},");
                        if (_cyclesPerSecond > 0 && QueryThreadCycleTime(handle, out ulong threadCycles))
                        {
                            double cycleCore = (threadCycles - _threadLastCycles[i]) / _cyclesPerSecond / windowSec * 100.0;
                            _threadLastCycles[i] = threadCycles;
                            _writer.WriteLine($"{t},thread_cycles,{(PerfThread)i},,,,{cycleCore.ToString("F2", c)},");
                        }
                    }

                    for (int i = 0; i < SectionCount; i++)
                    {
                        long ticks = Interlocked.Exchange(ref _ticks[i], 0);
                        long calls = Interlocked.Exchange(ref _calls[i], 0);
                        long max = Interlocked.Exchange(ref _max[i], 0);
                        if (calls == 0) continue;
                        double avgUs = ticks * tickUs / calls;
                        double core = ticks / (double)Stopwatch.Frequency / windowSec * 100.0;
                        _writer.WriteLine($"{t},section,{(PerfSection)i},{(calls / windowSec).ToString("F1", c)},{avgUs.ToString("F1", c)},{(max * tickUs).ToString("F1", c)},{core.ToString("F2", c)},");
                    }

                    for (int i = 0; i < CounterCount; i++)
                    {
                        long value = Interlocked.Exchange(ref _counters[i], 0);
                        _writer.WriteLine($"{t},counter,{(PerfCounter)i},,,,,{(value / windowSec).ToString("F1", c)}");
                    }

                    lock (_namedLock)
                    {
                        foreach (var pair in _named)
                        {
                            var stat = pair.Value;
                            if (stat.Calls == 0) continue;
                            double avgUs = stat.Ticks * tickUs / stat.Calls;
                            double core = stat.Ticks / (double)Stopwatch.Frequency / windowSec * 100.0;
                            _writer.WriteLine($"{t},msg,{pair.Key},{(stat.Calls / windowSec).ToString("F1", c)},{avgUs.ToString("F1", c)},{(stat.Max * tickUs).ToString("F1", c)},{core.ToString("F2", c)},");
                            stat.Ticks = 0;
                            stat.Calls = 0;
                            stat.Max = 0;
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }
}
