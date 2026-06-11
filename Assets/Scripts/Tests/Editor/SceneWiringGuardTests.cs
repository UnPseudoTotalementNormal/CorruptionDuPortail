using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameLogic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Tests.Editor
{
    /// <summary>
    /// Story 6.2 / Epic 6 (D0) — guard #2 of the two-guards pillar
    /// (refactor-architecture-despaghetti.md §5). Guard #1 (<see cref="DiSeamNoLocatorGuardTests"/>)
    /// checks the TYPE (no locator left in source); this guard checks the BINDING: every injected
    /// [SerializeField] dependency of every migrated consumer is actually wired in GameScene (or a
    /// prefab), so a forgotten drag-drop fails CI deterministically instead of NRE-ing mid-game.
    ///
    /// MECHANISM CHOICE (recorded per story task 2): a field counts as an injected dependency iff
    /// it is serialized (public without [NonSerialized], or [SerializeField], walking base types)
    /// AND its type is, or derives from, a member of
    /// <see cref="DiSeamMigratedConsumers.InjectedManagerTypes"/>. Zero per-field maintenance:
    /// presentation refs (LightManager.mainLight : Light) are excluded automatically while
    /// LightManager.gameManager is covered.
    ///
    /// EDITOR-STATE CONTRACT: the guard EditMode-loads GameScene (no PlayMode boot) and restores
    /// the previous scene setup afterwards. It requires a SAVED editor state — if a titled scene
    /// has unsaved changes the guard fails with a clear message instead of silently discarding
    /// user work. (A dirty UNTITLED scratch scene cannot be told apart from earlier EditMode tests
    /// leaking GameObjects, so it is tolerated and discarded.)
    /// </summary>
    [Category("SceneWiringGuard")]
    public class SceneWiringGuardTests
    {
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";

        private SceneSetup[] _previousSceneSetup;
        private bool _gameSceneOpened;

        [OneTimeSetUp]
        public void OpenGameScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                {
                    Assert.Fail(
                        $"SceneWiringGuard requires a saved editor state, but '{scene.path}' has unsaved changes. " +
                        "Save (or discard) them and re-run — the guard must load GameScene without destroying work.");
                }
            }

            // Untitled scenes carry an empty path and cannot be restored by RestoreSceneManagerSetup.
            _previousSceneSetup = EditorSceneManager.GetSceneManagerSetup()
                .Where(setup => !string.IsNullOrEmpty(setup.path))
                .ToArray();

            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            _gameSceneOpened = true;
        }

        [OneTimeTearDown]
        public void RestorePreviousScenes()
        {
            // Never leave GameScene open as a side effect of the test run.
            if (!_gameSceneOpened)
                return;

            if (_previousSceneSetup != null && _previousSceneSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(_previousSceneSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        [Test]
        public void MigratedConsumers_HaveAllInjectedFieldsWired()
        {
            foreach (Type consumer in DiSeamMigratedConsumers.All)
            {
                Component[] instances = FindSceneInstances(consumer);
                if (instances.Length == 0)
                    instances = FindPrefabInstances(consumer);

                Assert.IsNotEmpty(instances,
                    $"No instance of migrated consumer {consumer.Name} found in {GameScenePath} nor in any prefab — " +
                    "a migrated consumer must exist somewhere, else the curated set is stale.");

                foreach (Component instance in instances)
                {
                    string[] unwired = FindUnwiredInjectedFields(instance);
                    CollectionAssert.IsEmpty(unwired,
                        $"{consumer.Name} on '{GetHierarchyPath(instance)}' has unwired injected dependenc(y/ies) " +
                        $"[{string.Join(", ", unwired)}] — wire it in the composition root (GameScene/prefab), " +
                        "see refactor-architecture-despaghetti.md §3 lane A.");
                }
            }
        }

        [Test]
        public void Guard_Bites_OnSyntheticUnwiredField()
        {
            // Proves the detection mechanism actually fires (story task 4, unit-level) — and ONLY
            // on injected manager fields: the presentation ref and the value field must stay out.
            var go = new GameObject("SceneWiringGuard_Synthetic") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var synthetic = go.AddComponent<SyntheticUnwiredConsumer>();

                string[] unwired = FindUnwiredInjectedFields(synthetic);

                CollectionAssert.Contains(unwired, "gameManager",
                    "The guard failed to flag a null injected manager field — the mechanism is broken.");
                Assert.AreEqual(1, unwired.Length,
                    "The guard must flag ONLY injected manager fields — it also flagged: " +
                    $"[{string.Join(", ", unwired)}].");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Guard_Bites_WhenRealConsumerFieldIsCleared_InMemory()
        {
            // Integration-level bite (story task 4): clear LightManager.gameManager on the real
            // GameScene instance IN MEMORY (raw reflection, never saved), watch the guard flag it,
            // restore.
            var lightManager = (LightManager)Object.FindFirstObjectByType(typeof(LightManager), FindObjectsInactive.Include);
            Assert.IsNotNull(lightManager, $"LightManager not found in {GameScenePath}.");

            FieldInfo field = typeof(LightManager).GetField("gameManager", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "LightManager.gameManager field not found — was it renamed?");

            object original = field.GetValue(lightManager);
            try
            {
                field.SetValue(lightManager, null);
                CollectionAssert.Contains(FindUnwiredInjectedFields(lightManager), "gameManager",
                    "The guard failed to flag the deliberately-cleared LightManager.gameManager — the mechanism is broken.");
            }
            finally
            {
                field.SetValue(lightManager, original);
            }

            CollectionAssert.IsEmpty(FindUnwiredInjectedFields(lightManager),
                "LightManager must be fully wired again once the bite test restored the field.");
        }

        private static Component[] FindSceneInstances(Type consumer)
        {
            return Object.FindObjectsByType(consumer, FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Cast<Component>()
                .ToArray();
        }

        private static Component[] FindPrefabInstances(Type consumer)
        {
            return AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null)
                .SelectMany(prefab => prefab.GetComponentsInChildren(consumer, true))
                .ToArray();
        }

        // Pure field-check core, factored out so the bite tests can feed synthetic and
        // in-memory-mutated instances. Returns the names of the unwired injected dependencies.
        private static string[] FindUnwiredInjectedFields(Component component)
        {
            var unwired = new List<string>();
            foreach (FieldInfo field in GetSerializedFields(component.GetType()))
            {
                if (!IsInjectedManagerType(field.FieldType))
                    continue;

                // Unity null: a destroyed/"missing" reference must count as unwired, so compare
                // through the UnityEngine.Object == overload, never plain ReferenceEquals.
                var value = field.GetValue(component) as Object;
                if (value == null)
                    unwired.Add(field.Name);
            }

            return unwired.ToArray();
        }

        private static IEnumerable<FieldInfo> GetSerializedFields(Type type)
        {
            // DeclaredOnly per level, climbing BaseType: private fields of a base class are not
            // returned by a derived-type GetFields call.
            for (Type level = type; level != null && level != typeof(MonoBehaviour) && level != typeof(object); level = level.BaseType)
            {
                FieldInfo[] fields = level.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (FieldInfo field in fields)
                {
                    if (field.IsInitOnly)
                        continue; // Unity never serializes readonly fields.

                    bool serialized = field.IsPublic
                        ? !field.IsDefined(typeof(NonSerializedAttribute), false)
                        : field.IsDefined(typeof(SerializeField), false);

                    if (serialized)
                        yield return field;
                }
            }
        }

        private static bool IsInjectedManagerType(Type fieldType)
        {
            foreach (Type managerType in DiSeamMigratedConsumers.InjectedManagerTypes)
            {
                if (managerType.IsAssignableFrom(fieldType))
                    return true;
            }

            return false;
        }

        private static string GetHierarchyPath(Component component)
        {
            Transform node = component.transform;
            string path = node.name;
            while (node.parent != null)
            {
                node = node.parent;
                path = node.name + "/" + path;
            }

            return path;
        }

        // Deliberately-unwired synthetic consumer for the unit-level bite: gameManager must be
        // flagged; the presentation ref and the value field must not.
#pragma warning disable CS0169, CS0649 // fields intentionally unassigned, read via reflection only
        private sealed class SyntheticUnwiredConsumer : MonoBehaviour
        {
            [SerializeField] private GameManager gameManager;
            [SerializeField] private Light presentationRef;
            [SerializeField] private float tunable;
        }
#pragma warning restore CS0169, CS0649
    }
}
