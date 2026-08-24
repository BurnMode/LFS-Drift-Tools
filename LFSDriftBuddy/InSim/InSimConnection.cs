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
        /// <summary>LFS nickname (IS_NPL.PName), color codes stripped.</summary>
        public string PName { get; set; }
    }

    public class CheckpointCrossedEventArgs : PlidEventArgs
    {
        /// <summary>0 = finish, 1-3 = checkpoint number (per LFS layout editor).</summary>
        public int CheckpointIndex { get; set; }
        public bool Forward { get; set; }
    }

    /// <summary>Hit on any autocross object (IS_OBH). Object name is resolved from the
    /// official AXO_ index list (lfs.net/programmer/lyt).</summary>
    public class ObjectHitEventArgs : PlidEventArgs
    {
        /// <summary>Raw IS_OBH.Index, 4-191 per the LFS spec.</summary>
        public byte ObjectIndex { get; set; }

        /// <summary>Readable object name ("POST", "TYRE STACK"...), "OBJECT #N" fallback
        /// for unrecognised/reserved indices.</summary>
        public string ObjectName { get; set; }
    }

    /// <summary>
    /// Manages the TCP connection to LFS InSim, sends/receives packets.
    /// </summary>
    public class InSimConnection : IDisposable
    {
        // ── Public events ─────────────────────────────────────
        public event EventHandler<CarDataEventArgs> CarDataReceived;
        public event EventHandler<StatusEventArgs> StatusChanged;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        // Fires only when Connect() itself fails, unlike Disconnected (which signals losing an
        // already-established connection) — lets the UI safely revert its connect toggle without
        // confusing a failed attempt with a mid-session disconnect.
        public event EventHandler<string>? ConnectFailed;
        public event EventHandler<LapCompletedEventArgs> LapCompleted;
        public event EventHandler<string> TrackChanged;
        public event EventHandler<string> LayoutChanged;
        public event EventHandler<PlidEventArgs> CarReset;         // IS_CRS
        public event EventHandler RaceRestarted;                   // IS_RST
        public event EventHandler<PlidEventArgs> PitLaneEntered;   // IS_PLA, Fact=1
        public event EventHandler<PlidEventArgs> PitLaneExited;    // IS_PLA, Fact=0
        public event EventHandler<PlidEventArgs> PlayerPitted;
        public event EventHandler<CheckpointCrossedEventArgs> CheckpointCrossed;   // IS_UCO
        public event EventHandler<PlidEventArgs> RestrictedAreaEntered;            // IS_PEN (wrong way / restricted area)
        public event EventHandler<ObjectHitEventArgs> ObjectHit;
        public event EventHandler<byte[]> RawObjectHitDebug;
        public event EventHandler<PlayerNameEventArgs> PlayerNamed;   // IS_NPL

        // ── State ─────────────────────────────────────────────
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

        // PLID -> LFS nickname, filled from IS_NPL as players are seen and kept current via
        // IS_CPR (fired whenever a player renames themselves from the F12 connections screen).
        private readonly Dictionary<byte, string> _playerNames = new();
        private readonly Dictionary<byte, byte> _ucidToPlid = new();
        public string GetPlayerName(byte plid) => _playerNames.TryGetValue(plid, out var n) ? n : null;
        public string CurrentLayout { get; private set; } = "";   // "" = no custom layout (.lyt)

        // Autocross object names (AXO_*) — from https://www.lfs.net/programmer/lyt (LYT 0.8A).
        // Valid physical objects are Index 4-191 (outside that range = control objects: start/
        // checkpoints/marshal, handled elsewhere, not via IS_OBH). Entries without an official
        // name are omitted on purpose — GetObjectName() falls back to "OBJECT #N" for those.
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

        /// <summary>Readable name for an autocross object index (IS_OBH.Index).</summary>
        private static string GetObjectName(byte index) =>
            AxoObjectNames.TryGetValue(index, out var name) ? name : $"OBJECT #{index}";

        public bool IsConnected => _client?.Connected == true && _running;

        // ─────────────────────────────────────────────────────
        //  Connect
        // ─────────────────────────────────────────────────────
        // Without a timeout, TcpClient.Connect() on an unreachable host (bad IP/firewall, not a
        // simply-closed port, which fails almost instantly) can block for ~20s (Windows default),
        // too long for the UI to quickly revert the connect toggle.
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
                    string timeoutMsg = $"Nie udało się połączyć z {host}:{port} — przekroczono limit czasu ({ConnectTimeoutMs / 1000}s).";
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

                // Request MCI packets every mciInterval ms.
                var isi = Packets.BuildISI(
                     udpPort: 0,
                     flags: ISFlags.ISF_MCI | ISFlags.ISF_LOCAL | ISFlags.ISF_OBH | ISFlags.ISF_CON,   // ISF_CON needed for IS_CPR (driver rename)
                     prefix: 33,
                     interval: mciInterval,
                     admin: adminPassword,
                     iname: "-Drift Tools-"
                 );
                Send(isi);

                Send(Packets.BuildTiny(4, TinyType.TINY_SST));
                Send(Packets.BuildTiny(5, TinyType.TINY_AXI));
                Send(Packets.BuildTiny(6, TinyType.TINY_NPL));   // request IS_NPL for all current players

                RaiseStatus("Połączono z LFS na " + host + ":" + port);
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                string msg = "Błąd połączenia: " + ex.Message;
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
                RaiseStatus("Rozłączono.");
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
                RaiseStatus("Błąd wysyłania: " + ex.Message, isError: true);
            }
        }

        // Coordinates are 0-200 (not percentages). Recommended area: L 0-110, T 30-170.
        public void ShowButton(byte clickId, string text,
                               byte l = 35, byte t = 32, byte w = 40, byte h = 8,
                               byte bStyle = 32 /* ISB_DARK */ )
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

        // HUD logic runs during both live driving and replay (SPR) — ISS_GAME stays set the
        // whole time including replay, so only the front-end/main-menu screen excludes it.
        private static bool ComputeIsRaceNow(StateFlags flags) =>
            (flags & StateFlags.ISS_GAME) != 0
            && (flags & StateFlags.ISS_FRONT_END) == 0;

        public bool IsRaceNow => ComputeIsRaceNow(_gameState);

        public event EventHandler<bool>? RaceStateChanged; // true = entered a race

        /// <summary>Delete all InSim buttons for local connection (UCID=0).</summary>
        public void DeleteAllButtons()
        {
            Send(Packets.BuildBFN_DeleteAll(0));
        }

        /// <summary>Requests a fresh IS_STA (refreshes ViewPLID) — LFS doesn't push it on its
        /// own when the spectated car changes (Tab), only on request or major state changes.</summary>
        public void RequestState() => Send(Packets.BuildTiny(8, TinyType.TINY_SST));

        /// <summary>Requests a fresh IS_NPL burst for all current players — was only sent once
        /// at Connect(), so a player who joined afterwards (or whichever car got Tab-switched to)
        /// could stay unresolved in GetPlayerName() forever otherwise.</summary>
        public void RequestPlayerList() => Send(Packets.BuildTiny(9, TinyType.TINY_NPL));

        // ─────────────────────────────────────────────────────
        //  Receive loop
        // ─────────────────────────────────────────────────────
        private void ReceiveLoop()
        {
            byte[] tmp = new byte[4096];

            try
            {
                while (_running)
                {
                    int read = _stream.Read(tmp, 0, tmp.Length);
                    if (read == 0) break;  // connection closed

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
                    RaiseStatus("Błąd odbioru: " + ex.Message, isError: true);
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
                // LFS Size byte = actual_bytes / 4.
                int packetSize = _buffer[0] * 4;
                if (packetSize < 4) packetSize = 4;

                if (_bufferLen < packetSize) break;   // incomplete packet

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
                        Send(packet);  // echo back — keep-alive
                    else if (packet.Length >= 4 && packet[3] == (byte)TinyType.TINY_AXC)
                        HandleLayoutCleared();  // all objects removed = no layout
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
            // IS_OBH is 28 bytes in current LFS versions; Index at offset 26.
            if (p.Length < 28) return;

            byte plid = p[3];
            byte index = p[26];

            // 4-191 is the only valid physical-object range (lfs.net/programmer/lyt, NOTE5) —
            // below/above that are control objects handled by IS_UCO/IS_PEN instead.
            if (index < 4 || index >= 192) return;

            ObjectHit?.Invoke(this, new ObjectHitEventArgs
            {
                PLID = plid,
                ObjectIndex = index,
                ObjectName = GetObjectName(index)
            });
        }

        private void HandleNpl(byte[] p)
        {
            // IS_NPL: PLID at offset 3, UCID at offset 4, PName[24] at offset 8 — stable across
            // InSim versions; fields after PName (plate/car/etc.) have shifted between versions
            // and aren't needed here, so they're intentionally not parsed.
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
            // IS_CPR: player renamed themselves (F12 connections screen). UCID at offset 3,
            // PName[24] at offset 4 — only useful once we've seen that UCID's PLID via IS_NPL.
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

        // Strips LFS's "^" + one-char color/format codes (e.g. "^3Name^7") from a display name.
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
            // IS_UCO: InSim checkpoint/circle crossing. Index at offset 26.
            if (p.Length < 28) return;

            byte plid = p[3];
            byte ucoAction = p[5];   // 0=circle enter / 1=circle leave / 2=cp fwd / 3=cp rev

            byte flags = p[25];
            byte index = p[26];

            if (index != 252) return;   // 252 = InSim checkpoint (253 = circle, unsupported)

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
            // IS_PEN: penalty given/cleared. LFS sends Reason=PENR_WRONG_WAY (2) for both
            // wrong-way driving and entering a restricted area.
            if (p.Length < 8) return;

            byte plid = p[3];
            byte reason = p[6];

            if (reason == 2)
                RestrictedAreaEntered?.Invoke(this, new PlidEventArgs { PLID = plid });
        }

        private void HandlePlp(byte[] p)
        {
            // IS_PLP: player entered the setup/garage screen.
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
            // IS_RST: sent at the start of every race/qualifying, no PLID.
            RaceRestarted?.Invoke(this, EventArgs.Empty);
        }

        private void HandlePla(byte[] p)
        {
            // Fact: 0 = pit exit, 1 = pit entry.
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

            // Track[6] + Weather + Wind are always the last 8 bytes.
            if (p.Length >= 28)
            {
                string track = Encoding.Latin1.GetString(p, p.Length - 8, 6).TrimEnd('\0');
                if (!string.IsNullOrEmpty(track) && track != CurrentTrack)
                {
                    CurrentTrack = track;
                    TrackChanged?.Invoke(this, track);
                    Send(Packets.BuildTiny(7, TinyType.TINY_AXI));   // refresh layout info after a track change
                }
            }

            bool nowRace = ComputeIsRaceNow(_gameState);
            bool wasRace = ComputeIsRaceNow(prev);

            if (nowRace != wasRace)
                RaceStateChanged?.Invoke(this, nowRace);
            if (nowRace)
                Send(Packets.BuildTiny(6, TinyType.TINY_AXI));   // layout may only load once the race starts
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
            catch { /* malformed packet — ignore */ }
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
            // IS_AXI: LName[32] at offset 8.
            if (p.Length < 40) return;

            string layout = Encoding.Latin1.GetString(p, 8, 32).TrimEnd('\0', ' ');

            if (layout != CurrentLayout)
            {
                CurrentLayout = layout;
                LayoutChanged?.Invoke(this, layout);
            }
            Send(Packets.BuildTiny(7, TinyType.TINY_AXI));
            RaiseStatus(string.IsNullOrEmpty(layout)
                ? "InSim: brak nazwy layoutu w IS_AXI (pusty LName — layout mógł być wgrany przez hosta, nie lokalnie)"
                : $"InSim: wykryto layout '{layout}'");
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