using System;

namespace LFSDriftTools
{

    public class OutGaugeData
    {
        public float RPM { get; set; }
        public float Speed { get; set; }
        public float Throttle { get; set; }
        public float Gear { get; set; }
        public float EngTemp { get; set; }
        public float Turbo { get; set; }
        public ushort Flags { get; set; }
        public bool HasTurbo => (Flags & 0x2000) != 0;
        public float Fuel { get; set; }
        public float OilTemp { get; set; }

        public string Car { get; set; } = "";

        public byte PLID { get; set; }

        public uint DashLights { get; set; }
        public uint ShowLights { get; set; }
        public bool Valid { get; set; }

        public bool HandbrakeOn => (ShowLights & 0x0004) != 0;

        public bool FullBeamOn => (ShowLights & 0x0002) != 0;

        public bool LeftSignalOn => (ShowLights & 0x0020) != 0;
        public bool RightSignalOn => (ShowLights & 0x0040) != 0;

        public bool AnySignalOn => (ShowLights & 0x0080) != 0;
    }

    public class OutGaugeConnection : UdpTelemetryReceiver
    {
        public OutGaugeData LastData { get; private set; } = new OutGaugeData();
        public DateTime LastPacketUtc { get; private set; } = DateTime.MinValue;

        public event Action<OutGaugeData>? DataReceived;

        public OutGaugeConnection() : base(35555)
        {
        }

        protected override PerfThread ReceiveThread => PerfThread.OutGauge;
        protected override string ReceiveErrorKey => "status.outgauge.receive_error";
        protected override string ErrorLogSource => "OutGauge error handler";

        protected override void ResetOnStart() => LastPacketUtc = DateTime.MinValue;

        protected override void ClearOnStop() => LastData = new OutGaugeData();

        protected override void HandlePacket(byte[] data)
        {
            if (data.Length < 64) return;
            long perfStart = PerfProbe.Begin();

            var og = ParseOutGauge(data);
            if (!IsFinite(og)) return;

            PacketsReceived++;
            LastPacketUtc = DateTime.UtcNow;
            LastData = og;
            DataReceived?.Invoke(og);
            PerfProbe.End(PerfSection.OutGaugeHandler, perfStart);
        }

        private static bool IsFinite(OutGaugeData og) =>
            float.IsFinite(og.RPM) && float.IsFinite(og.Speed) && float.IsFinite(og.Throttle)
            && float.IsFinite(og.EngTemp) && float.IsFinite(og.Fuel) && float.IsFinite(og.Turbo);

        private OutGaugeData ParseOutGauge(byte[] d)
        {
            return new OutGaugeData
            {
                RPM = BitConverter.ToSingle(d, 16),
                Speed = BitConverter.ToSingle(d, 12),
                Throttle = d.Length >= 52 ? BitConverter.ToSingle(d, 48) : 0f,
                Gear = d[10],
                PLID = d.Length >= 12 ? d[11] : (byte)0,
                EngTemp = d.Length >= 28 ? BitConverter.ToSingle(d, 24) : 0f,
                Turbo = d.Length >= 24 ? BitConverter.ToSingle(d, 20) : 0f,
                Flags = d.Length >= 10 ? BitConverter.ToUInt16(d, 8) : (ushort)0,
                Fuel = d.Length >= 32 ? BitConverter.ToSingle(d, 28) : 0f,
                OilTemp = d.Length >= 40 ? BitConverter.ToSingle(d, 36) : 0f,
                Car = d.Length >= 8 ? CachedCarName(d, 4) : "",
                DashLights = d.Length >= 44 ? BitConverter.ToUInt32(d, 40) : 0u,
                ShowLights = d.Length >= 48 ? BitConverter.ToUInt32(d, 44) : 0u,
                Valid = true,
            };
        }

        private uint _lastCarBytes;
        private string _lastCarName = "";

        private string CachedCarName(byte[] d, int offset)
        {
            uint raw = BitConverter.ToUInt32(d, offset);
            if (raw != _lastCarBytes)
            {
                _lastCarName = ParseCarName(d, offset);
                _lastCarBytes = raw;
            }
            return _lastCarName;
        }

        private static string ParseCarName(byte[] d, int offset)
        {
            if (d[offset] == 0) return "";

            int len = 0;
            while (len < 4 && d[offset + len] != 0) len++;

            bool looksLikeOfficialCode = len > 0;
            for (int i = 0; i < len && looksLikeOfficialCode; i++)
            {
                byte b = d[offset + i];
                looksLikeOfficialCode = (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'0' && b <= (byte)'9');
            }

            if (looksLikeOfficialCode)
                return System.Text.Encoding.ASCII.GetString(d, offset, len);

            return BitConverter.ToString(d, offset, 4).Replace("-", "");
        }
    }
}
