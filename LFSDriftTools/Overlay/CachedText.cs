using System;
using System.Globalization;

namespace LFSDriftTools
{
    internal sealed class CachedText<T> where T : struct, IEquatable<T>
    {
        private readonly string _format;
        private bool _hasValue;
        private T _value;
        private CultureInfo? _culture;
        private string _text = "";

        public CachedText(string format)
        {
            _format = format;
        }

        public string Get(T value)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            if (!_hasValue || !_value.Equals(value) || !ReferenceEquals(culture, _culture))
            {
                _text = string.Format(culture, _format, value);
                _value = value;
                _culture = culture;
                _hasValue = true;
            }
            return _text;
        }
    }

    internal static class CharText
    {
        private static readonly string[] Ascii = BuildAscii();

        private static string[] BuildAscii()
        {
            var table = new string[128];
            for (int i = 0; i < table.Length; i++)
                table[i] = ((char)i).ToString();
            return table;
        }

        public static string Of(char c) => c < Ascii.Length ? Ascii[c] : c.ToString();
    }
}
