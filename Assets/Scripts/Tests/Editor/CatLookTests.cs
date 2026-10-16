using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>The player's cat as data: network packing, saved text, fallback of unknown ids, arrow stepping.</summary>
    [Category("CatCustomization")]
    public class CatLookTests
    {
        [Test]
        public void Pack_RoundTrips_EveryPart()
        {
            var look = new CatLook(3, 254, 1, 200);
            Assert.AreEqual(look, CatLook.Unpack(look.Pack()));
            Assert.AreEqual(0u, CatLook.Default.Pack(), "the default cat is the NetworkVariable's zero value");
        }

        [Test]
        public void Encode_RoundTrips_AndRejectsGarbage()
        {
            var look = new CatLook(2, 4, 3, 1);
            Assert.IsTrue(CatLook.TryDecode(look.Encode(), out CatLook decoded));
            Assert.AreEqual(look, decoded);

            foreach (string bad in new[] { null, "", "1.2.3", "1.2.3.4.5", "a.b.c.d", "1.2.3.999", "-1.0.0.0" })
            {
                Assert.IsFalse(CatLook.TryDecode(bad, out CatLook fallback), bad);
                Assert.AreEqual(CatLook.Default, fallback, bad);
            }
        }

        [Test]
        public void With_ChangesOnlyItsSlot()
        {
            CatLook look = CatLook.Default.With(CatSlot.Outfit, 3).With(CatSlot.Mouth, 2);
            Assert.AreEqual(0, look.Get(CatSlot.Fur));
            Assert.AreEqual(3, look.Get(CatSlot.Outfit));
            Assert.AreEqual(0, look.Get(CatSlot.Eyes));
            Assert.AreEqual(2, look.Get(CatSlot.Mouth));
        }

        [Test]
        public void Sanitized_ReplacesOnlyUnknownParts_WithTheDefaults()
        {
            // Known: fur 0-3, outfit 0-4, eyes 0-3, mouth 0-2.
            bool Exists(CatSlot slot, byte id) =>
                id <= (slot == CatSlot.Outfit ? 4 : slot == CatSlot.Mouth ? 2 : 3);
            var defaults = new CatLook(0, 0, 1, 0);

            CatLook sane = new CatLook(9, 4, 200, 2).Sanitized(Exists, defaults);

            Assert.AreEqual(new CatLook(0, 4, 1, 2), sane);
        }

        [Test]
        public void Step_WrapsBothWays_AndStartsFromAnUnknownCurrent()
        {
            byte[] ids = { 0, 1, 4 };
            Assert.AreEqual(1, CatLook.Step(ids, 0, 1));
            Assert.AreEqual(0, CatLook.Step(ids, 4, 1));
            Assert.AreEqual(4, CatLook.Step(ids, 0, -1));
            Assert.AreEqual(1, CatLook.Step(ids, 7, 1), "an unknown current counts as the first item");
            Assert.AreEqual(5, CatLook.Step(new byte[0], 5, 1));
        }
    }
}
