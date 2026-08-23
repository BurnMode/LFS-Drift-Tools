using System;
using System.Text;

namespace LFSDriftBuddy.InSim
{
    // Packet builders — hand-crafted byte arrays. Size byte = actual_byte_count / 4 in every packet.

    public static class Packets
    {
        // IS_ISI – Initialise InSim, 44 bytes.
        public static byte[] BuildISI(
            ushort udpPort   = 0,
            ISFlags flags    = ISFlags.ISF_MCI,
            byte   prefix    = 33,   // '!'
            ushort interval  = 200,  // ms between MCI packets
            string admin     = "",
            string iname     = "DriftTools")
        {
            byte[] packet = new byte[44];
            int i = 0;

            packet[i++] = 11;                               // Size = 44 / 4
            packet[i++] = (byte)PacketType.ISP_ISI;         // Type = 1
            packet[i++] = 1;                                 // ReqI (non-zero → VER reply)
            packet[i++] = 0;                                 // Zero

            // UDPPort (word LE)
            packet[i++] = (byte)(udpPort & 0xFF);
            packet[i++] = (byte)(udpPort >> 8);

            // Flags (word LE)
            ushort f = (ushort)flags;
            packet[i++] = (byte)(f & 0xFF);
            packet[i++] = (byte)(f >> 8);

            packet[i++] = 10;                               // InSimVer = 10 (current version)
            packet[i++] = prefix;                            // Prefix char

            // Interval (word LE)
            packet[i++] = (byte)(interval & 0xFF);
            packet[i++] = (byte)(interval >> 8);

            // Admin[16]
            byte[] adminBytes = PadString(admin, 16);
            Array.Copy(adminBytes, 0, packet, i, 16); i += 16;

            // IName[16]
            byte[] inameBytes = PadString(iname, 16);
            Array.Copy(inameBytes, 0, packet, i, 16);

            return packet;
        }

        // IS_TINY – keep-alive / generic 4-byte.
        public static byte[] BuildTiny(byte reqI, TinyType subT)
        {
            return new byte[]
            {
                1,                              // Size = 4 / 4
                (byte)PacketType.ISP_TINY,
                reqI,
                (byte)subT
            };
        }

        // IS_BTN – display button in LFS. ReqI must be non-zero. Text is null-terminated,
        // padded to a multiple of 4. L/T/W/H use the 0-200 coordinate space, not percentages.
        public static byte[] BuildBTN(
            byte   ucid,
            byte   clickId,
            byte   inst,
            byte   bStyle,
            byte   typeIn,
            byte   l, byte t, byte w, byte h,
            string text,
            byte   reqI = 1)
        {
            // Text must be null-terminated and padded to multiple of 4 (minimum 4 bytes)
            byte[] textBytes = Encoding.Latin1.GetBytes(text + "\0");
            int textLen = (textBytes.Length + 3) & ~3;  // round up to multiple of 4
            if (textLen < 4)   textLen = 4;
            if (textLen > 240) textLen = 240;

            byte[] paddedText = new byte[textLen];
            Array.Copy(textBytes, paddedText, Math.Min(textBytes.Length, textLen));

            int actualSize = 12 + textLen;
            byte[] packet  = new byte[actualSize];
            int i = 0;

            packet[i++] = (byte)(actualSize / 4);       // Size = actual_bytes / 4
            packet[i++] = (byte)PacketType.ISP_BTN;     // Type = 45
            packet[i++] = reqI;                          // ReqI – must NOT be 0
            packet[i++] = ucid;                          // UCID (0 = local, 255 = all)

            packet[i++] = clickId;                       // ClickID (0–239)
            packet[i++] = inst;                          // Inst flags
            packet[i++] = bStyle;                        // BStyle flags
            packet[i++] = typeIn;                        // TypeIn

            packet[i++] = l;                             // L: left  (0–200)
            packet[i++] = t;                             // T: top   (0–200)
            packet[i++] = w;                             // W: width (0–200)
            packet[i++] = h;                             // H: height (0–200)

            Array.Copy(paddedText, 0, packet, i, textLen);

            return packet;
        }

        // IS_BFN – delete buttons (SubT 3 = BFN_CLEAR, all buttons for this UCID).
        public static byte[] BuildBFN_DeleteAll(byte ucid)
        {
            return new byte[]
            {
                2,       // Size = 8 / 4
                43,      // ISP_BFN = 43
                0,       // ReqI
                3,       // SubT = BFN_CLEAR (clears all buttons for this UCID)
                ucid,    // UCID
                0,       // ClickID (unused for CLEAR)
                0,       // Inst
                0        // sp3
            };
        }

        private static byte[] PadString(string s, int length)
        {
            byte[] result = new byte[length];
            if (!string.IsNullOrEmpty(s))
            {
                byte[] b = Encoding.Latin1.GetBytes(s);
                Array.Copy(b, result, Math.Min(b.Length, length - 1));
            }
            return result;
        }
    }

    // IS_MCI – Multi Car Info. Header = 4 bytes, each CompCar = 28 bytes.
    public class CompCar
    {
        public ushort Node;
        public ushort Lap;
        public byte   PLID;
        public byte   Position;
        public byte   Info;
        public byte   Sp3;
        public int    X;        // world position — InSim.txt: 65536 = 1 metre
        public int    Y;
        public int    Z;
        public ushort Speed;    // 32768 = 100 m/s
        public ushort Direction;
        public ushort Heading;
        public short  AngVel;

        public double SpeedKmh => (Speed / 32768.0) * 100.0 * 3.6;
        public double SpeedMs  => (Speed / 32768.0) * 100.0;

        public static CompCar Parse(byte[] data, int offset)
        {
            var c = new CompCar();
            c.Node      = BitConverter.ToUInt16(data, offset);
            c.Lap       = BitConverter.ToUInt16(data, offset + 2);
            c.PLID      = data[offset + 4];
            c.Position  = data[offset + 5];
            c.Info      = data[offset + 6];
            c.Sp3       = data[offset + 7];
            c.X         = BitConverter.ToInt32(data, offset + 8);
            c.Y         = BitConverter.ToInt32(data, offset + 12);
            c.Z         = BitConverter.ToInt32(data, offset + 16);
            c.Speed     = BitConverter.ToUInt16(data, offset + 20);
            c.Direction = BitConverter.ToUInt16(data, offset + 22);
            c.Heading   = BitConverter.ToUInt16(data, offset + 24);
            c.AngVel    = BitConverter.ToInt16(data, offset + 26);
            return c;
        }
    }

    public class IS_MCI_Packet
    {
        public byte NumC;
        public CompCar[]? Cars;

        public static IS_MCI_Packet Parse(byte[] data)
        {
            var p = new IS_MCI_Packet();
            // data[0] = Size (in /4 units), data[3] = NumC
            p.NumC = data[3];
            p.Cars = new CompCar[p.NumC];
            for (int i = 0; i < p.NumC; i++)
                p.Cars[i] = CompCar.Parse(data, 4 + i * 28);
            return p;
        }
    }
}
