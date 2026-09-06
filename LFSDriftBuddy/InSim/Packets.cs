using System;
using System.Text;

namespace LFSDriftBuddy.InSim
{

    public static class Packets
    {

        public static byte[] BuildISI(
            ushort udpPort   = 0,
            ISFlags flags    = ISFlags.ISF_MCI,
            byte   prefix    = 33,
            ushort interval  = 200,
            string admin     = "",
            string iname     = "DriftTools")
        {
            byte[] packet = new byte[44];
            int i = 0;

            packet[i++] = 11;
            packet[i++] = (byte)PacketType.ISP_ISI;
            packet[i++] = 1;
            packet[i++] = 0;

            packet[i++] = (byte)(udpPort & 0xFF);
            packet[i++] = (byte)(udpPort >> 8);

            ushort f = (ushort)flags;
            packet[i++] = (byte)(f & 0xFF);
            packet[i++] = (byte)(f >> 8);

            packet[i++] = 10;
            packet[i++] = prefix;

            packet[i++] = (byte)(interval & 0xFF);
            packet[i++] = (byte)(interval >> 8);

            byte[] adminBytes = PadString(admin, 16);
            Array.Copy(adminBytes, 0, packet, i, 16); i += 16;

            byte[] inameBytes = PadString(iname, 16);
            Array.Copy(inameBytes, 0, packet, i, 16);

            return packet;
        }

        public static byte[] BuildTiny(byte reqI, TinyType subT)
        {
            return new byte[]
            {
                1,
                (byte)PacketType.ISP_TINY,
                reqI,
                (byte)subT
            };
        }

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

            byte[] textBytes = Encoding.Latin1.GetBytes(text + "\0");
            int textLen = (textBytes.Length + 3) & ~3;
            if (textLen < 4)   textLen = 4;
            if (textLen > 240) textLen = 240;

            byte[] paddedText = new byte[textLen];
            Array.Copy(textBytes, paddedText, Math.Min(textBytes.Length, textLen));

            int actualSize = 12 + textLen;
            byte[] packet  = new byte[actualSize];
            int i = 0;

            packet[i++] = (byte)(actualSize / 4);
            packet[i++] = (byte)PacketType.ISP_BTN;
            packet[i++] = reqI;
            packet[i++] = ucid;

            packet[i++] = clickId;
            packet[i++] = inst;
            packet[i++] = bStyle;
            packet[i++] = typeIn;

            packet[i++] = l;
            packet[i++] = t;
            packet[i++] = w;
            packet[i++] = h;

            Array.Copy(paddedText, 0, packet, i, textLen);

            return packet;
        }

        public static byte[] BuildBFN_DeleteAll(byte ucid)
        {
            return new byte[]
            {
                2,
                43,
                0,
                3,
                ucid,
                0,
                0,
                0
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

    public class CompCar
    {
        public ushort Node;
        public ushort Lap;
        public byte   PLID;
        public byte   Position;
        public byte   Info;
        public byte   Sp3;
        public int    X;
        public int    Y;
        public int    Z;
        public ushort Speed;
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

            p.NumC = data[3];
            p.Cars = new CompCar[p.NumC];
            for (int i = 0; i < p.NumC; i++)
                p.Cars[i] = CompCar.Parse(data, 4 + i * 28);
            return p;
        }
    }
}
