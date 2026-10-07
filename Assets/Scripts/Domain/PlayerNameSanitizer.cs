using System.Text;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-02 (epic-network-sync-hardening): one place that turns a raw profile name into the replicated pseudo.
    /// Trims and collapses whitespace, strips the UGS "#1234" discriminator ONLY for UGS names (a Steam name may
    /// legitimately contain '#': "#Nohan" used to become an empty pseudo), falls back when nothing is left, and
    /// truncates to the FixedString64Bytes capacity on a code-point boundary (FixedString throws in the Editor when
    /// over capacity and would otherwise cut a multi-byte character in half).
    /// </summary>
    public static class PlayerNameSanitizer
    {
        /// <summary>Usable UTF-8 bytes of a FixedString64Bytes (64 minus the length and terminator bytes).</summary>
        public const int MaxUtf8Bytes = 61;

        public static string Sanitize(string raw, bool stripUgsDiscriminator, string fallback)
        {
            string _name = CollapseWhitespace(raw);
            if (stripUgsDiscriminator)
            {
                _name = StripUgsDiscriminator(_name);
            }
            if (_name.Length == 0)
            {
                _name = CollapseWhitespace(fallback);
            }
            return TruncateUtf8(_name, MaxUtf8Bytes);
        }

        /// <summary>UGS player names are "Name#1234": cut at the LAST '#' when only digits follow it.</summary>
        public static string StripUgsDiscriminator(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            int _hash = name.LastIndexOf('#');
            if (_hash < 0 || _hash == name.Length - 1)
            {
                return name;
            }
            for (int _i = _hash + 1; _i < name.Length; _i++)
            {
                if (!char.IsDigit(name[_i]))
                {
                    return name;
                }
            }
            return name.Substring(0, _hash).TrimEnd();
        }

        public static string TruncateUtf8(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                return text ?? string.Empty;
            }

            var _builder = new StringBuilder();
            int _bytes = 0;
            for (int _i = 0; _i < text.Length; _i++)
            {
                int _width = char.IsHighSurrogate(text[_i]) && _i + 1 < text.Length && char.IsLowSurrogate(text[_i + 1]) ? 2 : 1;
                int _cost = Encoding.UTF8.GetByteCount(text.ToCharArray(_i, _width));
                if (_bytes + _cost > maxBytes)
                {
                    break;
                }
                _builder.Append(text, _i, _width);
                _bytes += _cost;
                _i += _width - 1;
            }
            return _builder.ToString().TrimEnd();
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var _builder = new StringBuilder(text.Length);
            bool _pendingSpace = false;
            foreach (char _c in text.Trim())
            {
                if (char.IsWhiteSpace(_c) || char.IsControl(_c))
                {
                    _pendingSpace = true;
                    continue;
                }
                if (_pendingSpace && _builder.Length > 0)
                {
                    _builder.Append(' ');
                }
                _pendingSpace = false;
                _builder.Append(_c);
            }
            return _builder.ToString();
        }
    }
}
