using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LFSDriftTools
{
    public abstract class UdpTelemetryReceiver : IDisposable
    {
        private const double ReceiveErrorThrottleSec = 5.0;
        private const int MaxDatagramBytes = 65536;

        public int UdpPort { get; set; }
        public bool IsRunning { get; private set; }
        public long PacketsReceived { get; protected set; }

        public event Action<string>? Error;

        private UdpClient? _udp;
        private CancellationTokenSource _cts = new();
        private DateTime _lastReceiveErrorAtUtc = DateTime.MinValue;

        protected UdpTelemetryReceiver(int defaultPort)
        {
            UdpPort = defaultPort;
        }

        protected abstract PerfThread ReceiveThread { get; }
        protected abstract string ReceiveErrorKey { get; }
        protected abstract string ErrorLogSource { get; }

        protected abstract void ResetOnStart();
        protected abstract void ClearOnStop();
        protected abstract void HandlePacket(byte[] data);

        public void Start()
        {
            if (IsRunning) return;
            StartFailed = false;
            PortBlockedBy = null;
            try
            {
                _cts = new CancellationTokenSource();
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, UdpPort));
                _udp.Client.ReceiveTimeout = 2000;
                IsRunning = true;
                PacketsReceived = 0;
                ResetOnStart();
                var udp = _udp;
                var token = _cts.Token;
                Task.Run(() => ReceiveLoop(udp, token));
            }
            catch (Exception ex)
            {
                IsRunning = false;
                if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied })
                {
                    PortBlockedBy = SystemProbe.FindUdpPortOwner(UdpPort) ?? "";
                    StartFailed = true;
                    Error?.Invoke(PortBlockedBy.Length > 0
                        ? string.Format(Localization.T("status.udp_port_in_use"), UdpPort, PortBlockedBy)
                        : string.Format(Localization.T("status.udp_port_in_use_unknown"), UdpPort));
                    return;
                }
                StartFailed = true;
                Error?.Invoke(string.Format(Localization.T("status.outgauge.udp_open_error"), UdpPort, ex.Message));
            }
        }

        public bool StartFailed { get; private set; }
        public string? PortBlockedBy { get; private set; }

        public void Stop()
        {
            if (!IsRunning) return;
            _cts.Cancel();
            _udp?.Close();
            _udp = null;
            IsRunning = false;
            ClearOnStop();
        }

        public void Dispose() => Stop();

        private void RaiseReceiveError(string message)
        {
            if ((DateTime.UtcNow - _lastReceiveErrorAtUtc).TotalSeconds < ReceiveErrorThrottleSec) return;
            _lastReceiveErrorAtUtc = DateTime.UtcNow;

            try { Error?.Invoke(message); }
            catch (Exception ex) { ErrorLog.Write(ErrorLogSource, ex); }
        }

        private void ReceiveLoop(UdpClient udp, CancellationToken ct)
        {
            PerfProbe.RegisterThread(ReceiveThread);
            var receiveBuffer = new byte[MaxDatagramBytes];
            byte[] packet = Array.Empty<byte>();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int length = udp.Client.Receive(receiveBuffer);
                    if (packet.Length != length)
                        packet = new byte[length];
                    Buffer.BlockCopy(receiveBuffer, 0, packet, 0, length);
                    HandlePacket(packet);
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
                        RaiseReceiveError(string.Format(Localization.T(ReceiveErrorKey), ex.Message));
                }
            }
        }
    }
}
