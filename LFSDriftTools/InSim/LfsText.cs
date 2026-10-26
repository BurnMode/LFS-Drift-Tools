using System.Collections.Generic;
using System.Text;

namespace LFSDriftTools.InSim
{
    public static class LfsText
    {
        private static readonly (char Code, int CodePage)[] Pages =
        {
            ('L', 1252), ('E', 1250), ('T', 1254), ('B', 1257), ('C', 1251), ('G', 1253),
        };

        private static readonly Encoding?[] Encodings = CreateEncodings();
        private static readonly Dictionary<char, short[]> Cache = new();
        private static readonly object CacheLock = new();

        public static byte[] Encode(string text)
        {
            var bytes = new List<byte>(text.Length + 8);
            int page = 0;
            foreach (char c in text)
            {
                if (c < 128)
                {
                    bytes.Add((byte)c);
                    continue;
                }

                short[] map = GetMap(c);
                int found = map[page] >= 0 ? page : System.Array.FindIndex(map, v => v >= 0);
                if (found < 0)
                {
                    bytes.Add((byte)'?');
                    continue;
                }

                if (found != page)
                {
                    bytes.Add((byte)'^');
                    bytes.Add((byte)Pages[found].Code);
                    page = found;
                }
                bytes.Add((byte)map[found]);
            }
            return bytes.ToArray();
        }

        private static short[] GetMap(char c)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(c, out var cached)) return cached;

                var map = new short[Pages.Length];
                var chars = new[] { c };
                for (int i = 0; i < Pages.Length; i++)
                {
                    map[i] = -1;
                    var encoding = Encodings[i];
                    if (encoding == null) continue;
                    byte[] encoded = encoding.GetBytes(chars);
                    if (encoded.Length == 1 && encoded[0] >= 128 && encoding.GetString(encoded) == c.ToString())
                        map[i] = encoded[0];
                }
                Cache[c] = map;
                return map;
            }
        }

        private static Encoding?[] CreateEncodings()
        {
            var result = new Encoding?[Pages.Length];
            for (int i = 0; i < Pages.Length; i++)
                result[i] = CodePagesEncodingProvider.Instance.GetEncoding(Pages[i].CodePage);
            return result;
        }
    }
}
