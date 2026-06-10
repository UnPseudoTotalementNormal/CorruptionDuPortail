using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 4.1 — EditMode characterization of <see cref="PowerResolver.ResolveCursedVision"/>.
    /// Pure Domain, no host: drives the decision and asserts the ORDERED descriptor list, the
    /// chosen/non-chosen branch (verdict text + card flag), and that engine ids are echoed
    /// verbatim. The PlayMode golden (Story 4.0) proves the adapter dispatches this list.
    /// </summary>
    [Category("PowerResolver")]
    public class PowerResolverTests
    {
        private const int Owner = 0;
        private const int Target = 100;
        private const int CardId = 2;       // (int)CardEffectID.CursedVision
        private const int ServerWindow = -1; // (int)ChatWindowIDs.Server

        private readonly PowerResolver _resolver = new();

        [Test]
        public void CursedVision_NonChosen_OrderedTrace_IsPinned()
        {
            var trace = _resolver.ResolveCursedVision(Owner, Target, false, "Bob", CardId, ServerWindow);

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(Owner, Target),
                new CorruptPlayer(Target),
                new RevealInfo(Target, RevealField.CorruptRevealed, RevealVisibility.Personal, Owner, false),
                new AddCardEffect(CardId, Target, true),                 // hidden (not chosen)
                new ChatLocal("Bob n'est pas un élu.", ServerWindow),
                new CorruptPlayer(Owner),
                new RevealInfo(Owner, RevealField.CorruptRevealed, RevealVisibility.Personal, Owner, false),
            }, trace);
        }

        [Test]
        public void CursedVision_Chosen_FlipsCardFlagAndVerdict()
        {
            var trace = _resolver.ResolveCursedVision(Owner, Target, true, "Alice", CardId, ServerWindow);

            // Same shape/order; only the card flag (false = not hidden) and the verdict change.
            Assert.AreEqual(new AddCardEffect(CardId, Target, false), trace[3]);
            Assert.AreEqual(new ChatLocal("Alice est un élu.", ServerWindow), trace[4]);
        }

        [Test]
        public void CursedVision_EchoesEngineIds_NoMagicConstants()
        {
            var trace = _resolver.ResolveCursedVision(Owner, Target, false, "X", 7, 42);
            Assert.AreEqual(new AddCardEffect(7, Target, true), trace[3]);
            Assert.AreEqual(new ChatLocal("X n'est pas un élu.", 42), trace[4]);
        }

        [Test]
        public void CursedVision_TargetsBothTargetThenOwner()
        {
            var trace = _resolver.ResolveCursedVision(Owner, Target, false, "X", CardId, ServerWindow);
            Assert.AreEqual(7, trace.Count);
            Assert.AreEqual(new NewTargeting(Owner, Target), trace[0]);
            Assert.AreEqual(new CorruptPlayer(Target), trace[1]);
            Assert.AreEqual(new CorruptPlayer(Owner), trace[5]); // owner self-corrupt last
        }

        // ---- Story 4.2: PBoundByInk click path -------------------------------------------
        [Test]
        public void BoundByInkClick_OrderedTrace_IsPinned()
        {
            const int chatId = 515100;
            var trace = _resolver.ResolveBoundByInkClick(Owner, Target, chatId);

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(Owner, Target),
                new DiscoverChat(chatId, "Lié par l'encre", PowerEffectAudience.Specific(Target)),
                new RegisterInkTarget(Target),
            }, trace);
        }

        [Test]
        public void BoundByInkClick_DiscoverChat_TargetsThePickedSlot_NotOwner()
        {
            var trace = _resolver.ResolveBoundByInkClick(Owner, Target, -1);
            var discover = (DiscoverChat)trace[1];
            Assert.AreEqual(PowerEffectAudience.Specific(Target), discover.Audience);
            Assert.AreEqual(-1, discover.ChatId); // unassigned chat id echoed verbatim
        }
    }
}
