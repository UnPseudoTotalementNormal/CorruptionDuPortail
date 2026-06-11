using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameLogic;
using NUnit.Framework;
using Unity.Collections;

namespace Tests.Editor
{
    /// <summary>
    /// Story 5.1 — DEFENSIVE wire-format guard over the reflection state-dispatch.
    ///
    /// <see cref="GameManager.DoStateMethodRpc"/> / <c>CallStateMethodRpc</c> serialize a target
    /// <see cref="GameState"/> by its <c>GetType().FullName</c> into a <see cref="FixedString64Bytes"/>
    /// and resolve it on the receiver by matching that string. So the wire format is the SET of
    /// GameState <c>FullName</c>s. Two silent-breakage risks this pins:
    ///   1. a rename/move of a GameState class changes its FullName → a remote receiver fails to
    ///      resolve the state (the `Assert.IsNotNull` fires only at runtime, on the wire);
    ///   2. a FullName whose UTF8 length exceeds <see cref="FixedString64Bytes"/> capacity is
    ///      silently truncated → mis-resolution.
    ///
    /// This is a purely defensive EditMode golden — NO refonte of the dispatch (the full
    /// reflection-dispatch rewrite is a separate protocol effort, explicitly out of scope). Any
    /// add/remove/rename/move of a production GameState, or any over-long FullName, fails a test
    /// here instead of silently changing the wire format.
    /// </summary>
    [Category("WireFormat")]
    public class StateDispatchWireFormatGuardTests
    {
        // Versioned snapshot of the wire-format state table (FullNames, sorted ordinal).
        // Changing this set is a WIRE-FORMAT CHANGE — update deliberately, never to "make it pass".
        private static readonly string[] FrozenStateFullNames =
        {
            "GameLogic.GameStates.AwakeningRecapState",
            "GameLogic.GameStates.AwakeningState",
            "GameLogic.GameStates.ChainingState",
            "GameLogic.GameStates.GameEndingState",
            "GameLogic.GameStates.GameIntroductionState",
            "GameLogic.GameStates.LobbyState",
            "GameLogic.GameStates.RoleAttributionState",
            "GameLogic.GameStates.TakeDownThePortalState",
            "GameLogic.GameStates.VictoryConditionCheckState",
            "GameLogic.GameStates.VoteRecapState",
            "GameLogic.GameStates.VoteState",
        };

        private static List<Type> ProductionGameStateTypes()
        {
            // The production Game assembly (GameManager lives there) — test DummyGameStates live in
            // the test assemblies and are excluded automatically.
            var assembly = typeof(GameManager).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            return types
                .Where(t => typeof(GameState).IsAssignableFrom(t) && !t.IsAbstract)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
        }

        [Test]
        public void StateTable_MatchesFrozenWireFormat()
        {
            var actual = ProductionGameStateTypes().Select(t => t.FullName).ToArray();

            CollectionAssert.AreEqual(FrozenStateFullNames, actual,
                "The GameState wire-format table changed. A renamed/moved/added/removed GameState " +
                "changes what is sent over the dispatch RPC and silently breaks remote resolution. " +
                "If this is intentional, update FrozenStateFullNames deliberately.\nObserved: " +
                string.Join(", ", actual));
        }

        [Test]
        public void EveryStateFullName_FitsFixedString64Bytes()
        {
            int cap = FixedString64Bytes.UTF8MaxLengthInBytes;
            foreach (var type in ProductionGameStateTypes())
            {
                int utf8Len = Encoding.UTF8.GetByteCount(type.FullName);
                Assert.LessOrEqual(utf8Len, cap,
                    $"GameState '{type.FullName}' serializes to {utf8Len} UTF8 bytes, over the " +
                    $"FixedString64Bytes capacity ({cap}). The dispatch would truncate it and the " +
                    "receiver would fail to resolve the state. Shorten the namespace/class name.");
            }
        }

        [Test]
        public void FrozenTable_IsInternallyConsistent()
        {
            // Guard the golden itself: no dup, sorted ordinal (so diffs are stable).
            CollectionAssert.AllItemsAreUnique(FrozenStateFullNames);
            CollectionAssert.AreEqual(
                FrozenStateFullNames.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                FrozenStateFullNames,
                "FrozenStateFullNames must stay sorted ordinal for stable diffs.");
        }
    }
}
