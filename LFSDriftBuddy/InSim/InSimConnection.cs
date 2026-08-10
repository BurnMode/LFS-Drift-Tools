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

    

    public class CheckpointCrossedEventArgs : PlidEventArgs
    {
        /// <summary>0 = meta, 1 = pierwszy punkt kontrolny, 2 = drugi, 3 = trzeci (wg edytora layoutu LFS)</summary>
        public int CheckpointIndex { get; set; }
        public bool Forward { get; set; }
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
        public event EventHandler<LapCompletedEventArgs> LapCompleted;
        public event EventHandler<string> TrackChanged;
        public event EventHandler<string> LayoutChanged;   // ← NOWE
        public event EventHandler<PlidEventArgs> CarReset;        // ← NOWE: IS_CRS
        public event EventHandler RaceRestarted;   // ← NOWE: IS_RST
        public event EventHandler<PlidEventArgs> PitLaneEntered;  // ← NOWE: IS_PLA, Fact=1
        public event EventHandler<PlidEventArgs> PitLaneExited;   // ← NOWE: IS_PLA, Fact=0
        public event EventHandler<PlidEventArgs> PlayerPitted;
        public event EventHandler<CheckpointCrossedEventArgs> CheckpointCrossed;   // ← NOWE: IS_UCO
        public event EventHandler<PlidEventArgs> RestrictedAreaEntered;            // ← NOWE: IS_PEN (zła trasa / zakazany obszar)
        public event EventHandler<PlidEventArgs> PostHit;
        public event EventHandler<PlidEventArgs> TyreStackHit;
        public event EventHandler<byte[]> RawObjectHitDebug;
        // ── State ─────────────────────────────────────────────
        private TcpClient   _client;
        private NetworkStream _stream;
        private Thread      _receiveThread;
        private byte[]      _buffer     = new byte[8192];
        private int         _bufferLen  = 0;
        private volatile bool _running  = false;
        private StateFlags _gameState = 0;
        public StateFlags GameState => _gameState;
        public byte ViewPLID { get; private set; } = 0;
        public string CurrentTrack { get; private set; } = "";
        public string CurrentLayout { get; private set; } = "";   // ← NOWE, "" = brak customowego layoutu (.lyt)

        private const byte AXO_POST = 136;
        private const byte AXO_TYRE_STACK2_BIG = 53;   // ← NOWE
        private const byte AXO_TYRE_STACK3_BIG = 54;   // ← NOWE
        private const byte AXO_TYRE_STACK4_BIG = 55;   // ← NOWE




        public bool IsConnected => _client?.Connected == true && _running;

        // ─────────────────────────────────────────────────────
        //  Connect
        // ─────────────────────────────────────────────────────
        public void Connect(string host, int port, string adminPassword = "", ushort mciInterval = 200)
        {
            try
            {
                Disconnect();

                _client = new TcpClient();
                _client.Connect(host, port);
                _stream = _client.GetStream();

                _running = true;

                // Start receive thread
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "InSim-Receive"
                };
                _receiveThread.Start();

                // Send IS_ISI – request MCI packets every mciInterval ms
                var isi = Packets.BuildISI(
                     udpPort: 0,
                     flags: ISFlags.ISF_MCI | ISFlags.ISF_LOCAL | ISFlags.ISF_OBH,   // ← tylko OBH, bez AXM
                     prefix: 33,               // '!'
                     interval: mciInterval,
                     admin: adminPassword,
                     iname: "-Drift Tools-"
                 );
                Send(isi);

                Send(Packets.BuildTiny(4, TinyType.TINY_SST));
                Send(Packets.BuildTiny(5, TinyType.TINY_AXI));

                RaiseStatus("Połączono z LFS na " + host + ":" + port);
                Connected?.Invoke(this, EventArgs.Empty);
                Send(Packets.BuildTiny(4, TinyType.TINY_SST));
                Send(Packets.BuildTiny(5, TinyType.TINY_AXI));
            }
            catch (Exception ex)
            {
                RaiseStatus("Błąd połączenia: " + ex.Message, isError: true);
                Cleanup();
            }
        }

        // ─────────────────────────────────────────────────────
        //  Disconnect
        // ─────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────
        //  Send raw bytes
        // ─────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────
        //  Show a button inside LFS
        // ─────────────────────────────────────────────────────
        // Show a button inside LFS
        // Coordinates are 0-200 (not percentages).
        // Recommended area: L 0-110, T 30-170
        // ISB_DARK = 32, ISB_LIGHT = 16
        public void ShowButton(byte clickId, string text,
                               byte l = 35, byte t = 32, byte w = 40, byte h = 8,
                               byte bStyle = 32 /* ISB_DARK */ )
        {
            var btn = Packets.BuildBTN(
                ucid:    0,
                clickId: clickId,
                inst:    1,
                bStyle:  bStyle,
                typeIn:  0,
                l: l, t: t, w: w, h: h,
                text:    text,
                reqI:    1          // non-zero is required!
            );
            Send(btn);
        }

        public bool IsRaceNow =>
        (_gameState & StateFlags.ISS_GAME) != 0        // musi być ustawiony bit "w grze"
        && (_gameState & StateFlags.ISS_REPLAY) == 0   // ale nie w powtórce (SPR)
        && (_gameState & StateFlags.ISS_FRONT_END) == 0; // i nie w menu głównym

        public event EventHandler<bool>? RaceStateChanged; // true = wszedł do wyścigu

        /// <summary>Delete all InSim buttons for local connection (UCID=0).</summary>
        public void DeleteAllButtons()
        {
            Send(Packets.BuildBFN_DeleteAll(0));
        }

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

                    // Append to buffer
                    if (_bufferLen + read > _buffer.Length)
                    {
                        // Grow buffer
                        byte[] newBuf = new byte[_buffer.Length * 2];
                        Array.Copy(_buffer, newBuf, _bufferLen);
                        _buffer = newBuf;
                    }
                    Array.Copy(tmp, 0, _buffer, _bufferLen, read);
                    _bufferLen += read;

                    // Process complete packets
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
                // CRITICAL: LFS Size byte = actual_bytes / 4
                // So actual packet size = Size * 4
                int packetSize = _buffer[0] * 4;
                if (packetSize < 4) packetSize = 4;   // minimum safety

                if (_bufferLen < packetSize) break;    // Incomplete packet yet

                // Copy complete packet
                byte[] packet = new byte[packetSize];
                Array.Copy(_buffer, packet, packetSize);

                // Shift buffer
                int remaining = _bufferLen - packetSize;
                Array.Copy(_buffer, packetSize, _buffer, 0, remaining);
                _bufferLen = remaining;

                // Dispatch
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
                // ── Keep-alive ────────────────────────────────
                case PacketType.ISP_TINY:
                    if (packet.Length >= 4 && packet[3] == (byte)TinyType.TINY_NONE)
                        Send(packet);  // Echo back
                    else if (packet.Length >= 4 && packet[3] == (byte)TinyType.TINY_AXC)
                        HandleLayoutCleared();  // wszystkie obiekty usunięte = brak layoutu
                    break;

                // ── Multi Car Info ────────────────────────────
                case PacketType.ISP_MCI:
                    HandleMCI(packet);
                    break;

                // ── Version reply ─────────────────────────────
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

                

                case PacketType.ISP_OBH:            // ← NOWE
                    HandleObh(packet);
                    break;
            }
        }




        private void HandleObh(byte[] p)
        {
            // IS_OBH: w aktualnej wersji LFS pakiet ma 28 bajtów (potwierdzone empirycznie zrzutami hex),
            // Index leży na offsecie 26, OBHFlags na offsecie 27.
            if (p.Length < 28) return;

            byte plid = p[3];
            byte index = p[26];
            byte obhFlags = p[27];

            if (index == AXO_POST)
            {
                PostHit?.Invoke(this, new PlidEventArgs { PLID = plid });
                return;
            }

            if (index == AXO_TYRE_STACK2_BIG || index == AXO_TYRE_STACK3_BIG || index == AXO_TYRE_STACK4_BIG)
            {
                TyreStackHit?.Invoke(this, new PlidEventArgs { PLID = plid });
                return;
            }
        }

        private void HandleUco(byte[] p)
        {
            // IS_UCO: Size=28 — zgłasza przejazd przez punkt kontrolny InSim lub wjazd/wyjazd z okręgu InSim
            // ObjectInfo (Index/Flags identyfikujące obiekt) leży na offsecie 20: X(short) Y(short) Zbyte Flags Index Heading
            if (p.Length < 28) return;

            byte plid = p[3];
            byte ucoAction = p[5];   // UCO_CIRCLE_ENTER=0 / UCO_CIRCLE_LEAVE=1 / UCO_CP_FWD=2 / UCO_CP_REV=3

            byte flags = p[25];
            byte index = p[26];

            if (index != 252) return;   // 252 = punkt kontrolny InSim (253 to okrąg InSim — na razie nieobsługiwany)

            int checkpointNumber = flags & 0x03;   // 00=meta / 01=1.punkt / 10=2.punkt / 11=3.punkt

            CheckpointCrossed?.Invoke(this, new CheckpointCrossedEventArgs
            {
                PLID = plid,
                CheckpointIndex = checkpointNumber,
                Forward = ucoAction == 2   // UCO_CP_FWD
            });
        }

        private void HandlePen(byte[] p)
        {
            // IS_PEN: Size=8 — kara nadana/zdjęta. LFS wysyła Reason=PENR_WRONG_WAY (2)
            // zarówno dla złej trasy, jak i wjazdu na zakazany obszar (Restricted area) w layoucie.
            if (p.Length < 8) return;

            byte plid = p[3];
            byte reason = p[6];

            if (reason == 2)   // PENR_WRONG_WAY
                RestrictedAreaEntered?.Invoke(this, new PlidEventArgs { PLID = plid });
        }

        private void HandlePlp(byte[] p)
        {
            // IS_PLP: Size=4, Type=ISP_PLP, ReqI, PLID
            // Wysyłane gdy gracz wchodzi na ekran ustawień/garażu (Shift+P lub menu "do boksów")
            if (p.Length < 4) return;
            PlayerPitted?.Invoke(this, new PlidEventArgs { PLID = p[3] });
        }
        private void HandleCrs(byte[] p)
        {
            // IS_CRS: Size=4, Type, ReqI, PLID
            if (p.Length < 4) return;
            CarReset?.Invoke(this, new PlidEventArgs { PLID = p[3] });
        }

        private void HandleRst(byte[] p)
        {
            // IS_RST: wysyłany na starcie każdego wyścigu/kwalifikacji — bez PLID, dotyczy całej sesji
            RaceRestarted?.Invoke(this, EventArgs.Empty);
        }

        private void HandlePla(byte[] p)
        {
            // IS_PLA: Size=8, Type, ReqI, PLID, Fact, Sp1, Sp2, Sp3
            // Fact: 0 = PITLANE_EXIT (wyjazd na tor) / 1 = PITLANE_ENTER (wjazd do alei)
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

            // ── Track[6] + Weather + Wind — zawsze ostatnie 8 bajtów pakietu ──
            if (p.Length >= 28)
            {
                string track = Encoding.Latin1.GetString(p, p.Length - 8, 6).TrimEnd('\0');
                if (!string.IsNullOrEmpty(track) && track != CurrentTrack)
                {
                    CurrentTrack = track;
                    TrackChanged?.Invoke(this, track);
                    Send(Packets.BuildTiny(7, TinyType.TINY_AXI));   // ← NOWE: świeże info o layoucie po zmianie trasy
                }
            }

            bool nowRace = IsRaceNow;
            bool wasRace = (prev & StateFlags.ISS_GAME) != 0;

            if (nowRace != wasRace)
                RaceStateChanged?.Invoke(this, nowRace);
            if (nowRace)
                Send(Packets.BuildTiny(6, TinyType.TINY_AXI));   // ← NOWE: layout może dociągnąć się dopiero na starcie
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
            catch { /* Malformed packet – ignore */ }
        }

        private void HandleVer(byte[] packet)
        {
            // IS_VER: Size=20, Type=2, ReqI, Zero, Version[8], Product[6], InSimVer(word)
            if (packet.Length >= 20)
            {
                string version = Encoding.Latin1.GetString(packet, 4, 8).TrimEnd('\0');
                string product = Encoding.Latin1.GetString(packet, 12, 6).TrimEnd('\0');
                ushort isVer   = BitConverter.ToUInt16(packet, 18);
                RaiseStatus($"LFS {version} ({product}), InSim v{isVer}");
            }
        }

        private void HandleAxi(byte[] p)
        {
            // IS_AXI: Size=40, Type=ISP_AXI, ReqI, Zero, AXStart, NumCP, NumO(word), LName[32] @ offset 8
            if (p.Length < 40) return;

            string layout = Encoding.Latin1.GetString(p, 8, 32).TrimEnd('\0', ' ');

            if (layout != CurrentLayout)
            {
                CurrentLayout = layout;
                LayoutChanged?.Invoke(this, layout);
            }
            Send(Packets.BuildTiny(7, TinyType.TINY_AXI));
            // ── diagnostyka: pokaż w statusie co realnie przyszło z InSim ──
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
            // IS_LAP: Size=20, Type=ISP_LAP
            // Size,Type,ReqI,PLID, LTime(uint), ETime(uint), LapsDone(word), Flags(word), Penalty, NumStops, Sp2, Sp3
            if (p.Length < 20) return;

            LapCompleted?.Invoke(this, new LapCompletedEventArgs
            {
                PLID = p[3],
                LapTimeMs = BitConverter.ToUInt32(p, 4),
                LapsDone = BitConverter.ToUInt16(p, 12)
            });
        }
        // ─────────────────────────────────────────────────────
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
