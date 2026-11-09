using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure tooltip text policy (Story 11.4): how the TextMeshPro <c>&lt;link=…&gt;…&lt;/link&gt;</c> runs of a
    /// tooltip read. Decision-only (NFR4): returns strings; the adapter sets them on the labels.
    /// </summary>
    public sealed class TooltipLinkFormatter
    {
        private static readonly Regex LinkOpen = new Regex("<link=\"?([^\">]+)\"?>", RegexOptions.Compiled);

        /// <summary>
        /// Wraps each <c>&lt;link=</c> opening tag with <c>&lt;color=#{hex}&gt;</c> and each
        /// <c>&lt;/link&gt;</c> with the matching <c>&lt;/color&gt;</c>. Mirrors the original
        /// <c>Replace("&lt;link=", …).Replace("&lt;/link&gt;", …)</c> exactly (including throwing on a null
        /// input, as the original string call did).
        /// </summary>
        public string WrapLinksWithColor(string text, string colorHex)
        {
            return text
                .Replace("<link=", $"<color=#{colorHex}><link=")
                .Replace("</link>", "</link></color>");
        }

        /// <summary>
        /// The ids of the links of a text, in reading order, each once: one keyword card per id beside the bubble.
        /// Empty for a null or empty text.
        /// </summary>
        public List<string> ExtractLinkIds(string text)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return ids;
            }
            foreach (Match match in LinkOpen.Matches(text))
            {
                string id = match.Groups[1].Value;
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }
    }
}
