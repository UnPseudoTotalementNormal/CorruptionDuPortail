using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tests.PlayMode.Infra
{
    /// <summary>
    /// Single source of truth for wiping production static state between PlayMode tests. Domain reload is OFF in
    /// this project, so any surviving singleton `.instance` or static collection leaks into the NEXT test — the
    /// #1 cause of order-dependent, non-reproducible flakes. Every PlayMode fixture teardown should call
    /// <see cref="ResetAll"/>.
    ///
    /// Types are resolved by SIMPLE NAME across loaded assemblies (namespace-agnostic, survives a type move),
    /// and this inventory MUST stay in sync with the singletons tracked by StaticSingletonCensusGuardTests.
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

        public static Type ResolveType(string simpleName) =>
            AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(t => t.Name == simpleName);

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
