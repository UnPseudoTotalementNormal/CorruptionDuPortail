using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// DETERMINISM FREEZE PROOF (Story 1.1 §3b A — role-pool iteration order).
    ///
    /// The only production change in Story 1.1: <see cref="RoleAttributionState"/>
    /// now indexes its random role selection into a FROZEN, authored
    /// <c>SerializedDictionary</c> order (filtered to still-available roles) instead
    /// of <c>Dictionary.Keys</c>, whose order is implementation-defined and can drift
    /// after asset reload / removals.
    ///
    /// This is behavior-preserving — the SerializedDictionary already enumerates in
    /// authored order, which is the de-facto order the old code produced — but that
    /// must be PROVEN, not assumed. These tests are the only safety net for the
    /// freeze (no golden on role attribution exists until Story 3.2).
    ///
    /// Pure over the dictionary → EditMode, no NGO boot. Internals are reached via
    /// [assembly: InternalsVisibleTo("Tests.Editor")] (AssemblyInfo.cs), never a
    /// public widening or a TEST_ backdoor.
    /// </summary>
    [Category("Determinism")]
    public class RolePoolOrderingTests
    {
        private readonly List<Object> _created = new();

        private RoleDataObject NewRole()
        {
            var _role = ScriptableObject.CreateInstance<RoleDataObject>();
            _created.Add(_role);
            return _role;
        }

        private RoleAttributionState NewState()
        {
            var _state = ScriptableObject.CreateInstance<RoleAttributionState>();
            _created.Add(_state);
            return _state;
        }

        [TearDown]
        public void TearDown()
        {
            // Throwaway SOs: never persisted, destroy immediately (EditMode).
            foreach (var _object in _created)
            {
                if (_object != null)
                {
                    Object.DestroyImmediate(_object);
                }
            }
            _created.Clear();
        }

        [Test]
        public void FrozenRolePoolOrder_EqualsAuthoredOrder_AndIsStableAcrossCalls()
        {
            var _state = NewState();
            var _roleA = NewRole();
            var _roleB = NewRole();
            var _roleC = NewRole();

            // Authored (serialized) insertion order: A, B, C.
            _state.roleAttributionDictionary.Add(_roleA, new RoleAttributionSetting { roleToAttribute = 1 });
            _state.roleAttributionDictionary.Add(_roleB, new RoleAttributionSetting { roleToAttribute = 1 });
            _state.roleAttributionDictionary.Add(_roleC, new RoleAttributionSetting { roleToAttribute = 1 });

            CollectionAssert.AreEqual(new[] { _roleA, _roleB, _roleC }, _state.GetFrozenRolePoolOrder(),
                "Frozen pool order must equal the authored SerializedDictionary order.");

            // Stable across repeated calls — no per-call reordering.
            CollectionAssert.AreEqual(
                _state.GetFrozenRolePoolOrder().ToList(),
                _state.GetFrozenRolePoolOrder().ToList(),
                "Frozen pool order must be identical on repeated calls.");
        }

        [Test]
        public void FrozenRolePoolOrder_PreservesRelativeOrderOfSurvivors_AfterMiddleRemoval()
        {
            var _state = NewState();
            var _roleA = NewRole();
            var _roleB = NewRole();
            var _roleC = NewRole();

            _state.roleAttributionDictionary.Add(_roleA, new RoleAttributionSetting { roleToAttribute = 1 });
            _state.roleAttributionDictionary.Add(_roleB, new RoleAttributionSetting { roleToAttribute = 1 });
            _state.roleAttributionDictionary.Add(_roleC, new RoleAttributionSetting { roleToAttribute = 1 });

            // Remove the MIDDLE entry — survivors must keep their relative order (A, C).
            // This is exactly what the production selection relies on: it filters the
            // frozen order to still-available roles, so removals never reorder survivors.
            _state.roleAttributionDictionary.Remove(_roleB);

            CollectionAssert.AreEqual(new[] { _roleA, _roleC }, _state.GetFrozenRolePoolOrder(),
                "After removing a middle entry, survivors must preserve relative authored order.");
        }
    }
}
