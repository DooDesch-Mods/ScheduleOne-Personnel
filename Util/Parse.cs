using System;
using UnityEngine;

namespace Personnel.Util
{
    /// <summary>
    /// Tolerant parsing helpers for manifest values. Enum strings accept any casing and treat '_', '-' and
    /// spaces as noise ("direct-approach" == "DirectApproach"); times accept "HH:MM" or a raw hhmm int
    /// (830 == "8:30"), matching the format S1API schedule specs consume.
    /// </summary>
    internal static class Parse
    {
        public static bool TryParseEnum<T>(string value, out T result) where T : struct, Enum
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string wanted = Fold(value);
            foreach (string name in Enum.GetNames(typeof(T)))
            {
                if (Fold(name) == wanted)
                {
                    result = (T)Enum.Parse(typeof(T), name);
                    return true;
                }
            }
            return false;
        }

        private static string Fold(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        /// <summary>"HH:MM"/"H:MM" or a raw hhmm number ("830", "1930") to the hhmm int the game uses.</summary>
        public static bool TryParseTime(string value, out int hhmm)
        {
            hhmm = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();

            int h, m;
            int colon = value.IndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(value.Substring(0, colon), out h)) return false;
                if (!int.TryParse(value.Substring(colon + 1), out m)) return false;
            }
            else
            {
                if (!int.TryParse(value, out int raw) || raw < 0) return false;
                h = raw / 100;
                m = raw % 100;
            }

            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            hhmm = h * 100 + m;
            return true;
        }

        /// <summary>[x,y,z] array to a Vector3.</summary>
        public static bool TryParseVec3(float[] xyz, out Vector3 v)
        {
            v = default;
            if (xyz == null || xyz.Length != 3) return false;
            if (float.IsNaN(xyz[0]) || float.IsNaN(xyz[1]) || float.IsNaN(xyz[2])) return false;
            v = new Vector3(xyz[0], xyz[1], xyz[2]);
            return true;
        }

        /// <summary>Deterministic, platform-stable hash of a string (FNV-1a). Never use string.GetHashCode for this.</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (s != null)
                    foreach (char c in s)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                return (int)hash;
            }
        }
    }
}
