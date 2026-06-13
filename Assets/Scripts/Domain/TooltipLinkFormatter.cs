namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure tooltip text-formatting policy (Story 11.4). The Focus and Tooltip systems are otherwise
    /// engine-coupled presentation (screen-space geometry, RectTransform/Camera math, DOTween, and the
    /// already-cohesive reflection-based TooltipLinkParser) with no further pure decision to lift; this is
    /// the one extractable rule: wrap every TextMeshPro <c>&lt;link=…&gt;…&lt;/link&gt;</c> run in a colour
    /// tag so links read as distinct. Decision-only (NFR4): returns the formatted string; the adapter sets
    /// it on the label.
    /// </summary>
    public sealed class TooltipLinkFormatter
    {
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
    }
}
