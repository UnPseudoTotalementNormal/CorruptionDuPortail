using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode tests for the pure <see cref="RoleCardPowerVisibility"/> classifier — the single source of
    /// truth for what appears in the role-presentation card (RoleCard) and in which section. These lock the
    /// DESIGN of the card's power list: which powers are shown, hidden, and whether they read as active pills
    /// or passive rows. The controller (RoleCardController) only renders what this decides, so these tests
    /// pin the behaviour without booting NGO or UI Toolkit.
    /// </summary>
    [Category("RoleCardPowerVisibility")]
    public class RoleCardPowerVisibilityTests
    {
        // Convenience wrapper defaulting every flag to "a plain active power with a description".
        private static RoleCardSlot Classify(
            bool authoredIsPassive = false,
            bool hideFromRoleCard = false,
            bool isStolenCopy = false,
            bool hideFromRoleCardRuntime = false,
            bool hasDescription = true)
            => RoleCardPowerVisibility.Classify(
                authoredIsPassive, hideFromRoleCard, isStolenCopy, hideFromRoleCardRuntime, hasDescription);

        // --- The two visible sections -------------------------------------------------------------------

        [Test]
        public void PlainActivePower_ShowsAsActivePill()
        {
            Assert.AreEqual(RoleCardSlot.ActivePill, Classify());
        }

        [Test]
        public void PlainPassivePower_ShowsAsPassiveRow()
        {
            Assert.AreEqual(RoleCardSlot.PassiveRow, Classify(authoredIsPassive: true));
        }

        [Test]
        public void ActivePower_WithNoDescription_StillShowsAsActivePill()
        {
            // Active powers render name + (possibly empty) description — they are never dropped for an empty desc.
            Assert.AreEqual(RoleCardSlot.ActivePill, Classify(hasDescription: false));
        }

        [Test]
        public void PassivePower_WithNoDescription_IsHidden()
        {
            // A passive row is only its description text; with nothing to render it is omitted.
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(authoredIsPassive: true, hasDescription: false));
        }

        // --- Hidden: authored win-objective -------------------------------------------------------------

        [Test]
        public void WinObjectivePower_hideFromRoleCard_IsHidden([Values(false, true)] bool authoredIsPassive)
        {
            // A faction win-objective is not personal kit — hidden whether it is authored active or passive.
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(authoredIsPassive: authoredIsPassive, hideFromRoleCard: true));
        }

        // --- Hidden: one-shot copies (Ugues' Marque d'Hurluberluges, Luma's Mélange des cartes) ----------

        [Test]
        public void StolenCopy_IsHidden()
        {
            // Showing a copied power would leak that the role is real (not factice) and which power it copied.
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(isStolenCopy: true));
        }

        // --- Hidden: L'Incomplet's Réincarnation runtime grants -----------------------------------------

        [Test]
        public void ReincarnationGrantedPower_hideFromRoleCardRuntime_IsHidden()
        {
            // The picked chosen role's kit granted to L'Incomplet — same leak, hidden via the runtime marker.
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(hideFromRoleCardRuntime: true));
        }

        // --- The Réincarnation-stays-active regression guard --------------------------------------------

        [Test]
        public void SpentReincarnationPower_StaysActivePill()
        {
            // Post-use, PReincarnation flips the LIVE isPassive to true to disable itself. The classifier is
            // fed authoredIsPassive (design-time = false for Réincarnation), so its own card entry must stay
            // an active pill — it must NOT jump to the passive section. This is the exact regression Poyo flagged.
            Assert.AreEqual(RoleCardSlot.ActivePill, Classify(authoredIsPassive: false));
        }

        // --- Precedence: a hide flag beats section assignment -------------------------------------------

        [Test]
        public void HideFlags_WinOver_SectionAssignment([Values(false, true)] bool authoredIsPassive)
        {
            // Any single hide reason forces Hidden regardless of active/passive.
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(authoredIsPassive: authoredIsPassive, hideFromRoleCard: true));
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(authoredIsPassive: authoredIsPassive, isStolenCopy: true));
            Assert.AreEqual(RoleCardSlot.Hidden, Classify(authoredIsPassive: authoredIsPassive, hideFromRoleCardRuntime: true));
        }
    }
}
