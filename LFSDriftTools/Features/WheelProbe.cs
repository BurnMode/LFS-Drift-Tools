using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public static class WheelProbe
    {
        public static readonly bool Enabled = DetectEnabled();

        private static readonly object _sync = new();
        private static StreamWriter? _writer;
        private static DateTime _startUtc;
        private static DateTime _lastFlushUtc;
        private static readonly StringBuilder _line = new(512);
        private static volatile float _rpm;
        private static volatile int _gear;
        private static volatile float _throttle;
        private static volatile uint _lights;
        private static volatile string _car = "";

        private static bool DetectEnabled()
        {
            try
            {
                foreach (var arg in Environment.GetCommandLineArgs())
                    if (string.Equals(arg, "--wheellog", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
            }
            return false;
        }

        public static void Initialize()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_writer != null) return;
                try
                {
                    string dir = Path.Combine(Application.StartupPath, "PerfLogs");
                    Directory.CreateDirectory(dir);
                    LogRetention.PruneBeforeNewRecording(dir, "perf_*.csv", "wheels_*.csv");
                    _writer = new StreamWriter(Path.Combine(dir, $"wheels_{DateTime.Now:yyyyMMdd_HHmmss}.csv"), false, new UTF8Encoding(false));
                    var header = new StringBuilder("src,t_s,car,speed_kmh,fwd_kmh,roll_deg,pitch_deg,accel_ms2,rpm,gear,throttle,lights");
                    foreach (var w in new[] { "rl", "rr", "fl", "fr" })
                        header.Append($",{w}_angvel,{w}_radius_m,{w}_susp,{w}_load,{w}_slipratio,{w}_slipfrac,{w}_airtemp,{w}_touch,{w}_lean");
                    _writer.WriteLine(header.ToString());
                    _startUtc = DateTime.UtcNow;
                    _lastFlushUtc = _startUtc;
                }
                catch
                {
                    _writer = null;
                }
            }
        }

        public static void Shutdown()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private static DateTime _lastSimUtc = DateTime.MinValue;
        private static DateTime _lastGaugeRowUtc = DateTime.MinValue;
        private const double GaugeRowGapSec = 0.5;

        public static void NoteGauge(OutGaugeData data)
        {
            if (!Enabled) return;
            _rpm = data.RPM;
            _gear = (int)data.Gear;
            _throttle = data.Throttle;
            _lights = data.ShowLights;
            _car = data.Car ?? "";

            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                if (_writer == null) return;
                if ((now - _lastSimUtc).TotalSeconds < GaugeRowGapSec || (now - _lastGaugeRowUtc).TotalSeconds < GaugeRowGapSec) return;
                _lastGaugeRowUtc = now;
                var c = CultureInfo.InvariantCulture;
                try
                {
                    _writer.WriteLine($"gauge,{(now - _startUtc).TotalSeconds.ToString("F3", c)},{_car},{(data.Speed * 3.6f).ToString("F2", c)},,,,,{data.RPM.ToString("F0", c)},{(int)data.Gear},{data.Throttle.ToString("F2", c)},{data.ShowLights}");
                    _writer.Flush();
                }
                catch
                {
                }
            }
        }

        public static void Record(OutSimSample s)
        {
            if (!Enabled || !s.HasWheels || s.Wheels == null || s.Wheels.Length < 4) return;
            lock (_sync)
            {
                if (_writer == null) return;
                var c = CultureInfo.InvariantCulture;
                float cos = MathF.Cos(-s.Heading), sin = MathF.Sin(-s.Heading);
                float fwd = s.Vel.X * sin + s.Vel.Y * cos;

                _line.Clear();
                _lastSimUtc = s.ReceivedAtUtc;
                _line.Append("sim,").Append((s.ReceivedAtUtc - _startUtc).TotalSeconds.ToString("F3", c)).Append(',')
                    .Append(_car).Append(',')
                    .Append((s.Vel.Length() * 3.6f).ToString("F2", c)).Append(',')
                    .Append((fwd * 3.6f).ToString("F2", c)).Append(',')
                    .Append((s.Roll * 180f / MathF.PI).ToString("F2", c)).Append(',')
                    .Append((s.Pitch * 180f / MathF.PI).ToString("F2", c)).Append(',')
                    .Append(s.Accel.Length().ToString("F2", c)).Append(',')
                    .Append(_rpm.ToString("F0", c)).Append(',')
                    .Append(_gear).Append(',')
                    .Append(_throttle.ToString("F2", c)).Append(',')
                    .Append(_lights);
                for (int i = 0; i < 4; i++)
                {
                    var w = s.Wheels[i];
                    float radius = Math.Abs(w.AngVel) > 1f ? Math.Abs(fwd) / Math.Abs(w.AngVel) : 0f;
                    _line.Append(',').Append(w.AngVel.ToString("F2", c))
                        .Append(',').Append(radius.ToString("F4", c))
                        .Append(',').Append(w.SuspDeflect.ToString("F4", c))
                        .Append(',').Append(w.VerticalLoad.ToString("F0", c))
                        .Append(',').Append(w.SlipRatio.ToString("F3", c))
                        .Append(',').Append(w.SlipFraction)
                        .Append(',').Append(w.AirTempC)
                        .Append(',').Append(w.Touching ? 1 : 0)
                        .Append(',').Append((w.LeanRelToRoad * 180f / MathF.PI).ToString("F2", c));
                }
                try
                {
                    _writer.WriteLine(_line.ToString());
                    if ((s.ReceivedAtUtc - _lastFlushUtc).TotalSeconds >= 1.0)
                    {
                        _writer.Flush();
                        _lastFlushUtc = s.ReceivedAtUtc;
                    }
                }
                catch
                {
                }
            }
        }
    }
}
