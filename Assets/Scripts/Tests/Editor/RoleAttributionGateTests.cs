using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode adapter tests for the start-gate integration seam: <see cref="RoleAttributionState"/>.
    /// ValidateComposition joins each role's authored faction with its max/forced and runs the shared pure
    /// <see cref="CompositionValidator"/> — no NGO required. Proves the faction actually flows through
    /// BuildCompositionSnapshot (what the Domain-only tests cannot reach) and that the wired vs null
    /// CompositionRuleSet toggles the faction rules on/off.
    /// </summary>
    [Category("RoleAttributionGate")]
    public class RoleAttributionGateTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }
            _created.Clear();
        }

        private RoleAttributionState BuildState(bool withRules, params (FactionType faction, RoleID id, int max, int forced)[] roles)
        {
            var state = ScriptableObject.CreateInstance<RoleAttributionState>();
            _created.Add(state);

            if (withRules)
            {
                var rules = ScriptableObject.CreateInstance<CompositionRuleSet>(); // default {anomaly:1, chosen:1}
                _created.Add(rules);
                ReflectionHelper.SetPrivateField(state, "_compositionRules", rules);
            }

            foreach ((FactionType faction, RoleID id, int max, int forced) r in roles)
            {
                var data = ScriptableObject.CreateInstance<RoleDataObject>();
                _created.Add(data);
                data.role = new Role { roleID = r.id, roleName = r.id.ToString(), factionType = r.faction };
                state.roleAttributionDictionary.Add(data, new RoleAttributionSetting { max = r.max, forced = r.forced });
            }

            return state;
        }

        [Test]
        public void ValidateComposition_ForcedMonopolisesReals_RejectsThroughTheAdapter()
        {
            // Poyo bug 1 via the real adapter: anomaly max5/forced5 + chosen max5/forced0, 5 players → 6 guaranteed.
            var state = BuildState(true,
                (FactionType.anomaly, RoleID.Robot, 5, 5),
                (FactionType.chosen, RoleID.Oracle, 5, 0));

            CompositionValidation v = state.ValidateComposition(5);

            Assert.IsFalse(v.IsValid, "the chosen top-up should push guaranteed reals to 6 > 5 players");
        }

        [Test]
        public void ValidateComposition_BothFactionsFakeable_AcceptsThroughTheAdapter()
        {
            // Poyo bug 2: anomaly max5/forced0 + chosen max5/forced0, 5 players → accepted (distributor guarantees).
            var state = BuildState(true,
                (FactionType.anomaly, RoleID.Robot, 5, 0),
                (FactionType.chosen, RoleID.Oracle, 5, 0));

            CompositionValidation v = state.ValidateComposition(5);

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }

        [Test]
        public void ValidateComposition_MissingChosenFaction_RejectsWithFactionReason()
        {
            // No chosen role in the pool → the chosen minimum is infeasible; the failure names the Élu faction.
            var state = BuildState(true,
                (FactionType.anomaly, RoleID.Robot, 5, 0),
                (FactionType.anomaly, RoleID.Oracle, 2, 0));

            CompositionValidation v = state.ValidateComposition(3);

            Assert.IsFalse(v.IsValid);
            Assert.IsTrue(v.Failures.Any(f => f.OffendingFaction == FactionType.chosen), "expected a chosen-faction failure");
        }

        [Test]
        public void ValidateComposition_NoRuleSet_OnlyScalarRules_AllowsASingleFaction()
        {
            // Unwired ruleset (headless harness): faction rules OFF → a single-faction pool is fine if it covers players.
            var state = BuildState(false,
                (FactionType.anomaly, RoleID.Robot, 5, 0));

            CompositionValidation v = state.ValidateComposition(3);

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }
    }
}
