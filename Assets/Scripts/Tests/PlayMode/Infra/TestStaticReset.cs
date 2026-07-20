using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tests.PlayMode.Infra
{
    /// <summary>
    /// Single source of truth for wiping production static state between PlayMode tests. Domain reload is OFF in
    /// this project, so any surviving singleton `.instance` or static collection leaks into the NEXT test — the
    /// #1 cause of order-dependent, non-reproducible flakes.
    ///
    /// STATUS 2026-07-20 — NOT WIRED. <see cref="ResetAll"/> has NO caller outside its own gate
    /// (FixtureResetTests). Do not read this class as an active protection: nothing in the suite is isolated from
    /// static leaks today. MultiClientGameFixture.TearDown declines to call it on purpose (see the NOTE there) —
    /// a measured 3 tests out of 244 currently depend on a leaked singleton and fail the moment it is wired.
    /// Either finish the job (instrument those 3, wire their singletons, then call ResetAll from the fixture
    /// teardowns) or delete this file and FixtureResetTests. Plan: deferred-work.md, "audit des tests PlayMode
    /// post-PR#91".
    ///
    /// Types are resolved by SIMPLE NAME within the production (`Game`) assembly (namespace-agnostic, survives
    /// a type move), and this inventory MUST stay in sync with the singletons tracked by StaticSingletonCensusGuardTests.
    /// Spec + rationale: _bmad-output/implementation-artifacts/test-infra-foundation.md.
    /// </summary>
    public static class TestStaticReset
    {
        // 21 production singletons exposing a static `instance` (field or auto-property; SelectionFlowService
        // backs it with a private `_instance`).
        public static readonly string[] SingletonTypeNames =
        {
            "GameManager", "CharacterManager", "RoleTargetSystem", "ChainingManager", "ChatManager",
            "PowerManager", "BoardManager", "CardEffectManager", "GameAudioManager", "LobbyPlayerInfoHolder",
            "FocusManager", "SelectionFlowService", "MessageManager", "ArrowManager", "CardPickerManager",
            "TooltipManager", "NoteManager", "BoardCameraManager", "InputManager", "LobbyManager", "GameAssetHolder",
        };

        // Static collections that grow across tests and must be cleared. (simpleTypeName, fieldName).
        private static readonly (string typeName, string field)[] LeakyCollections =
        {
            ("PBoundByInk", "usedBoundByInkIds"),
            ("DontDestroyOnLoadComponent", "existingIds"),
        };

        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Null every production singleton, clear the leaky static collections, reset the Liveness bridge.</summary>
        public static void ResetAll()
        {
            foreach (string name in SingletonTypeNames)
            {
                Type t = ResolveType(name);
                if (t != null)
                {
                    SetStaticInstance(t, null);
                }
            }

            foreach ((string typeName, string field) in LeakyCollections)
            {
                ClearStaticCollection(typeName, field);
            }

            // The Liveness bridge has its own dedicated reset for its per-NetworkManager registries.
            Type bridge = ResolveType("LivenessNetworkBridge");
            bridge?.GetMethod("ResetSessionStatics", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        // Pinned to the assembly that declares the production managers (asmdef `Game`). A simple-name
        // search across ALL loaded assemblies is collision-prone (`GameManager` / `InputManager` are
        // ubiquitous in packages and samples), and a foreign match makes ResetAll silently no-op on the
        // real singleton while the gate stays green — the exact failure mode this class exists to prevent.
        private static readonly Assembly ProductionAssembly = typeof(Characters.CharacterManager).Assembly;

        public static Type ResolveType(string simpleName) =>
            SafeTypes(ProductionAssembly).FirstOrDefault(t => t.Name == simpleName);

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        public static object GetStaticInstance(Type t)
        {
            PropertyInfo prop = t.GetProperty("instance", Flags);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(null);
            }
            FieldInfo field = t.GetField("instance", Flags) ?? t.GetField("_instance", Flags);
            return field?.GetValue(null);
        }

        public static void SetStaticInstance(Type t, object value)
        {
            PropertyInfo prop = t.GetProperty("instance", Flags);
            MethodInfo setter = prop?.GetSetMethod(true);
            if (setter != null)
            {
                setter.Invoke(null, new[] { value });
                return;
            }
            FieldInfo field = t.GetField("instance", Flags)
                              ?? t.GetField("_instance", Flags)
                              ?? t.GetField("<instance>k__BackingField", Flags);
            field?.SetValue(null, value);
        }

        public static object GetStaticCollection(string typeName, string field)
        {
            Type t = ResolveType(typeName);
            return t?.GetField(field, Flags)?.GetValue(null);
        }

        private static void ClearStaticCollection(string typeName, string field)
        {
            object coll = GetStaticCollection(typeName, field);
            coll?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(coll, null);
        }
    }
}
