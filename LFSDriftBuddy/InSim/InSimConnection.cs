using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LFSDriftBuddy.InSim
{
    public class CarDataEventArgs : EventArgs
    {
        public CompCar Car { get; set; }
    }

    public class StatusEventArgs : EventArgs
    {
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    public class LapCompletedEventArgs : EventArgs
    {
        public byte PLID { get; set; }
        public int LapsDone { get; set; }
        public uint LapTimeMs { get; set; }
    }

    public class PlidEventArgs : EventArgs
    {
        public byte PLID { get; set; }
    }

    public class PlayerNameEventArgs : PlidEventArgs
    {
                public string PName { get; set; }
    }

    public class CheckpointCrossedEventArgs : PlidEventArgs
    {
                public int CheckpointIndex { get; set; }
        public bool Forward { get; set; }
    }

        public class ObjectHitEventArgs : PlidEventArgs
    {
                public byte ObjectIndex { get; set; }

                public string ObjectName { get; set; }
    }

    public class DerbyCarContact
    {
        public byte PLID { get; set; }
        public double SpeedKmh { get; set; }
        public double DirectionDeg { get; set; }
        public double HeadingDeg { get; set; }
        public sbyte AccelF { get; set; }
        public sbyte AccelR { get; set; }
        public int Throttle0to15 { get; set; }
    }

    public class CarContactEventArgs : EventArgs
    {
        public double ClosingSpeedKmh { get; set; }
        public DerbyCarContact A { get; set; }
        public DerbyCarContact B { get; set; }
    }

        public class InSimConnection : IDisposable
    {

        public event EventHandler<CarDataEventArgs> CarDataReceived;
        public event EventHandler<StatusEventArgs> StatusChanged;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        public event EventHandler<string>? ConnectFailed;
        public event EventHandler<LapCompletedEventArgs> LapCompleted;
        public event EventHandler<string> TrackChanged;
        public event EventHandler<string> LayoutChanged;
        public event EventHandler<PlidEventArgs> CarReset;
        public event EventHandler RaceRestarted;
        public event EventHandler<PlidEventArgs> PitLaneEntered;
        public event EventHandler<PlidEventArgs> PitLaneExited;
        public event EventHandler<PlidEventArgs> PlayerPitted;
        public event EventHandler<CheckpointCrossedEventArgs> CheckpointCrossed;
        public event EventHandler<PlidEventArgs> RestrictedAreaEntered;
        public event EventHandler<ObjectHitEventArgs> ObjectHit;
        public event EventHandler<byte[]> RawObjectHitDebug;
        public event EventHandler<CarContactEventArgs> CarContact;
        public event EventHandler<PlayerNameEventArgs> PlayerNamed;

        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private byte[] _buffer = new byte[8192];
        private int _bufferLen = 0;
        private volatile bool _running = false;
        private StateFlags _gameState = 0;
        public StateFlags GameState => _gameState;
        public byte ViewPLID { get; private set; } = 0;
        public string CurrentTrack { get; private set; } = "";

        private readonly Dictionary<byte, string> _playerNames = new();
        private readonly Dictionary<byte, byte> _ucidToPlid = new();
        public string? GetPlayerName(byte plid) => _playerNames.TryGetValue(plid, out var n) ? n : null;
        public string CurrentLayout { get; private set; } = "";

        private static readonly Dictionary<byte, string> AxoObjectNames = new()
        {
            [4] = "CHALK LINE",
            [5] = "CHALK LINE",
            [6] = "CHALK ARROW",
            [7] = "CHALK ARROW",
            [8] = "CHALK ARROW",
            [9] = "CHALK ARROW",
            [10] = "CHALK ARROW",
            [11] = "CHALK ARROW",
            [12] = "CHALK ARROW",
            [13] = "CHALK ARROW",
            [16] = "PAINTED LETTER",
            [17] = "PAINTED ARROW",
            [20] = "CONE",
            [21] = "CONE",
            [32] = "TALL CONE",
            [33] = "TALL CONE",
            [40] = "POINTER CONE",
            [48] = "TYRE",
            [49] = "TYRE STACK",
            [50] = "TYRE STACK",
            [51] = "TYRE STACK",
            [52] = "BIG TYRE",
            [53] = "BIG TYRE STACK",
            [54] = "BIG TYRE STACK",
            [55] = "BIG TYRE STACK",
            [64] = "CORNER MARKER",
            [84] = "DISTANCE MARKER",
            [92] = "LETTER BOARD",
            [93] = "LETTER BOARD",
            [96] = "ARMCO BARRIER",
            [97] = "ARMCO BARRIER",
            [98] = "ARMCO BARRIER",
            [104] = "BARRIER",
            [105] = "BARRIER",
            [106] = "BARRIER",
            [112] = "BANNER",
            [120] = "RAMP",
            [121] = "RAMP",
            [124] = "SUV",
            [125] = "VAN",
            [126] = "TRUCK",
            [127] = "AMBULANCE",
            [128] = "SPEED HUMP",
            [129] = "SPEED HUMP",
            [130] = "SPEED HUMP",
            [131] = "SPEED HUMP",
            [132] = "KERB",
            [136] = "POST",
            [140] = "MARQUEE",
            [144] = "HAY BALE",
            [145] = "BIN",
            [146] = "BIN",
            [147] = "RAILING",
            [148] = "RAILING",
            [149] = "START LIGHTS",
            [150] = "START LIGHTS",
            [151] = "START LIGHTS",
            [160] = "METAL SIGN",
            [164] = "CHEVRON SIGN",
            [165] = "CHEVRON SIGN",
            [168] = "SPEED SIGN",
            [172] = "CONCRETE SLAB",
            [173] = "CONCRETE RAMP",
            [174] = "CONCRETE WALL",
            [175] = "CONCRETE PILLAR",
            [176] = "CONCRETE SLAB WALL",
            [177] = "CONCRETE RAMP WALL",
            [178] = "CONCRETE SHORT WALL",
            [179] = "CONCRETE WEDGE",
        };

                private static string GetObjectName(byte index) =>
            AxoObjectNames.TryGetValue(index, out var name) ? name : $"OBJECT #{index}";

        public bool IsConnected => _client?.Connected == true && _running;

        private const int ConnectTimeoutMs = 4000;

        public void Connect(string host, int port, string adminPassword = "", ushort mciInterval = 200)
        {
            try
            {
                Disconnect();

                _client = new TcpClient();

                var connectTask = _client.ConnectAsync(host, port);
                if (!connectTask.Wait(ConnectTimeoutMs))
                {
                    Cleanup();
                    string timeoutMsg = string.Format(Localization.T("status.insim.connect_timeout"), host, port, ConnectTimeoutMs / 1000);
                    RaiseStatus(timeoutMsg, isError: true);
                    ConnectFailed?.Invoke(this, timeoutMsg);
                    return;
                }

                _stream = _client.GetStream();
                _running = true;

                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "InSim-Receive"
                };
                _receiveThread.Start();

                var isi = Packets.BuildISI(
                     udpPort: 0,
                     flags: ISFlags.ISF_MCI | ISFlags.ISF_LOCAL | ISFlags.ISF_OBH | ISFlags.ISF_CON,
                     prefix: 33,
                     interval: mciInterval,
                     admin: adminPassword,
                     iname: "-Drift Tools-"
                 );
                Send(isi);

                Send(Packets.BuildTiny(4, TinyType.TINY_SST));
                Send(Packets.BuildTiny(5, TinyType.TINY_AXI));
                Send(Packets.BuildTiny(6, TinyType.TINY_NPL));

                RaiseStatus(string.Format(Localization.T("status.insim.connected"), host, port));
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                string msg = string.Format(Localization.T("status.insim.connect_error"), ex.Message);
                RaiseStatus(msg, isError: true);
                ConnectFailed?.Invoke(this, msg);
                Cleanup();
            }
        }

        public void Disconnect()
        {
            if (_running)
            {
                _running = false;
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
                RaiseStatus(Localization.T("status.insim.disconnected"));
            }
        }

        public void Send(byte[] data)
        {
            try
            {
                _stream?.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                RaiseStatus(string.Format(Localization.T("status.insim.send_error"), ex.Message), isError: true);
            }
        }

        public void ShowButton(byte clickId, string text,
                               byte l = 35, byte t = 32, byte w = 40, byte h = 8,
                               byte bStyle = 32  )
        {
            var btn = Packets.BuildBTN(
                ucid: 0,
                clickId: clickId,
                inst: 1,
                bStyle: bStyle,
                typeIn: 0,
                l: l, t: t, w: w, h: h,
                text: text,
                reqI: 1
            );
            Send(btn);
        }

        private static bool ComputeIsRaceNow(StateFlags flags) =>
            (flags & StateFlags.ISS_GAME) != 0
            && (flags & StateFlags.ISS_FRONT_END) == 0;

        public bool IsRaceNow => ComputeIsRaceNow(_gameState);

        public event EventHandler<bool>? RaceStateChanged;

                public void DeleteAllButtons()
        {
            Send(Packets.BuildBFN_DeleteAll(0));
        }

                public void RequestState() => Send(Packets.BuildTiny(8, TinyType.TINY_SST));

                public void RequestPlayerList() => Send(Packets.BuildTiny(9, TinyType.TINY_NPL));

        private void ReceiveLoop()
        {
            byte[] tmp = new byte[4096];

            try
            {
                while (_running)
                {
                    int read = _stream.Read(tmp, 0, tmp.Length);
                    if (read == 0) break;

                    if (_bufferLen + read > _buffer.Length)
                    {
                        byte[] newBuf = new byte[_buffer.Length * 2];
                        Array.Copy(_buffer, newBuf, _bufferLen);
                        _buffer = newBuf;
                    }
                    Array.Copy(tmp, 0, _buffer, _bufferLen, read);
                    _bufferLen += read;

                    ProcessBuffer();
                }
            }
            catch (Exception ex)
            {
                if (_running)
                    RaiseStatus(string.Format(Localization.T("status.insim.receive_error"), ex.Message), isError: true);
            }
            finally
            {
                _running = false;
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ProcessBuffer()
        {
            while (_bufferLen >= 4)
            {

                int packetSize = _buffer[0] * 4;
                if (packetSize < 4) packetSize = 4;

                if (_bufferLen < packetSize) break;

                byte[] packet = new byte[packetSize];
                Array.Copy(_buffer, packet, packetSize);

                int remaining = _bufferLen - packetSize;
                Array.Copy(_buffer, packetSize, _buffer, 0, remaining);
                _bufferLen = remaining;

                HandlePacket(packet);
            }
        }

        private void HandlePacket(byte[] packet)
        {
            if (packet.Length < 4) return;

            if (packet[1] == 51)
                RawObjectHitDebug?.Invoke(this, packet);

            PacketType type = (PacketType)packet[1];

            switch (type)
            {
                case PacketType.ISP_TINY:
                    if (packet.Length >= 4 && packet[3] == (byte)TinyType.TINY_NONE)
                        Send(packet);
                    else if (packet.Length >= 4 && packet[3] == (byte)TinyType.TINY_AXC)
                        HandleLayoutCleared();
                    break;

                case PacketType.ISP_MCI:
                    HandleMCI(packet);
                    break;

                case PacketType.ISP_VER:
                    HandleVer(packet);
                    break;

                case PacketType.ISP_STA:
                    HandleSTA(packet);
                    break;

                case PacketType.ISP_LAP:
                    HandleLap(packet);
                    break;
                case PacketType.ISP_AXI:
                    HandleAxi(packet);
                    break;
                case PacketType.ISP_CRS:
                    HandleCrs(packet);
                    break;

                case PacketType.ISP_RST:
                    HandleRst(packet);
                    break;

                case PacketType.ISP_PLA:
                    HandlePla(packet);
                    break;

                case PacketType.ISP_PLP:
                    HandlePlp(packet);
                    break;

                case PacketType.ISP_UCO:
                    HandleUco(packet);
                    break;

                case PacketType.ISP_PEN:
                    HandlePen(packet);
                    break;

                case PacketType.ISP_OBH:
                    HandleObh(packet);
                    break;

                case PacketType.ISP_CON:
                    HandleCon(packet);
                    break;

                case PacketType.ISP_NPL:
                    HandleNpl(packet);
                    break;

                case PacketType.ISP_CPR:
                    HandleCpr(packet);
                    break;
            }
        }

        private void HandleObh(byte[] p)
        {

            if (p.Length < 28) return;

            byte plid = p[3];
            byte index = p[26];

            if (index < 4 || index >= 192) return;

            ObjectHit?.Invoke(this, new ObjectHitEventArgs
            {
                PLID = plid,
                ObjectIndex = index,
                ObjectName = GetObjectName(index)
            });
        }

        // IS_CON layout, corrected against two live captures (2025-xx test): Size(1) Type(1)
        // ReqI(1) SubT(1) SpClose(2,word, 0.1 m/s units) Time(2,word) + 4 more header bytes of
        // unknown purpose (offsets 8-11, not needed here) — 12 bytes total before the two
        // 16-byte CarContOBJ blocks (car A at offset 12, car B at offset 28): PLID Info Steer
        // ThrBrk CluHan GearSp Speed Direction Heading AccelF AccelR, followed by position bytes
        // we don't need. Confirmed live: local player's PLID consistently landed at offset 28
        // (Car B) in both test captures, and Car B's Speed/ThrBrk/AccelF all read as sensible
        // non-zero values matching an actual moving, accelerating car at impact.
        private static DerbyCarContact ParseCarContOBJ(byte[] p, int offset)
        {
            byte thrBrk = p[offset + 3];
            return new DerbyCarContact
            {
                PLID = p[offset + 0],
                Throttle0to15 = thrBrk & 0x0F,
                SpeedKmh = p[offset + 6] / 255.0 * 460.0,
                DirectionDeg = p[offset + 7] / 256.0 * 360.0,
                HeadingDeg = p[offset + 8] / 256.0 * 360.0,
                AccelF = unchecked((sbyte)p[offset + 9]),
                AccelR = unchecked((sbyte)p[offset + 10]),
            };
        }

        private void HandleCon(byte[] p)
        {
            if (p.Length < 44) return;

            ushort spClose = BitConverter.ToUInt16(p, 4);
            var a = ParseCarContOBJ(p, 12);
            var b = ParseCarContOBJ(p, 28);

            CarContact?.Invoke(this, new CarContactEventArgs
            {
                ClosingSpeedKmh = spClose / 10.0 * 3.6,
                A = a,
                B = b,
            });
        }

        private void HandleNpl(byte[] p)
        {

            if (p.Length < 32) return;

            byte plid = p[3];
            byte ucid = p[4];
            string pname = StripLfsColorCodes(ReadNullPaddedLatin1(p, 8, 24));
            if (string.IsNullOrWhiteSpace(pname)) return;

            _ucidToPlid[ucid] = plid;
            _playerNames[plid] = pname;
            PlayerNamed?.Invoke(this, new PlayerNameEventArgs { PLID = plid, PName = pname });
        }

        private void HandleCpr(byte[] p)
        {

            if (p.Length < 28) return;

            byte ucid = p[3];
            if (!_ucidToPlid.TryGetValue(ucid, out byte plid)) return;

            string pname = StripLfsColorCodes(ReadNullPaddedLatin1(p, 4, 24));
            if (string.IsNullOrWhiteSpace(pname)) return;

            _playerNames[plid] = pname;
            PlayerNamed?.Invoke(this, new PlayerNameEventArgs { PLID = plid, PName = pname });
        }

        private static string ReadNullPaddedLatin1(byte[] d, int offset, int length)
        {
            int len = 0;
            while (len < length && d[offset + len] != 0) len++;
            return Encoding.Latin1.GetString(d, offset, len);
        }

        private static string StripLfsColorCodes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '^' && i + 1 < s.Length) { i++; continue; }
                sb.Append(s[i]);
            }
            return sb.ToString().Trim();
        }

        private void HandleUco(byte[] p)
        {

            if (p.Length < 28) return;

            byte plid = p[3];
            byte ucoAction = p[5];

            byte flags = p[25];
            byte index = p[26];

            if (index != 252) return;

            int checkpointNumber = flags & 0x03;

            CheckpointCrossed?.Invoke(this, new CheckpointCrossedEventArgs
            {
                PLID = plid,
                CheckpointIndex = checkpointNumber,
                Forward = ucoAction == 2
            });
        }

        private void HandlePen(byte[] p)
        {

            if (p.Length < 8) return;

            byte plid = p[3];
            byte reason = p[6];

            if (reason == 2)
                RestrictedAreaEntered?.Invoke(this, new PlidEventArgs { PLID = plid });
        }

        private void HandlePlp(byte[] p)
        {

            if (p.Length < 4) return;
            PlayerPitted?.Invoke(this, new PlidEventArgs { PLID = p[3] });
        }

        private void HandleCrs(byte[] p)
        {
            if (p.Length < 4) return;
            CarReset?.Invoke(this, new PlidEventArgs { PLID = p[3] });
        }

        private void HandleRst(byte[] p)
        {

            RaceRestarted?.Invoke(this, EventArgs.Empty);
        }

        private void HandlePla(byte[] p)
        {

            if (p.Length < 8) return;

            byte plid = p[3];
            byte fact = p[4];

            if (fact == 0)
                PitLaneExited?.Invoke(this, new PlidEventArgs { PLID = plid });
            else if (fact == 1)
                PitLaneEntered?.Invoke(this, new PlidEventArgs { PLID = plid });
        }

        private void HandleSTA(byte[] p)
        {
            if (p.Length < 12) return;

            var prev = _gameState;
            _gameState = (StateFlags)BitConverter.ToUInt16(p, 6);
            ViewPLID = p[11];

            if (p.Length >= 28)
            {
                string track = Encoding.Latin1.GetString(p, p.Length - 8, 6).TrimEnd('\0');
                if (!string.IsNullOrEmpty(track) && track != CurrentTrack)
                {
                    CurrentTrack = track;
                    TrackChanged?.Invoke(this, track);
                    Send(Packets.BuildTiny(7, TinyType.TINY_AXI));
                }
            }

            bool nowRace = ComputeIsRaceNow(_gameState);
            bool wasRace = ComputeIsRaceNow(prev);

            if (nowRace != wasRace)
                RaceStateChanged?.Invoke(this, nowRace);
            if (nowRace)
                Send(Packets.BuildTiny(6, TinyType.TINY_AXI));
        }

        private void HandleMCI(byte[] packet)
        {
            if (packet.Length < 4) return;

            try
            {
                var mci = IS_MCI_Packet.Parse(packet);
                if (mci.Cars != null)
                {
                    foreach (var car in mci.Cars)
                    {
                        CarDataReceived?.Invoke(this, new CarDataEventArgs { Car = car });
                    }
                }
            }
            catch {  }
        }

        private void HandleVer(byte[] packet)
        {
            if (packet.Length >= 20)
            {
                string version = Encoding.Latin1.GetString(packet, 4, 8).TrimEnd('\0');
                string product = Encoding.Latin1.GetString(packet, 12, 6).TrimEnd('\0');
                ushort isVer = BitConverter.ToUInt16(packet, 18);
                RaiseStatus($"LFS {version} ({product}), InSim v{isVer}");
            }
        }

        private void HandleAxi(byte[] p)
        {

            if (p.Length < 40) return;

            string layout = Encoding.Latin1.GetString(p, 8, 32).TrimEnd('\0', ' ');

            if (layout != CurrentLayout)
            {
                CurrentLayout = layout;
                LayoutChanged?.Invoke(this, layout);
                RaiseStatus(string.IsNullOrEmpty(layout)
                    ? Localization.T("status.insim.axi_no_layout")
                    : string.Format(Localization.T("status.insim.axi_layout_detected"), layout));
            }
            Send(Packets.BuildTiny(7, TinyType.TINY_AXI));
        }

        private void HandleLayoutCleared()
        {
            if (CurrentLayout != "")
            {
                CurrentLayout = "";
                LayoutChanged?.Invoke(this, "");
            }
        }

        private void HandleLap(byte[] p)
        {
            if (p.Length < 20) return;

            LapCompleted?.Invoke(this, new LapCompletedEventArgs
            {
                PLID = p[3],
                LapTimeMs = BitConverter.ToUInt32(p, 4),
                LapsDone = BitConverter.ToUInt16(p, 12)
            });
        }

        private void Cleanup()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        private void RaiseStatus(string message, bool isError = false)
        {
            StatusChanged?.Invoke(this, new StatusEventArgs { Message = message, IsError = isError });
        }

        public void Dispose() => Disconnect();
    }
}
