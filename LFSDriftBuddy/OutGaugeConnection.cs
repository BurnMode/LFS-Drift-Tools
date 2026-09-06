using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LFSDriftBuddy
{

    public class OutGaugeData
    {
        public float RPM { get; set; }
        public float Speed { get; set; }
        public float Throttle { get; set; }
        public float Gear { get; set; }
        public float EngTemp { get; set; }
        public float Fuel { get; set; }

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

    public class OutGaugeConnection : IDisposable
    {
        public int UdpPort { get; set; } = 35555;

        public OutGaugeData LastData { get; private set; } = new OutGaugeData();
        public bool IsRunning { get; private set; } = false;

        public event Action<OutGaugeData>? DataReceived;
        public event Action<string>? Error;

        private UdpClient? _udp;
        private CancellationTokenSource _cts = new();

        public void Start()
        {
            if (IsRunning) return;
            try
            {
                _cts = new CancellationTokenSource();
                _udp = new UdpClient(UdpPort);
                _udp.Client.ReceiveTimeout = 2000;
                IsRunning = true;
                Task.Run(() => ReceiveLoop(_cts.Token));
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
            LastData = new OutGaugeData();
        }

        private void ReceiveLoop(CancellationToken ct)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] data = _udp!.Receive(ref ep);
                    if (data.Length < 64) continue;

                    var og = ParseOutGauge(data);
                    LastData = og;
                    DataReceived?.Invoke(og);
                }
                catch (SocketException)
                {
                    if (!ct.IsCancellationRequested)
                        continue;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Error?.Invoke(string.Format(Localization.T("status.outgauge.receive_error"), ex.Message));
                }
            }
        }

        private static OutGaugeData ParseOutGauge(byte[] d)
        {
            return new OutGaugeData
            {
                RPM = BitConverter.ToSingle(d, 16),
                Speed = BitConverter.ToSingle(d, 12),
                Throttle = d.Length >= 52 ? BitConverter.ToSingle(d, 48) : 0f,
                Gear = d[10],
                PLID = d.Length >= 12 ? d[11] : (byte)0,
                EngTemp = d.Length >= 28 ? BitConverter.ToSingle(d, 24) : 0f,
                Fuel = d.Length >= 32 ? BitConverter.ToSingle(d, 28) : 0f,
                Car = d.Length >= 8 ? ParseCarName(d, 4) : "",
                DashLights = d.Length >= 44 ? BitConverter.ToUInt32(d, 40) : 0u,
                ShowLights = d.Length >= 48 ? BitConverter.ToUInt32(d, 44) : 0u,
                Valid = true,
            };
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

        public void Dispose()
        {
            Stop();
        }
    }
}
