using System;
using UnityEngine;

namespace Personnel.Util
{
    /// <summary>Parses "#RRGGBB" / "#RRGGBBAA" hex strings into a <see cref="Color"/>.</summary>
    public static class ColorParse
    {
        public static bool TryParse(string s, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return false;
            try
            {
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
                color = new Color32(r, g, b, a);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Parse, or return <paramref name="fallback"/> when the string is empty/invalid.</summary>
        public static Color Parse(string s, Color fallback) => TryParse(s, out Color c) ? c : fallback;
    }
}
