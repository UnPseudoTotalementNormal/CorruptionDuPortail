using CorruptionDuPortail.Domain.Localization;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using System.Linq;
using CorruptionDuPortail.Domain.PlayerIcons;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Powers v2 — EditMode characterization of pure power DECISIONS. No host, no NGO: build a
    /// PowerContext by hand, call Decide, assert the ordered effect list by value. This is the
    /// unit-test surface the v2 architecture exists to enable.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionTests
    {
        [Test]
        public void CorruptionParanoia_RevealsOwnCorruptionToSelf_Broadcast()
        {
            var outcome = new CorruptionParanoiaDecision().Decide(new PowerContext(ownerSlot: 3));

            Assert.IsTrue(outcome.Accepted);
            Assert.AreEqual(1, outcome.UsesConsumed);
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(3, RevealField.CorruptRevealed, RevealVisibility.Personal, 3, true),
            }, outcome.Effects);
        }

        [Test]
        public void CorruptionParanoia_IsPassive()
        {
            Assert.IsTrue(new CorruptionParanoiaDecision().IsPassive);
            Assert.AreEqual(PowerId.CorruptionParanoia, new CorruptionParanoiaDecision().Id);
        }

        // ---- Roster-reading passives -----------------------------------------------------
        private sealed class FakeRoster : IRosterView
        {
            public System.Collections.Generic.IReadOnlyList<int> Slots { get; set; } = new int[0];
            public readonly System.Collections.Generic.Dictionary<int, Characters.FactionType> Factions = new();
            public readonly System.Collections.Generic.Dictionary<int, string> Pseudos = new();
            public readonly System.Collections.Generic.Dictionary<int, int> Roles = new();
            public readonly System.Collections.Generic.HashSet<int> Robots = new();
            public readonly System.Collections.Generic.HashSet<int> Healed = new();
            public readonly System.Collections.Generic.HashSet<int> Corrupted = new();
            public readonly System.Collections.Generic.HashSet<int> Chained = new();
            public readonly System.Collections.Generic.HashSet<int> Eliminated = new();
            public readonly System.Collections.Generic.HashSet<int> Fakes = new();
            public readonly System.Collections.Generic.Dictionary<int, string> RoleNames = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
            public bool IsRobot(int slot) => Robots.Contains(slot);
            public bool IsHealed(int slot) => Healed.Contains(slot);
            public bool IsCorrupted(int slot) => Corrupted.Contains(slot);
            public bool IsChained(int slot) => Chained.Contains(slot);
            public bool IsEliminated(int slot) => Eliminated.Contains(slot);
            public bool IsFake(int slot) => Fakes.Contains(slot);
            public string RoleNameOf(int slot) => RoleNames.TryGetValue(slot, out var n) ? n : "";
        }

        [Test]
        public void CorruptionInsight_RevealsEveryCharacterCorruptionToOwner_InOrder()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 5 } };
            var outcome = new CorruptionInsightDecision().Decide(new PowerContext(ownerSlot: 0, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(5, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
            }, outcome.Effects);
        }

        [Test]
        public void CorruptionKnowledge_RevealsForceCorruptPerCharacter()
        {
            var roster = new FakeRoster { Slots = new[] { 2, 7 } };
            var outcome = new CorruptionKnowledgeDecision().Decide(new PowerContext(ownerSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(2, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 2, true),
                new RevealInfo(7, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 2, true),
            }, outcome.Effects);
        }

        [Test]
        public void EyeOfTheVoid_DiscoversAnomalyChat_AnomaliesOnly()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.chosen;
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Factions[2] = Characters.FactionType.anomaly;

            var outcome = new EyeOfTheVoidDecision { AnomalyChatId = 1 }.Decide(new PowerContext(ownerSlot: 0, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new DiscoverChat(1, "", PowerEffectAudience.Specific(1)),
                new DiscoverChat(1, "", PowerEffectAudience.Specific(2)),
                new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 1, false),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 2, false),
            }, outcome.Effects);
        }

        [Test]
        public void EyeOfTheVoid_SkipsFakeAnomalies()
        {
            // Slot 3 is a fake anomaly (unassigned role): no chat, not revealed, sees nothing.
            var roster = new FakeRoster { Slots = new[] { 1, 2, 3 } };
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Factions[2] = Characters.FactionType.anomaly;
            roster.Factions[3] = Characters.FactionType.anomaly;
            roster.Fakes.Add(3);

            var outcome = new EyeOfTheVoidDecision { AnomalyChatId = 1 }.Decide(new PowerContext(ownerSlot: 1, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new DiscoverChat(1, "", PowerEffectAudience.Specific(1)),
                new DiscoverChat(1, "", PowerEffectAudience.Specific(2)),
                new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 1, false),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 2, false),
            }, outcome.Effects);
        }

        [Test]
        public void EyeOfTheVoid_NoAnomalies_AcceptsEmpty()
        {
            var roster = new FakeRoster { Slots = new[] { 0 } };
            roster.Factions[0] = Characters.FactionType.chosen;
            var outcome = new EyeOfTheVoidDecision { AnomalyChatId = 1 }.Decide(new PowerContext(ownerSlot: 0, roster: roster));
            Assert.IsTrue(outcome.Accepted);
            Assert.AreEqual(0, outcome.Effects.Count);
        }

        [Test]
        public void AutoCorruption_CorruptsOwner()
        {
            var outcome = new AutoCorruptionDecision().Decide(new PowerContext(ownerSlot: 4));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new CorruptPlayer(4) }, outcome.Effects);
        }

        // ---- Active powers ---------------------------------------------------------------
        [Test]
        public void ChainedByShadows_RoleMatchChosen_TargetsRevealsChains()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;                 // target(1) same role as picked-role owner(2)
            roster.Factions[1] = Characters.FactionType.chosen;
            var ctx = new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster);

            var outcome = new ChainedByShadowsDecision().Decide(ctx);

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
                new AddToChain(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_RoleMatchNotChosen_CorruptsButNoChain()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            roster.Factions[1] = Characters.FactionType.anomaly;
            var outcome = new ChainedByShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            // A correct guess corrupts the target even when it is NOT chosen (so no chain).
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_RoleMismatch_OnlyTargets()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 9;                 // different roles
            var outcome = new ChainedByShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, outcome.Effects);
        }

        // ---- Lot B (Abyss): sole-anomaly extra-use bonus ---------------------------------
        // Note: FactionType default is `anomaly` (enum value 0), so every slot's faction is set explicitly —
        // an unset slot would silently count as an in-play anomaly and skew the "sole anomaly" test.

        [Test]
        public void ChainedByShadows_SoleAnomaly_CorrectGuess_BonusFresh_GrantsExtraUse()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.anomaly;   // owner = the only anomaly in play
            roster.Factions[1] = Characters.FactionType.marginal;  // target: non-chosen so the chain stays out
            roster.Factions[2] = Characters.FactionType.chosen;    // picked-role owner
            roster.Roles[1] = 7; roster.Roles[2] = 7;              // guessed right
            var state = new FakeState().With<IExtraUseState>(new FakeExtraUse { BonusConsumedThisNight = false });

            var outcome = new ChainedByShadowsDecision().Decide(
                new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster, state: state));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
                new GrantExtraUse(0),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_SoleAnomaly_CorrectGuess_BonusAlreadyConsumed_NoExtraUse()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.anomaly;
            roster.Factions[1] = Characters.FactionType.marginal;
            roster.Factions[2] = Characters.FactionType.chosen;
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            var state = new FakeState().With<IExtraUseState>(new FakeExtraUse { BonusConsumedThisNight = true });

            var outcome = new ChainedByShadowsDecision().Decide(
                new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster, state: state));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_TwoAnomaliesInPlay_CorrectGuess_NoExtraUse()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.anomaly;
            roster.Factions[1] = Characters.FactionType.anomaly;   // a second anomaly still in play
            roster.Factions[2] = Characters.FactionType.chosen;
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            var state = new FakeState().With<IExtraUseState>(new FakeExtraUse { BonusConsumedThisNight = false });

            var outcome = new ChainedByShadowsDecision().Decide(
                new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster, state: state));

            // Two anomalies in play => no bonus. The correct guess still corrupts the (anomaly) target.
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_OtherAnomalyChained_CountsAsSole_GrantsExtraUse()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.anomaly;
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Chained.Add(1);                                  // the other anomaly is out of play
            roster.Factions[2] = Characters.FactionType.chosen;
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            var state = new FakeState().With<IExtraUseState>(new FakeExtraUse { BonusConsumedThisNight = false });

            var outcome = new ChainedByShadowsDecision().Decide(
                new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster, state: state));

            // target(1) is anomaly (not chosen) so no chain, but the correct guess corrupts it; sole anomaly
            // in play => the bonus fires.
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new CorruptPlayer(1),
                new GrantExtraUse(0),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_SoleAnomaly_WrongGuess_NoExtraUse()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.anomaly;
            roster.Factions[1] = Characters.FactionType.marginal;
            roster.Factions[2] = Characters.FactionType.chosen;
            roster.Roles[1] = 7; roster.Roles[2] = 9;              // wrong guess
            var state = new FakeState().With<IExtraUseState>(new FakeExtraUse { BonusConsumedThisNight = false });

            var outcome = new ChainedByShadowsDecision().Decide(
                new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster, state: state));

            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, outcome.Effects);
        }

        [Test]
        public void TruthChains_Anomaly_ChainsAndAnnounces()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Pseudos[1] = "Bob";
            var outcome = new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new AddToChain(1),
                new ChatSendServer(Loc.Token("{0} sera lié par les chaînes de la vérité.", "Bob"), -1),
            }, outcome.Effects);
        }

        [Test]
        public void TruthChains_NonAnomaly_OnlyTargets()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.chosen;
            var outcome = new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, outcome.Effects);
        }

        [Test]
        public void Blessing_RoleMatch_NotHealed_HealsRevealsBlessesAnnounces()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7; roster.Pseudos[1] = "Bob";
            var outcome = new BlessingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new HealPlayer(1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new SetBlessed(1),
                new ChatBroadcast(Loc.Token("{0} est maintenant béni.", "Bob"), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void Blessing_AlreadyHealed_SkipsHeal()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7; roster.Pseudos[1] = "Bob"; roster.Healed.Add(1);
            var outcome = new BlessingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new SetBlessed(1),
                new ChatBroadcast(Loc.Token("{0} est maintenant béni.", "Bob"), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void HighPriorityBounty_Robot_EliminatesBroadcastsRevealsRefresh()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Robots.Add(1); roster.Pseudos[1] = "Bob"; roster.RoleNames[0] = "Chasseur";
            var outcome = new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 0),
                new SetEliminated(1),
                new ChatBroadcast(Loc.Token("{0} était le robot et a été éliminé par {1}.", "Bob", Loc.Token("Chasseur")), -1, PowerEffectAudience.All),
                new RevealPublic(1, RevealField.RoleRevealed),
                RequestCharacterRefresh.Instance,
            }, outcome.Effects);
        }

        [Test]
        public void HighPriorityBounty_NotRobot_ChainsOwnerWarnsRefresh()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            var outcome = new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 0),
                new AddToChain(0),
                new ChatBroadcast(Loc.Token("Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil."), -1, PowerEffectAudience.Specific(0)),
                RequestCharacterRefresh.Instance,
            }, outcome.Effects);
        }

        [Test]
        public void CursedVision_NonChosen_CorruptsTargetRevealsCardsChats()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.anomaly; roster.Pseudos[1] = "Bob";
            var outcome = new CursedVisionDecision { CardEffectId = 2 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            // The owner no longer corrupts itself (role-adjustment pass): only the TARGET is corrupted/revealed.
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new AddCardEffect(2, 1, true),
                new ChatLocal(Loc.Token("{0} n'est pas un élu.", "Bob"), -1),
            }, outcome.Effects);
        }

        [Test]
        public void CursedVision_Chosen_FlipsCardAndVerdict()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.chosen; roster.Pseudos[1] = "Alice";
            var outcome = new CursedVisionDecision { CardEffectId = 2 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            Assert.AreEqual(new AddCardEffect(2, 1, false), outcome.Effects[3]);
            Assert.AreEqual(new ChatLocal(Loc.Token("{0} est un élu.", "Alice"), -1), outcome.Effects[4]);
        }

        [Test]
        public void Omniscience_ChosenTarget_TargetsStoresRevealsHacksRefreshes()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 5 } };
            roster.Factions[5] = Characters.FactionType.chosen;
            var outcome = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 5, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 5),
                new StoreHackTarget(5),
                new RevealInfo(5, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(5, RevealField.Hacked, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, outcome.Effects);
        }

        [Test]
        public void Omniscience_NonChosenTarget_RevealsRoleOnly_NoHack()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 5 } };
            roster.Factions[5] = Characters.FactionType.anomaly; // idem marginal : pas de piratage
            var outcome = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 5, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 5),
                new RevealInfo(5, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, outcome.Effects);
        }

        [Test]
        public void InfiniteMessage_SetsOwnerMessageLeftToMax()
        {
            var outcome = new InfiniteMessageDecision().Decide(new PowerContext(ownerSlot: 2));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new SetMessageLeft(2, int.MaxValue) }, outcome.Effects);
        }

        [Test]
        public void EmbraceOfShadows_RoleMatch_CorruptsRaisesRevealsBoth()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            var outcome = new EmbraceOfShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new CorruptionSucceeded(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, false),
            }, outcome.Effects);
        }

        [Test]
        public void EmbraceOfShadows_RoleMismatch_TargetsFails()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 9;
            var outcome = new EmbraceOfShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1), new CorruptionFailed(1) }, outcome.Effects);
        }

        [Test]
        public void LackOfAffection_ChosenTrueLocal_RevealsAndChats()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.chosen; roster.RoleNames[0] = "Marginal";
            var decision = new LackOfAffectionDecision { IconId = 42, VisitCardEffectId = 4 };
            var outcome = decision.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, isTrueLocalTarget: true, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(0, RevealField.RoleRevealed, RevealVisibility.Personal, 1, false),
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 1, false),
                new ChatLocal(Loc.Token("{0} est venu(e) vous voir...", Loc.Token("Marginal")), -1),
                new AddPlayerIcon(42, 0, 1, PlayerIconLifetime.ClearAtAwakeningStart),
                new AddCardEffect(4, 1, false, 0),
            }, outcome.Effects);
        }

        [Test]
        public void LackOfAffection_NonChosen_NotTrueLocal_OnlyTheVisitMarks()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.anomaly;
            var decision = new LackOfAffectionDecision { IconId = 42, VisitCardEffectId = 4 };
            var outcome = decision.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, isTrueLocalTarget: false, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new AddPlayerIcon(42, 0, 1, PlayerIconLifetime.ClearAtAwakeningStart),
                new AddCardEffect(4, 1, false, 0),
            }, outcome.Effects);
        }

        // The visit marks run the opposite way to the Orpheline's passive: the TARGET sees her icon on HER
        // thumbnail (an anomaly learns she came), and she marks the target's CARD, never its role thumbnail.
        [Test]
        public void LackOfAffection_VisitMarks_TargetSeesOrphelineIcon_OrphelineMarksTargetCard()
        {
            var roster = new FakeRoster { Slots = new[] { 3, 7 } };
            roster.Factions[7] = Characters.FactionType.anomaly;
            var decision = new LackOfAffectionDecision { IconId = 42, VisitCardEffectId = 4 };
            var outcome = decision.Decide(new PowerContext(ownerSlot: 3, targetSlot: 7, isTrueLocalTarget: true, roster: roster));

            var icon = outcome.Effects.OfType<AddPlayerIcon>().Single();
            Assert.AreEqual(3, icon.MarkedSlot, "the icon sits on the Orpheline's thumbnail");
            Assert.AreEqual(7, icon.ViewerSlot, "only the visited player sees it");

            var card = outcome.Effects.OfType<AddCardEffect>().Single();
            Assert.AreEqual(7, card.Slot, "the mark sits on the visited player's card");
            Assert.AreEqual(3, card.ViewerSlot, "only the Orpheline sees it");
        }

        [Test]
        public void CorruptingMark_TargetsStoresRaisesCorrupts()
        {
            var outcome = new CorruptingMarkDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 3));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 3),
                new StoreLastCorrupted(3),
                new CorruptionSucceeded(3),
                new CorruptPlayer(3),
            }, outcome.Effects);
        }

        [Test]
        public void Legacy_GrantsLegacyToOwner()
        {
            var outcome = new LegacyDecision().Decide(new PowerContext(ownerSlot: 5));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new GrantLegacyPower(5) }, outcome.Effects);
        }

        [Test]
        public void Reincarnation_TargetsPassiveThenGrants()
        {
            var outcome = new ReincarnationDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 3));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 3),
                new SetPassiveBroadcast(true),
                new GrantRolePowers(0, 3),
            }, outcome.Effects);
        }

        [Test]
        public void MarqueHurluberluges_GrantsStolenPowersToOwner()
        {
            var outcome = new MarqueHurluberlugesDecision().Decide(new PowerContext(ownerSlot: 7));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new GrantStolenPowers(7) }, outcome.Effects);
        }

        [Test]
        public void MarqueHurluberluges_IsPassive()
        {
            Assert.IsTrue(new MarqueHurluberlugesDecision().IsPassive);
            Assert.AreEqual(PowerId.MarqueHurluberluges, new MarqueHurluberlugesDecision().Id);
        }

        // ---- Query/state-port powers -----------------------------------------------------
        private sealed class FakeState : IPowerStateResolver
        {
            private readonly System.Collections.Generic.Dictionary<System.Type, object> _map = new();
            public FakeState With<T>(T impl) where T : class { _map[typeof(T)] = impl; return this; }
            public TPort Resolve<TPort>() where TPort : class => _map.TryGetValue(typeof(TPort), out var v) ? (TPort)v : null;
        }
        private sealed class FakeInk : IInkChatState { public int ChatId { get; set; } }
        private sealed class FakeVision : IVisionGuesses { public System.Collections.Generic.IReadOnlyList<VisionGuess> Guesses { get; set; } }
        private sealed class FakeCards : ICardsShufflingGuess { public bool IsCorrect { get; set; } public string ClickedPseudo { get; set; } public string GuessRoleName { get; set; } public System.Collections.Generic.IReadOnlyList<string> TargetedRoleNames { get; set; } = new string[0]; }
        private sealed class FakeExtraUse : IExtraUseState { public bool BonusConsumedThisNight { get; set; } }

        [Test]
        public void BoundByInk_TargetsDiscoversRegisters()
        {
            var state = new FakeState().With<IInkChatState>(new FakeInk { ChatId = 515100 });
            var outcome = new BoundByInkDecision { IconId = 42, LinkCardEffectId = 5 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new DiscoverChat(515100, Loc.Token("Lié par l'encre"), PowerEffectAudience.Specific(1)),
                new RegisterInkTarget(1),
                new AddPlayerIcon(42, 0, 1, PlayerIconLifetime.ClearAtAwakeningStart),
                new AddCardEffect(5, 1, false, 0),
            }, outcome.Effects);
        }

        [Test]
        public void Clandestine_CountsChosenAmongPickedTargets_OwnerOnly()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2, 3 } };
            roster.Factions[1] = Characters.FactionType.chosen;
            roster.Factions[2] = Characters.FactionType.anomaly;
            roster.Factions[3] = Characters.FactionType.chosen;
            var outcome = new ClandestineObservationDecision().Decide(
                new PowerContext(ownerSlot: 0, roster: roster, targetSlots: new[] { 1, 2, 3 }));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new ChatBroadcast(Loc.Token("Parmi les joueurs observés, {0} sont des élus.", 2), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void Clandestine_NoChosenAmongTargets_ReportsZero()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Factions[2] = Characters.FactionType.marginal;
            var outcome = new ClandestineObservationDecision().Decide(
                new PowerContext(ownerSlot: 0, roster: roster, targetSlots: new[] { 1, 2 }));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new ChatBroadcast(Loc.Token("Parmi les joueurs observés, {0} sont des élus.", 0), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void Vision_FirstMatchStops_FoundMessage()
        {
            var state = new FakeState().With<IVisionGuesses>(new FakeVision { Guesses = new[] { new VisionGuess(1, false, "Alice"), new VisionGuess(7, true, "Bob"), new VisionGuess(9, false, "Carol") } });
            var outcome = new VisionOfTheImpossibleDecision().Decide(new PowerContext(ownerSlot: 0, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new NewTargeting(0, 7),
                new ChatBroadcast(Loc.Token("{0} est l'un de ces personnages.", "Bob"), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void Vision_NoMatch_NotFound()
        {
            var state = new FakeState().With<IVisionGuesses>(new FakeVision { Guesses = new[] { new VisionGuess(1, false, "A"), new VisionGuess(7, false, "B") } });
            var outcome = new VisionOfTheImpossibleDecision().Decide(new PowerContext(ownerSlot: 0, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new NewTargeting(0, 7),
                new ChatBroadcast(Loc.Token("Aucun personnage n'a été trouvé."), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void CardsShuffling_Correct_RecordsReveals()
        {
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards { IsCorrect = true, ClickedPseudo = "Bob", GuessRoleName = "Sorcier" });
            var outcome = new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new DiscoveredAdd(1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new ChatBroadcast(Loc.Token("Vous avez correctement deviné que {0} est {1}.", "Bob", Loc.Token("Sorcier")), -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void CardsShuffling_Incorrect_WithTargets_AppendsList()
        {
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards { IsCorrect = false, ClickedPseudo = "Bob", GuessRoleName = "Sorcier", TargetedRoleNames = new[] { "Robot", "Élu" } });
            var outcome = new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new ChatBroadcast(
                    Loc.Token("Votre supposition était incorrecte, {0} n'est pas {1}.", "Bob", Loc.Token("Sorcier")) +
                    "\n" + Loc.Token("Le role {0} a ciblé ces rôles:", Loc.Token("Sorcier")) +
                    "\n- " + Loc.Token("Robot") + "\n- " + Loc.Token("Élu"),
                    -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void PersonalBeacons_RevealsForceCorruptPerRobot()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Robots.Add(1); roster.Robots.Add(2);
            var outcome = new PersonalBeaconsDecision().Decide(new PowerContext(ownerSlot: 0, roster: roster));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(1, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(2, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 0, true),
            }, outcome.Effects);
        }

        // ---- T36: what each player reads --------------------------------------------------
        // A decision sends tokens (asserted above); these read them back with the game's real translation table. The
        // French must stay, to the letter, the sentence the game wrote before it had a language system.

        [Test]
        public void ChatMessage_WithARoleName_IsReadInEachPlayersLanguage()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Robots.Add(1); roster.Pseudos[1] = "Bob"; roster.RoleNames[0] = "La Chasseuse de Prime";
            var outcome = new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));
            string sent = ((ChatBroadcast)outcome.Effects[2]).Message;

            Localization.LocalizationRuntime.LoadTables();
            try
            {
                Assert.AreEqual("Bob était le robot et a été éliminé par La Chasseuse de Prime.", Loc.Resolve(sent));
                Loc.SetLanguage(GameLanguage.English);
                Assert.AreEqual("Bob was the robot and was eliminated by The Bounty Hunter.", Loc.Resolve(sent));
                CollectionAssert.IsEmpty(Loc.MissingTexts);
            }
            finally
            {
                Loc.SetLanguage(GameLanguage.French);
            }
        }

        [Test]
        public void ChatMessage_OfSeveralLines_IsReadInEachPlayersLanguage()
        {
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards
            {
                IsCorrect = false, ClickedPseudo = "Bob", GuessRoleName = "Le Gardien", TargetedRoleNames = new[] { "Le Robot", "L'Orpheline" },
            });
            var outcome = new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            string sent = ((ChatBroadcast)outcome.Effects[1]).Message;

            Localization.LocalizationRuntime.LoadTables();
            try
            {
                Assert.AreEqual(
                    "Votre supposition était incorrecte, Bob n'est pas Le Gardien.\nLe role Le Gardien a ciblé ces rôles:\n- Le Robot\n- L'Orpheline",
                    Loc.Resolve(sent));
                Loc.SetLanguage(GameLanguage.English);
                Assert.AreEqual(
                    "Your guess was wrong, Bob is not The Guardian.\nThe role The Guardian targeted these roles:\n- The Robot\n- The Orphan",
                    Loc.Resolve(sent));
                CollectionAssert.IsEmpty(Loc.MissingTexts);
            }
            finally
            {
                Loc.SetLanguage(GameLanguage.French);
            }
        }

        [Test]
        public void ChatMessage_FitsTheNetworkString_EvenWithTheLongestNames()
        {
            // The chat travels in a FixedString512Bytes: a token (French source + marks + arguments) must fit like the
            // sentence did. Worst case: the longest role name guessed, and every chosen role targeted.
            string longest = "Abyss, L'Extension du Néant";
            var targeted = new[]
            {
                "Luma, la croupière", "Dr Gloubi", "La Dryade", "Le Gardien", "Geôlier", "L'Incomplet", "Le Messager",
                "Nara, l'oracle brisée", "L'Orpheline", "Le Repenti", "Le Technomancien", "La Chasseuse de Prime",
            };
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards
            {
                IsCorrect = false, ClickedPseudo = new string('W', 20), GuessRoleName = longest, TargetedRoleNames = targeted,
            });
            var outcome = new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            string sent = ((ChatBroadcast)outcome.Effects[1]).Message;

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(sent), 509, "FixedString512Bytes holds 509 bytes of text");
        }
    }
}
