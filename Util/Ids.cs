using System.Text;

namespace Personnel.Util
{
    /// <summary>
    /// Deterministic, collision-resistant NPC ids. An NPC's id is always <c>normalize(packName)_normalize(npcName)</c>
    /// so two NPCs only ever share an id if they're in the same pack AND have the same name - which the editor rejects.
    /// Normalize = lowercase, runs of non-alphanumerics collapsed to a single '_', trimmed.
    /// </summary>
    public static class Ids
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool under = false;
            foreach (char ch in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); under = false; }
                else if (!under) { sb.Append('_'); under = true; }
            }
            return sb.ToString().Trim('_');
        }

        public static string Make(string pack, string name)
        {
            string p = Normalize(pack), n = Normalize(name);
            if (string.IsNullOrEmpty(p)) return n;
            if (string.IsNullOrEmpty(n)) return p;
            return p + "_" + n;
        }
    }
}
