using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace LFSDriftTools
{
    public sealed class AssistRecorder
    {
        public const double MaxDurationSec = 600.0;

        private const double SegmentStartHold = 0.5;
        private const double SegmentEndHold = 0.1;
        private const double SegmentStartBodySlipDeg = 6.0;
        private const double SegmentEndBodySlipDeg = 3.0;
        private const double SegmentMinSpeedKmh = 30.0;
        private const double SegmentEndDelaySec = 0.7;
        private const double ReversalRatePercentPerSec = 150.0;
        private const double StickMovingRatePercentPerSec = 100.0;
        private const double StickRecentSec = 0.3;

        private sealed class Segment
        {
            public int Number;
            public double StartSec;
            public double EndSec;
            public int Samples;
            public double SpeedSum;
            public double FrontSlipSum;
            public double RearSlipSum;
            public double MaxOutputRate;
            public int Reversals;
            public int ReversalsWhileSteering;
            public double StickTravel;
            public double OutputTravel;
            public double TorqueSum;
            public double TorqueMax;
            public int TorqueSamples;
        }

        private readonly object _lock = new();
        private StreamWriter? _writer;
        private string _csvPath = "";
        private string _summaryPath = "";
        private string _header = "";
        private readonly Stopwatch _clock = new();
        private readonly List<Segment> _segments = new();
        private Segment? _current;
        private double _belowEndSince = -1;
        private bool _hasPrev;
        private double _prevOutput;
        private double _prevRaw;
        private int _outputDirection;
        private double _lastStickMoveSec = double.NegativeInfinity;
        private bool _limitRaised;
        private int _freshSamples;
        private int _torqueSamples;

        private volatile bool _recording;

        public bool IsRecording
        {
            get => _recording;
            private set => _recording = value;
        }

        public event Action<int>? SegmentStarted;
        public event Action? LimitReached;

        public string Start(string directory, string header)
        {
            lock (_lock)
            {
                StopInternal();

                Directory.CreateDirectory(directory);
                LogRetention.PruneBeforeNewRecording(directory, "drift_*.csv", "drift_*_summary.txt");
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                _csvPath = Path.Combine(directory, $"drift_{stamp}.csv");
                _summaryPath = Path.Combine(directory, $"drift_{stamp}_summary.txt");
                _header = header;

                _writer = new StreamWriter(_csvPath, false, new UTF8Encoding(false), 1 << 16);
                _writer.WriteLine("t_s,segment,dt_ms,sample_new,data_age_ms,data_fresh,speed_kmh,raw_pct,player_target_pct,"
                    + "stick_angle_deg,target_angle_deg,front_slip_raw_deg,front_slip_deg,rear_slip_deg,body_slip_deg,"
                    + "yaw_rate_dps,front_steer_deg,input_steer_deg,blend,handover,hold_weight,angle_error_deg,"
                    + "servo_raw_pct,servo_smoothed_pct,desired_offset_pct,assist_offset_pct,target_pct,output_pct,"
                    + "body_slip_growth_dps,spin_catch_pct,"
                    + "rl_slip_deg,rr_slip_deg,fl_slip_deg,fr_slip_deg,rl_load_n,rr_load_n,fl_load_n,fr_load_n,"
                    + "rl_slip_ratio,rr_slip_ratio,fl_slip_ratio,fr_slip_ratio,rl_touch,rr_touch,fl_touch,fr_touch,"
                    + "steer_torque_valid,steer_torque_nm,"
                    + "throttle_pct,brake_pct,gear,pad_lt_pct,pad_rt_pct,limited_player_pct,straight_stab_pct,throttle_cut_pct,pad_rt_out_pct,tc_slip_ratio,tc_cut_pct");

                _segments.Clear();
                _current = null;
                _belowEndSince = -1;
                _hasPrev = false;
                _outputDirection = 0;
                _lastStickMoveSec = double.NegativeInfinity;
                _limitRaised = false;
                _freshSamples = 0;
                _torqueSamples = 0;
                _clock.Restart();
                IsRecording = true;
                return _csvPath;
            }
        }

        public (string CsvPath, string SummaryPath, int Segments)? Stop()
        {
            lock (_lock)
            {
                if (!IsRecording) return null;
                return StopInternal();
            }
        }

        public void Record(AssistTrace t)
        {
            if (!_recording) return;

            int startedSegment = 0;
            bool limitHit = false;

            lock (_lock)
            {
                if (!IsRecording || _writer == null) return;

                double now = _clock.Elapsed.TotalSeconds;
                if (now > MaxDurationSec)
                {
                    if (!_limitRaised)
                    {
                        _limitRaised = true;
                        limitHit = true;
                    }
                }
                else
                {
                    startedSegment = UpdateSegments(t, now);
                    WriteRow(t, now);
                }
            }

            if (startedSegment > 0) SegmentStarted?.Invoke(startedSegment);
            if (limitHit) LimitReached?.Invoke();
        }

        private int UpdateSegments(AssistTrace t, double now)
        {
            int started = 0;
            double dtSec = Math.Max(0.0005, t.DtMs / 1000.0);

            double outputRate = 0;
            double stickRate = 0;
            if (_hasPrev)
            {
                outputRate = (t.OutputPercent - _prevOutput) / dtSec;
                stickRate = (t.RawPercent - _prevRaw) / dtSec;
            }

            if (t.DataFresh)
            {
                _freshSamples++;
                if (t.SteerTorqueValid) _torqueSamples++;
            }

            if (Math.Abs(stickRate) >= StickMovingRatePercentPerSec)
                _lastStickMoveSec = now;

            if (_current == null)
            {
                bool sliding = Math.Abs(t.BodySlipDeg) >= SegmentStartBodySlipDeg && t.SpeedKmh >= SegmentMinSpeedKmh;
                if (t.HoldWeight >= SegmentStartHold || sliding)
                {
                    _current = new Segment { Number = _segments.Count + 1, StartSec = now, EndSec = now };
                    _segments.Add(_current);
                    _belowEndSince = -1;
                    _outputDirection = 0;
                    started = _current.Number;
                }
            }
            else
            {
                if (t.HoldWeight < SegmentEndHold && (Math.Abs(t.BodySlipDeg) < SegmentEndBodySlipDeg || t.SpeedKmh < SegmentMinSpeedKmh))
                {
                    if (_belowEndSince < 0) _belowEndSince = now;
                    else if (now - _belowEndSince >= SegmentEndDelaySec)
                    {
                        _current.EndSec = _belowEndSince;
                        _current = null;
                    }
                }
                else
                {
                    _belowEndSince = -1;
                }
            }

            if (_current != null)
            {
                var s = _current;
                s.EndSec = now;
                s.Samples++;
                s.SpeedSum += t.SpeedKmh;
                s.FrontSlipSum += Math.Abs(t.FrontSlipDeg);
                s.RearSlipSum += Math.Abs(t.RearSlipDeg);
                if (t.SteerTorqueValid)
                {
                    s.TorqueSum += Math.Abs(t.SteerTorqueNm);
                    s.TorqueMax = Math.Max(s.TorqueMax, Math.Abs(t.SteerTorqueNm));
                    s.TorqueSamples++;
                }
                if (_hasPrev)
                {
                    s.MaxOutputRate = Math.Max(s.MaxOutputRate, Math.Abs(outputRate));
                    s.StickTravel += Math.Abs(t.RawPercent - _prevRaw);
                    s.OutputTravel += Math.Abs(t.OutputPercent - _prevOutput);

                    int direction = outputRate >= ReversalRatePercentPerSec ? 1
                        : outputRate <= -ReversalRatePercentPerSec ? -1 : 0;
                    if (direction != 0)
                    {
                        if (_outputDirection != 0 && direction != _outputDirection)
                        {
                            s.Reversals++;
                            if (now - _lastStickMoveSec <= StickRecentSec)
                                s.ReversalsWhileSteering++;
                        }
                        _outputDirection = direction;
                    }
                }
            }

            _prevOutput = t.OutputPercent;
            _prevRaw = t.RawPercent;
            _hasPrev = true;
            return started;
        }

        private readonly StringBuilder _row = new(640);
        private readonly char[] _number = new char[48];

        private StringBuilder Num(double value, string format)
        {
            if (value.TryFormat(_number, out int written, format, CultureInfo.InvariantCulture))
                _row.Append(_number, 0, written);
            else
                _row.Append(value.ToString(format, CultureInfo.InvariantCulture));
            return _row;
        }

        private StringBuilder Col(double value, string format)
        {
            _row.Append(',');
            return Num(value, format);
        }

        private StringBuilder Col(bool value) => _row.Append(',').Append(value ? 1 : 0);

        private void WriteRow(AssistTrace t, double now)
        {
            _row.Clear();
            Num(now, "F4");
            _row.Append(',').Append(_current?.Number ?? 0);
            Col(t.DtMs, "F2");
            Col(t.SampleNew);
            Col(t.DataAgeMs, "F1");
            Col(t.DataFresh);
            Col(t.SpeedKmh, "F3"); Col(t.RawPercent, "F3"); Col(t.PlayerTargetPercent, "F3"); Col(t.StickAngleDeg, "F3"); Col(t.TargetAngleDeg, "F3");
            Col(t.FrontSlipRawDeg, "F3"); Col(t.FrontSlipDeg, "F3"); Col(t.RearSlipDeg, "F3"); Col(t.BodySlipDeg, "F3"); Col(t.YawRateDegS, "F3");
            Col(t.FrontSteerDeg, "F3"); Col(t.InputSteerDeg, "F3"); Col(t.Blend, "F3"); Col(t.Handover, "F3"); Col(t.HoldWeight, "F3"); Col(t.AngleErrorDeg, "F3");
            Col(t.ServoRawPercent, "F3"); Col(t.ServoSmoothedPercent, "F3"); Col(t.DesiredOffsetPercent, "F3"); Col(t.AssistOffsetPercent, "F3");
            Col(t.TargetPercent, "F3"); Col(t.OutputPercent, "F3"); Col(t.BodySlipGrowthDegS, "F3"); Col(t.SpinCatchPercent, "F3");
            for (int w = 0; w < 4; w++) Col(t.WheelSlipDeg[w], "F3");
            for (int w = 0; w < 4; w++) Col(t.WheelLoadN[w], "F3");
            for (int w = 0; w < 4; w++) Col(t.WheelSlipRatio[w], "F3");
            for (int w = 0; w < 4; w++) Col(t.WheelTouching[w]);
            Col(t.SteerTorqueValid);
            Col(t.SteerTorqueNm, "F3");
            Col(t.ThrottlePercent, "F1");
            Col(t.BrakePercent, "F1");
            _row.Append(',').Append(t.Gear);
            Col(t.PadLeftTriggerPercent, "F1");
            Col(t.PadRightTriggerPercent, "F1");
            Col(t.LimitedPlayerPercent, "F2");
            Col(t.StraightStabilityPercent, "F2");
            Col(t.ThrottleCutPercent, "F1");
            Col(t.ThrottleOutPercent, "F1");
            Col(t.TractionSlipRatio, "F3");
            Col(t.TractionCutPercent, "F1");
            _writer!.WriteLine(_row);
        }

        private (string CsvPath, string SummaryPath, int Segments)? StopInternal()
        {
            if (!IsRecording) return null;
            IsRecording = false;
            _clock.Stop();

            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Assist log close", ex);
            }
            _writer = null;

            WriteSummary();
            return (_csvPath, _summaryPath, _segments.Count);
        }

        private void WriteSummary()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"Drift assist log {Path.GetFileName(_csvPath)}");
            sb.AppendLine($"Recorded: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", c)}, length {_clock.Elapsed.TotalSeconds.ToString("F1", c)} s");
            sb.AppendLine();
            sb.AppendLine(_header);
            sb.AppendLine();
            if (_torqueSamples > 0)
                sb.AppendLine($"Steer torque: received in {(100.0 * _torqueSamples / Math.Max(1, _freshSamples)).ToString("F0", c)}% of OutSim samples.");
            else
                sb.AppendLine("Steer torque: not received. Set OutSim Opts to 1bf in the app and in cfg.txt, then restart LFS.");
            sb.AppendLine();
            sb.AppendLine("Segments (assist weight above 50%, or body slip above 6 deg at 30 km/h or more):");
            sb.AppendLine("Reversals = output changed direction faster than 150 %/s; while steering = within 0.3 s of a stick movement.");
            sb.AppendLine();

            foreach (var s in _segments)
            {
                int n = Math.Max(1, s.Samples);
                sb.AppendLine($"#{s.Number}  {s.StartSec.ToString("F1", c)} s - {s.EndSec.ToString("F1", c)} s  ({(s.EndSec - s.StartSec).ToString("F1", c)} s)");
                sb.AppendLine($"    speed {(s.SpeedSum / n).ToString("F0", c)} km/h, front slip {(s.FrontSlipSum / n).ToString("F1", c)} deg, rear slip {(s.RearSlipSum / n).ToString("F1", c)} deg");
                sb.AppendLine($"    stick travel {s.StickTravel.ToString("F0", c)} %, output travel {s.OutputTravel.ToString("F0", c)} %, max output rate {s.MaxOutputRate.ToString("F0", c)} %/s");
                sb.AppendLine($"    reversals {s.Reversals}, while steering {s.ReversalsWhileSteering}");
                if (s.TorqueSamples > 0)
                    sb.AppendLine($"    steer torque avg {(s.TorqueSum / s.TorqueSamples).ToString("F1", c)} Nm, max {s.TorqueMax.ToString("F1", c)} Nm");
                sb.AppendLine("    notes: ");
                sb.AppendLine();
            }

            if (_segments.Count == 0)
                sb.AppendLine("No drift segments recorded.");

            try
            {
                File.WriteAllText(_summaryPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Assist log summary", ex);
            }
        }
    }
}
