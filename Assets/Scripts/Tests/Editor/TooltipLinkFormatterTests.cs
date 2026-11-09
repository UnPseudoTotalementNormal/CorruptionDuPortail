using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 11.4 — EditMode characterization of <see cref="TooltipLinkFormatter.WrapLinksWithColor"/>,
    /// the pure extraction of TooltipManager's link-colouring policy. Pins the single-link wrap, multiple
    /// links, the no-link passthrough, the empty string, and that the supplied hex is honoured verbatim.
    /// </summary>
    [Category("TooltipLinkFormatter")]
    public class TooltipLinkFormatterTests
    {
        private const string Hex = "6fb5d1";
        private readonly TooltipLinkFormatter _formatter = new();

        [Test]
        public void SingleLink_IsWrappedInTheColourTag()
        {
            Assert.AreEqual(
                "<color=#6fb5d1><link=power_0_1>Vision</link></color>",
                _formatter.WrapLinksWithColor("<link=power_0_1>Vision</link>", Hex));
        }

        [Test]
        public void MultipleLinks_AreEachWrapped()
        {
            Assert.AreEqual(
                "a <color=#6fb5d1><link=x>X</link></color> b <color=#6fb5d1><link=y>Y</link></color>",
                _formatter.WrapLinksWithColor("a <link=x>X</link> b <link=y>Y</link>", Hex));
        }

        [Test]
        public void TextWithoutLinks_IsReturnedUnchanged()
        {
            Assert.AreEqual("plain description, no links", _formatter.WrapLinksWithColor("plain description, no links", Hex));
        }

        [Test]
        public void EmptyString_StaysEmpty()
        {
            Assert.AreEqual("", _formatter.WrapLinksWithColor("", Hex));
        }

        [Test]
        public void TheSuppliedHex_IsHonouredVerbatim()
        {
            Assert.AreEqual(
                "<color=#ff0000><link=x>X</link></color>",
                _formatter.WrapLinksWithColor("<link=x>X</link>", "ff0000"));
        }

        [Test]
        public void LinkIds_AreListedInReadingOrder_EachOnce()
        {
            CollectionAssert.AreEqual(
                new[] { "powercomponent_3_41_0", "corruption" },
                _formatter.ExtractLinkIds(
                    "Texte.\n<link=powercomponent_3_41_0>En chaîne</link>, <link=\"corruption\">Corruption</link> <link=corruption>x</link>"));
        }

        [Test]
        public void LinkIds_OfATextWithoutLinks_AreEmpty()
        {
            Assert.IsEmpty(_formatter.ExtractLinkIds("plain description, no links"));
            Assert.IsEmpty(_formatter.ExtractLinkIds(null));
        }
    }
}
