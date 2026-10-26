using System;
using System.IO;
using System.Numerics;

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

        public const int RearLeft = 0;
        public const int RearRight = 1;
        public const int FrontLeft = 2;
        public const int FrontRight = 3;

        public float RearSlipAngleRad()
        {
            if (!HasWheels || Wheels.Length < 4) return 0f;
            return (Wheels[RearLeft].SlipAngleRad + Wheels[RearRight].SlipAngleRad) * 0.5f;
        }

        public float FrontSlipAngleRad()
        {
            if (!HasWheels || Wheels.Length < 4) return 0f;
            return (Wheels[FrontLeft].SlipAngleRad + Wheels[FrontRight].SlipAngleRad) * 0.5f;
        }

        public const float MinFrontLoadN = 50f;

        public bool TryFrontSlipAngleRadLoadWeighted(out float slipRad)
        {
            slipRad = 0f;
            if (!HasWheels || Wheels.Length < 4) return false;

            var left = Wheels[FrontLeft];
            var right = Wheels[FrontRight];
            float leftWeight = left.Touching ? Math.Max(0f, left.VerticalLoad) : 0f;
            float rightWeight = right.Touching ? Math.Max(0f, right.VerticalLoad) : 0f;
            float total = leftWeight + rightWeight;
            if (total < MinFrontLoadN) return false;

            slipRad = (left.SlipAngleRad * leftWeight + right.SlipAngleRad * rightWeight) / total;
            return true;
        }

        public float FrontSteerRad()
        {
            if (!HasWheels || Wheels.Length < 4) return 0f;
            return (Wheels[FrontLeft].SteerRad + Wheels[FrontRight].SteerRad) * 0.5f;
        }
    }

    public class OutSimConnection : UdpTelemetryReceiver
    {
        public OutSimOpts Opts { get; set; } =
            OutSimOpts.Header | OutSimOpts.Id | OutSimOpts.Time | OutSimOpts.Main |
            OutSimOpts.Inputs | OutSimOpts.Drive | OutSimOpts.Wheels;

        public OutSimSample? LastSample { get; private set; }
        public long PacketsRejected { get; private set; }

        public event Action<OutSimSample>? DataReceived;
        public event Action<string>? Warning;

        private DateTime _lastRejectWarnAtUtc = DateTime.MinValue;

        public OutSimConnection() : base(30020)
        {
        }

        protected override PerfThread ReceiveThread => PerfThread.OutSim;
        protected override string ReceiveErrorKey => "status.outsim.receive_error";
        protected override string ErrorLogSource => "OutSim error handler";

        protected override void ResetOnStart() => PacketsRejected = 0;

        protected override void ClearOnStop() => LastSample = null;

        protected override void HandlePacket(byte[] data)
        {
            long perfStart = PerfProbe.Begin();
            if (OutSimPacketParser.TryParse(data, Opts, out var sample) && sample != null)
            {
                PacketsReceived++;
                LastSample = sample;
                DataReceived?.Invoke(sample);
                PerfProbe.End(PerfSection.OutSimHandler, perfStart);
                return;
            }

            PacketsRejected++;
            if ((DateTime.UtcNow - _lastRejectWarnAtUtc).TotalSeconds > 5)
            {
                _lastRejectWarnAtUtc = DateTime.UtcNow;
                try { Warning?.Invoke(string.Format(Localization.T("status.outsim.opts_mismatch"), data.Length)); }
                catch (Exception ex) { ErrorLog.Write("OutSim warning handler", ex); }
            }
        }
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

                if (offset != data.Length)
                    return false;

                sample = s;
                return true;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or InvalidDataException)
            {
                return false;
            }
        }

        private static float ReadFloat(byte[] d, ref int offset)
        {
            float v = BitConverter.ToSingle(d, offset);
            if (!float.IsFinite(v))
                throw new InvalidDataException();

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
