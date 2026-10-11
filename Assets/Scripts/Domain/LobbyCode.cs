using System.Text;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Short join code of a Steam lobby. A Steam lobby only has a 64-bit id, so the host publishes this code in the
    /// lobby data and "Rejoindre avec un code" looks it up. Six characters from an alphabet without look-alikes
    /// (no 0/O, 1/I/L): players read it aloud and type it, so the lookup ignores case and spaces.
    /// </summary>
    public static class LobbyCode
    {
        public const int Length = 6;
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public static string Generate(IRandomProvider random)
        {
            var _code = new StringBuilder(Length);
            for (int i = 0; i < Length; i++)
            {
                _code.Append(Alphabet[random.Next(Alphabet.Length)]);
            }
            return _code.ToString();
        }

        /// <summary>What a player typed, as stored in the lobby data: upper case, no whitespace.</summary>
        public static string Normalize(string typed)
        {
            if (string.IsNullOrEmpty(typed))
            {
                return string.Empty;
            }
            var _code = new StringBuilder(typed.Length);
            foreach (char c in typed)
            {
                if (!char.IsWhiteSpace(c))
                {
                    _code.Append(char.ToUpperInvariant(c));
                }
            }
            return _code.ToString();
        }
    }
}
