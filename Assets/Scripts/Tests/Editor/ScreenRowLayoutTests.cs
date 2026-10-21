using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    public class ScreenRowLayoutTests
    {
        private const float Wide = 16f / 9f;
        private const float Card = 6.3f / 8.8f;

        [Test]
        public void EightCardsAtSixteenNine_KeepTheMockupSize()
        {
            Assert.AreEqual(ScreenRowLayout.CardHeight, ScreenRowLayout.CardHeightFor(8, Wide, Card), 1e-5f);
        }

        [TestCase(14, 16f / 9f)]
        [TestCase(14, 4f / 3f)]
        [TestCase(10, 4f / 3f)]
        public void ManyCards_ShrinkToStayOnScreen(int count, float aspect)
        {
            ScreenRowSlot[] slots = ScreenRowLayout.CardSlots(count, aspect, Card, true, true);
            float height = ScreenRowLayout.CardHeightFor(count, aspect, Card);
            float halfWidth = height * Card / 2f;

            Assert.LessOrEqual(height, ScreenRowLayout.CardHeight + 1e-5f);
            Assert.GreaterOrEqual(slots[0].X - halfWidth, -aspect, "the first card leaves the screen");
            Assert.LessOrEqual(slots[count - 1].X + halfWidth, aspect, "the last card leaves the screen");
        }

        [Test]
        public void Cards_AreCentredAndInOrder()
        {
            ScreenRowSlot[] slots = ScreenRowLayout.CardSlots(7, Wide, Card, true, true);

            Assert.AreEqual(0f, slots[3].X, 1e-5f);
            for (int i = 1; i < slots.Length; i++)
            {
                Assert.Greater(slots[i].X, slots[i - 1].X);
                Assert.AreEqual(slots[0].Y, slots[i].Y, 1e-6f);
            }
        }

        [Test]
        public void OpenCards_SitAboveTheOpenRoles_WithoutOverlap()
        {
            float height = ScreenRowLayout.CardHeightFor(8, Wide, Card);
            float cardsBottom = ScreenRowLayout.CardsCentreY(height, true, true) - height / 2f;
            float rolesTop = ScreenRowLayout.RolesCentreY(true) + ScreenRowLayout.RolesHeight / 2f;

            Assert.Greater(cardsBottom, rolesTop);
            Assert.Greater(ScreenRowLayout.RolesCentreY(true) - ScreenRowLayout.RolesHeight / 2f, -1f);
        }

        [Test]
        public void ClosedRoles_LetTheCardsComeDown()
        {
            float height = ScreenRowLayout.CardHeightFor(8, Wide, Card);

            Assert.Less(ScreenRowLayout.CardsCentreY(height, true, false), ScreenRowLayout.CardsCentreY(height, true, true));
            Assert.AreEqual(-1f + ScreenRowLayout.BottomMargin, ScreenRowLayout.CardsCentreY(height, true, false) - height / 2f, 1e-5f);
        }

        [Test]
        public void ClosedRows_WaitUnderTheScreen()
        {
            float height = ScreenRowLayout.CardHeightFor(8, Wide, Card);

            Assert.Less(ScreenRowLayout.CardsCentreY(height, false, true) + height / 2f, -1f);
            Assert.Less(ScreenRowLayout.RolesCentreY(false) + ScreenRowLayout.RolesHeight / 2f, -1f);
        }

        [Test]
        public void NoCards_NoSlots()
        {
            Assert.IsEmpty(ScreenRowLayout.CardSlots(0, Wide, Card, true, true));
        }
    }
}
