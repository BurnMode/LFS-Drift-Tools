using System;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LFSDriftTools
{
    [Flags]
    public enum OutSimOpts : uint
    {
        None = 0,
        Header = 1,
        Id = 2,
        Time = 4,
        Main = 8,
        Inputs = 16,
        Drive = 32,
        Distance = 64,
        Wheels = 128,
        Extra1 = 256,

        Full = Header | Id | Time | Main | Inputs | Drive | Distance | Wheels | Extra1
    }

    public struct OutSimWheelSample
    {
        public float SuspDeflect;
        public float SteerRad;
        public float XForce;
        public float YForce;
        public float VerticalLoad;
        public float AngVel;
        public float LeanRelToRoad;
        public byte AirTempC;
        public byte SlipFraction;
        public bool Touching;
        public float SlipRatio;
        public float TanSlipAngle;

        public float SlipAngleRad => MathF.Atan(TanSlipAngle);
    }

    public class OutSimSample
    {
        public DateTime ReceivedAtUtc;

        public bool HasMain;
        public bool HasInputs;
        public bool HasDrive;
        public bool HasWheels;
        public bool HasExtra1;

        public int Id;
        public uint TimeMs;

        public Vector3 AngVel;
        public float Heading;
        public float Pitch;
        public float Roll;
        public Vector3 Accel;
        public Vector3 Vel;
        public Vector3 Pos;

        public float Throttle;
        public float Brake;
        public float InputSteerRad;
        public float Clutch;
        public float Handbrake;

        public byte Gear;
        public float EngineAngVel;
        public float MaxTorqueAtVel;

        public float CurrentLapDist;
        public float IndexedDistance;

        public OutSimWheelSample[] Wheels = new OutSimWheelSample[4];

        public float SteerTorqueNm;

        public float SpeedMs => Vel.Length();
        public float SpeedKmh => SpeedMs * 3.6f;

        public float RearSlipAngleRad()
        {
            if (!HasWheels || Wheels.Length < 4) return 0f;
            return (Wheels[2].SlipAngleRad + Wheels[3].SlipAngleRad) * 0.5f;
        }
    }

    public class OutSimConnection : IDisposable
    {
        public int UdpPort { get; set; } = 30020;

        public OutSimOpts Opts { get; set; } =
            OutSimOpts.Header | OutSimOpts.Id | OutSimOpts.Time | OutSimOpts.Main |
            OutSimOpts.Inputs | OutSimOpts.Drive | OutSimOpts.Wheels;

        public OutSimSample? LastSample { get; private set; }
        public bool IsRunning { get; private set; }

        public long PacketsReceived { get; private set; }
        public long PacketsRejected { get; private set; }

        public event Action<OutSimSample>? DataReceived;
        public event Action<string>? Error;
        public event Action<string>? Warning;

        private UdpClient? _udp;
        private CancellationTokenSource _cts = new();
        private DateTime _lastRejectWarnAtUtc = DateTime.MinValue;

        public void Start()
        {
            if (IsRunning) return;
            try
            {
                _cts = new CancellationTokenSource();
                _udp = new UdpClient(UdpPort);
                _udp.Client.ReceiveTimeout = 2000;
                IsRunning = true;
                PacketsReceived = 0;
                PacketsRejected = 0;
                var udp = _udp;
                var token = _cts.Token;
                Task.Run(() => ReceiveLoop(udp, token));
            }
            catch (Exception ex)
            {
                Error?.Invoke(string.Format(Localization.T("status.outgauge.udp_open_error"), UdpPort, ex.Message));
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _cts.Cancel();
            _udp?.Close();
            _udp = null;
            IsRunning = false;
            LastSample = null;
        }

        private void ReceiveLoop(UdpClient udp, CancellationToken ct)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] data = udp.Receive(ref ep);

                    if (OutSimPacketParser.TryParse(data, Opts, out var sample) && sample != null)
                    {
                        PacketsReceived++;
                        LastSample = sample;
                        DataReceived?.Invoke(sample);
                    }
                    else
                    {
                        PacketsRejected++;

                        if ((DateTime.UtcNow - _lastRejectWarnAtUtc).TotalSeconds > 5)
                        {
                            _lastRejectWarnAtUtc = DateTime.UtcNow;
                            Warning?.Invoke(string.Format(Localization.T("status.outsim.opts_mismatch"), data.Length));
                        }
                    }
                }
                catch (SocketException)
                {
                    if (ct.IsCancellationRequested) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Error?.Invoke(string.Format(Localization.T("status.outsim.receive_error"), ex.Message));
                }
            }
        }

        public void Dispose() => Stop();
    }

    internal static class OutSimPacketParser
    {
        public static bool TryParse(byte[] data, OutSimOpts opts, out OutSimSample? sample)
        {
            sample = null;
            try
            {
                int offset = 0;
                var s = new OutSimSample { ReceivedAtUtc = DateTime.UtcNow };

                if (opts.HasFlag(OutSimOpts.Header))
                    offset += 4;

                if (opts.HasFlag(OutSimOpts.Id))
                    s.Id = ReadInt(data, ref offset);

                if (opts.HasFlag(OutSimOpts.Time))
                    s.TimeMs = ReadUInt(data, ref offset);

                if (opts.HasFlag(OutSimOpts.Main))
                {
                    s.AngVel = ReadVector3(data, ref offset);
                    s.Heading = ReadFloat(data, ref offset);
                    s.Pitch = ReadFloat(data, ref offset);
                    s.Roll = ReadFloat(data, ref offset);
                    s.Accel = ReadVector3(data, ref offset);
                    s.Vel = ReadVector3(data, ref offset);
                    s.Pos = ReadFixedVec(data, ref offset);
                    s.HasMain = true;
                }

                if (opts.HasFlag(OutSimOpts.Inputs))
                {
                    s.Throttle = ReadFloat(data, ref offset);
                    s.Brake = ReadFloat(data, ref offset);
                    s.InputSteerRad = ReadFloat(data, ref offset);
                    s.Clutch = ReadFloat(data, ref offset);
                    s.Handbrake = ReadFloat(data, ref offset);
                    s.HasInputs = true;
                }

                if (opts.HasFlag(OutSimOpts.Drive))
                {
                    s.Gear = data[offset]; offset += 4;
                    s.EngineAngVel = ReadFloat(data, ref offset);
                    s.MaxTorqueAtVel = ReadFloat(data, ref offset);
                    s.HasDrive = true;
                }

                if (opts.HasFlag(OutSimOpts.Distance))
                {
                    s.CurrentLapDist = ReadFloat(data, ref offset);
                    s.IndexedDistance = ReadFloat(data, ref offset);
                }

                if (opts.HasFlag(OutSimOpts.Wheels))
                {
                    for (int w = 0; w < 4; w++)
                    {
                        var wheel = new OutSimWheelSample
                        {
                            SuspDeflect = ReadFloat(data, ref offset),
                            SteerRad = ReadFloat(data, ref offset),
                            XForce = ReadFloat(data, ref offset),
                            YForce = ReadFloat(data, ref offset),
                            VerticalLoad = ReadFloat(data, ref offset),
                            AngVel = ReadFloat(data, ref offset),
                            LeanRelToRoad = ReadFloat(data, ref offset),
                        };
                        wheel.AirTempC = data[offset]; offset++;
                        wheel.SlipFraction = data[offset]; offset++;
                        wheel.Touching = data[offset] != 0; offset++;
                        offset++;
                        wheel.SlipRatio = ReadFloat(data, ref offset);
                        wheel.TanSlipAngle = ReadFloat(data, ref offset);
                        s.Wheels[w] = wheel;
                    }
                    s.HasWheels = true;
                }

                if (opts.HasFlag(OutSimOpts.Extra1))
                {
                    s.SteerTorqueNm = ReadFloat(data, ref offset);
                    offset += 4;
                    s.HasExtra1 = true;
                }

                sample = s;
                return true;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
            {
                return false;
            }
        }

        private static float ReadFloat(byte[] d, ref int offset)
        {
            float v = BitConverter.ToSingle(d, offset);
            offset += 4;
            return v;
        }

        private static int ReadInt(byte[] d, ref int offset)
        {
            int v = BitConverter.ToInt32(d, offset);
            offset += 4;
            return v;
        }

        private static uint ReadUInt(byte[] d, ref int offset)
        {
            uint v = BitConverter.ToUInt32(d, offset);
            offset += 4;
            return v;
        }

        private static Vector3 ReadVector3(byte[] d, ref int offset)
        {
            float x = ReadFloat(d, ref offset);
            float y = ReadFloat(d, ref offset);
            float z = ReadFloat(d, ref offset);
            return new Vector3(x, y, z);
        }

        private static Vector3 ReadFixedVec(byte[] d, ref int offset)
        {
            int x = ReadInt(d, ref offset);
            int y = ReadInt(d, ref offset);
            int z = ReadInt(d, ref offset);
            const float scale = 1f / 65536f;
            return new Vector3(x * scale, y * scale, z * scale);
        }
    }
}
