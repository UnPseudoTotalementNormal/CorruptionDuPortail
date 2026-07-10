using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
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
            public readonly System.Collections.Generic.Dictionary<int, string> RoleNames = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
            public bool IsRobot(int slot) => Robots.Contains(slot);
            public bool IsHealed(int slot) => Healed.Contains(slot);
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
                new AddToChain(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_RoleMatchNotChosen_NoChain()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            roster.Factions[1] = Characters.FactionType.anomaly;
            var outcome = new ChainedByShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
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
                new ChatSendServer("Bob sera lié par les chaînes de la vérité.", -1),
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
                new ChatBroadcast("Bob est maintenant béni.", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Bob est maintenant béni.", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Bob était le robot et a été éliminé par Chasseur.", -1, PowerEffectAudience.All),
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
                new ChatBroadcast("Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.", -1, PowerEffectAudience.Specific(0)),
                RequestCharacterRefresh.Instance,
            }, outcome.Effects);
        }

        [Test]
        public void CursedVision_NonChosen_CorruptsRevealsCardsChatsBoth()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.anomaly; roster.Pseudos[1] = "Bob";
            var outcome = new CursedVisionDecision { CardEffectId = 2 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new AddCardEffect(2, 1, true),
                new ChatLocal("Bob n'est pas un élu.", -1),
                new CorruptPlayer(0),
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
            }, outcome.Effects);
        }

        [Test]
        public void CursedVision_Chosen_FlipsCardAndVerdict()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.chosen; roster.Pseudos[1] = "Alice";
            var outcome = new CursedVisionDecision { CardEffectId = 2 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster));

            Assert.AreEqual(new AddCardEffect(2, 1, false), outcome.Effects[3]);
            Assert.AreEqual(new ChatLocal("Alice est un élu.", -1), outcome.Effects[4]);
        }

        [Test]
        public void Omniscience_TargetsStoresRevealsRefreshes()
        {
            var outcome = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 5));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 5),
                new StoreHackTarget(5),
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
            var outcome = new LackOfAffectionDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, isTrueLocalTarget: true, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(0, RevealField.RoleRevealed, RevealVisibility.Personal, 1, false),
                new ChatLocal("Marginal est venu(e) vous voir...", -1),
            }, outcome.Effects);
        }

        [Test]
        public void LackOfAffection_NonChosen_NotTrueLocal_Empty()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            roster.Factions[1] = Characters.FactionType.anomaly;
            var outcome = new LackOfAffectionDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, isTrueLocalTarget: false, roster: roster));
            Assert.AreEqual(0, outcome.Effects.Count);
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

        // ---- Query/state-port powers -----------------------------------------------------
        private sealed class FakeState : IPowerStateResolver
        {
            private readonly System.Collections.Generic.Dictionary<System.Type, object> _map = new();
            public FakeState With<T>(T impl) where T : class { _map[typeof(T)] = impl; return this; }
            public TPort Resolve<TPort>() where TPort : class => _map.TryGetValue(typeof(TPort), out var v) ? (TPort)v : null;
        }
        private sealed class FakeInk : IInkChatState { public int ChatId { get; set; } }
        private sealed class FakeClandestine : IClandestineReport { public bool HasCharacters { get; set; } public string RoleLabel { get; set; } public int DistinctTargetingCount { get; set; } }
        private sealed class FakeVision : IVisionGuesses { public System.Collections.Generic.IReadOnlyList<VisionGuess> Guesses { get; set; } }
        private sealed class FakeCards : ICardsShufflingGuess { public bool IsCorrect { get; set; } public string ClickedPseudo { get; set; } public string GuessRoleName { get; set; } public System.Collections.Generic.IReadOnlyList<string> TargetedRoleNames { get; set; } = new string[0]; }

        [Test]
        public void BoundByInk_TargetsDiscoversRegisters()
        {
            var state = new FakeState().With<IInkChatState>(new FakeInk { ChatId = 515100 });
            var outcome = new BoundByInkDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new DiscoverChat(515100, "Lié par l'encre", PowerEffectAudience.Specific(1)),
                new RegisterInkTarget(1),
            }, outcome.Effects);
        }

        [Test]
        public void Clandestine_NoChars_ZeroWithPeriod()
        {
            var state = new FakeState().With<IClandestineReport>(new FakeClandestine { HasCharacters = false, RoleLabel = "Robot", DistinctTargetingCount = 0 });
            var outcome = new ClandestineObservationDecision().Decide(new PowerContext(ownerSlot: 0, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new ChatBroadcast("Total de personne qui ont ciblé le rôle \"Robot\": 0.", -1, PowerEffectAudience.Specific(0)),
            }, outcome.Effects);
        }

        [Test]
        public void Clandestine_HasChars_CountNoPeriod()
        {
            var state = new FakeState().With<IClandestineReport>(new FakeClandestine { HasCharacters = true, RoleLabel = "Robot Mécanique", DistinctTargetingCount = 3 });
            var outcome = new ClandestineObservationDecision().Decide(new PowerContext(ownerSlot: 0, state: state));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new ChatBroadcast("Total de personne qui ont ciblé le rôle \"Robot Mécanique\": 3", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Bob est l'un de ces personnages.", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Aucun personnage n'a été trouvé.", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Vous avez correctement deviné que Bob est Sorcier.", -1, PowerEffectAudience.Specific(0)),
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
                new ChatBroadcast("Votre supposition était incorrecte, Bob n'est pas Sorcier.\nLe role Sorcier a ciblé ces rôles:\n- Robot\n- Élu", -1, PowerEffectAudience.Specific(0)),
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
    }
}
