using System.Collections.Generic;
using System.Linq;
using Avatars;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avatars.Cosmetics.EditorTools
{
    /// <summary>
    /// Plugs the artist's cat animations (Cat_Animations_v2.fbx, exported by tools/art/cats/export_animations.py) into
    /// the game, reproducibly: clip import settings, the Cat_Avatar Animator (rebuilt from scratch, deterministic), the
    /// emote wheel's entries, and the GameScene wiring of the tablet animation.
    ///
    /// Animator: Idle plays the artist's Idle (AvatarIdleMotion adds its procedural motion on top). Emotes by EmoteId,
    /// held by the Emoting bool: 1 Coucou (Wave), 2 Pleurer (Crying, with tears), 3 Accuser (Accusing then Accused
    /// Idle), 4 Pose ardente (Fiery Idle Test). Chained bool (server, from the seat's character): Getting Chained then
    /// Chained Idle, an emote tried while chained = Chained Struggle. Tablet bool (owner): Pulls Tablet Out, Look at
    /// Tablet Idle, Putting Table Away. Chained wins over everything. Intentions: the artist's asset tracker
    /// (_bmad-output/art-references/cats/Corrupted Deadlines.pdf).
    /// </summary>
    public static class CatAnimationsAssetBuilder
    {
        private const string AnimationsPath = "Assets/Art/Models/Cat_Animations_v2.fbx";
        private const string ControllerPath = "Assets/Animators/Cat_Avatar.controller";
        private const string EmoteSetPath = "Assets/Data/Emotes/EmoteSet.asset";
        private const string CatPrefabPath = "Assets/Prefabs/Avatars/Cat_Avatar.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";

        private static readonly HashSet<string> Loops = new()
            { "Idle", "Wave", "Crying", "Accused Idle", "Chained Idle", "Look at Tablet Idle", "Fiery Idle Test" };

        [MenuItem("Tools/Cat Cosmetics/Rebuild animations")]
        public static void RebuildAnimations()
        {
            ImportClips();
            Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(AnimationsPath)
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            BuildController(clips);
            BuildEmoteSet();
            ResaveCatPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CatAnimations] Animations rebuilt ({clips.Count} clips).");
        }

        [MenuItem("Tools/Cat Cosmetics/Wire game scene")]
        public static void WireGameScene()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            AvatarManager manager = Object.FindAnyObjectByType<AvatarManager>(FindObjectsInactive.Include);
            var smartphone = Object.FindAnyObjectByType<Smartphone.SmartphoneController>(FindObjectsInactive.Include);
            var so = new SerializedObject(manager);
            so.FindProperty("_smartphone").objectReferenceValue = smartphone;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CatAnimations] GameScene wired (smartphone {(smartphone != null ? smartphone.name : "MISSING")}).");
        }

        private static void ImportClips()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AnimationsPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            // A skeleton-only FBX has a single root: without this Unity drops the "BW Rig" node, every curve binds to
            // "Root/..." (not the cat's "BW Rig/Root/...") and the rig's own curve moves the whole cat.
            importer.preserveHierarchy = true;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = clip.takeName.Split('|').Last(); // "BW Rig|BW Rig|Idle" -> "Idle"
                clip.loopTime = Loops.Contains(clip.name);
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static void BuildController(Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in sm.states.ToArray()) sm.RemoveState(child.state);
            foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);

            Param(controller, "EmoteId", AnimatorControllerParameterType.Int);
            Param(controller, "Emote", AnimatorControllerParameterType.Trigger);
            Param(controller, "Emoting", AnimatorControllerParameterType.Bool);
            Param(controller, "Chained", AnimatorControllerParameterType.Bool);
            foreach (AnimatorControllerParameter old in controller.parameters.Where(p => p.name == "Struggle").ToArray())
                controller.RemoveParameter(old); // was a server timer; the artist wants the struggle on an emote
            Param(controller, "Tablet", AnimatorControllerParameterType.Bool);

            AnimatorState State(string name, string clip, int x, int y, string tag = "")
            {
                AnimatorState state = sm.AddState(name, new Vector3(x, y, 0f));
                state.motion = clips.TryGetValue(clip, out AnimationClip c) ? c : null;
                if (state.motion == null) Debug.LogError($"[CatAnimations] clip '{clip}' missing");
                state.writeDefaultValues = true;
                state.tag = tag;
                return state;
            }

            AnimatorState idle = State("Idle", "Idle", 300, 0);
            sm.defaultState = idle;
            AnimatorState wave = State("Wave", "Wave", 600, -200);
            AnimatorState waveLoop = State("WaveLoop", "Wave", 600, -120);
            AnimatorState crying = State("Crying", "Crying", 600, -40, "Crying");
            // "Angry": the eyes frown (CatFrown, the artist's note on Accusing: "furrowed brows").
            AnimatorState accusing = State("Accusing", "Accusing", 600, 40, "Angry");
            AnimatorState accused = State("Accused Idle", "Accused Idle", 850, 40, "Angry");
            AnimatorState fiery = State("Fiery Idle", "Fiery Idle Test", 600, 120);
            AnimatorState gettingChained = State("Getting Chained", "Getting Chained", 0, 250);
            AnimatorState gettingChained2 = State("Getting Chained 2", "Getting Chained 2", 0, 330);
            AnimatorState chainedIdle = State("Chained Idle", "Chained Idle", 0, 410);
            AnimatorState struggle = State("Chained Struggle", "Chained Struggle", 250, 410);
            AnimatorState tabletOut = State("Pulls Tablet Out", "Pulls Tablet Out", 600, 250);
            AnimatorState tabletIdle = State("Look at Tablet Idle", "Look at Tablet Idle", 600, 330);
            AnimatorState tabletAway = State("Putting Table Away", "Putting Table Away", 600, 410);

            // Chained first: it wins over emotes and the tablet (transitions are checked in order).
            foreach (AnimatorState from in new[] { idle, wave, waveLoop, crying, accusing, accused, fiery, tabletOut, tabletIdle, tabletAway })
                Go(from, gettingChained, null, ("Chained", AnimatorConditionMode.If, 0f));
            Go(gettingChained, gettingChained2, 1f);
            Go(gettingChained2, chainedIdle, 1f);
            // The artist's tracker: chained = the default pose; an emote tried while chained replays the struggle.
            Go(chainedIdle, struggle, null, ("Emoting", AnimatorConditionMode.If, 0f));
            Go(chainedIdle, struggle, null, ("Emote", AnimatorConditionMode.If, 0f));
            Go(struggle, chainedIdle, 1f);
            foreach (AnimatorState from in new[] { gettingChained, gettingChained2, chainedIdle, struggle })
                Go(from, idle, null, ("Chained", AnimatorConditionMode.IfNot, 0f));

            // Emotes: the one-shot coucou (Emote trigger), the held ones (Emoting bool), by EmoteId.
            Go(idle, wave, null, ("Emote", AnimatorConditionMode.If, 0f), ("EmoteId", AnimatorConditionMode.Equals, 1f));
            Go(wave, idle, 0.9f);
            HeldEmote(idle, waveLoop, waveLoop, 1);
            HeldEmote(idle, crying, crying, 2);
            HeldEmote(idle, accusing, accused, 3);
            Go(accusing, accused, 1f);
            Go(accusing, idle, null, ("Emoting", AnimatorConditionMode.IfNot, 0f));
            HeldEmote(idle, fiery, fiery, 4);

            // Tablet.
            Go(idle, tabletOut, null, ("Tablet", AnimatorConditionMode.If, 0f));
            Go(tabletOut, tabletIdle, 1f);
            Go(tabletIdle, tabletAway, null, ("Tablet", AnimatorConditionMode.IfNot, 0f));
            Go(tabletAway, idle, 1f);

            EditorUtility.SetDirty(controller);
        }

        // Idle -> enter (Emoting + EmoteId), leave (from the held state) when Emoting goes false.
        private static void HeldEmote(AnimatorState idle, AnimatorState enter, AnimatorState held, int emoteId)
        {
            Go(idle, enter, null, ("Emoting", AnimatorConditionMode.If, 0f), ("EmoteId", AnimatorConditionMode.Equals, emoteId));
            Go(held, idle, null, ("Emoting", AnimatorConditionMode.IfNot, 0f));
        }

        // A transition: on exit time (exitTime set, no condition) or on conditions (no exit time).
        private static void Go(AnimatorState from, AnimatorState to, float? exitTime,
            params (string parameter, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = exitTime.HasValue;
            t.exitTime = exitTime ?? 0f;
            t.duration = exitTime.HasValue ? 0.1f : 0.15f;
            t.hasFixedDuration = true;
            foreach ((string parameter, AnimatorConditionMode mode, float threshold) in conditions)
                t.AddCondition(mode, threshold, parameter);
        }

        private static void Go(AnimatorState from, AnimatorState to, float exitTime) => Go(from, to, (float?)exitTime);

        private static void Param(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (controller.parameters.All(p => p.name != name)) controller.AddParameter(name, type);
        }

        private static void BuildEmoteSet()
        {
            var set = AssetDatabase.LoadAssetAtPath<EmoteSet>(EmoteSetPath);
            var so = new SerializedObject(set);
            SerializedProperty list = so.FindProperty("_emotes");
            var emotes = new (string name, int id)[] { ("Coucou", 1), ("Pleurer", 2), ("Accuser", 3), ("Pose ardente", 4) };
            list.arraySize = emotes.Length;
            for (int i = 0; i < emotes.Length; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = emotes[i].name;
                e.FindPropertyRelative("animatorEmoteId").intValue = emotes[i].id;
                e.FindPropertyRelative("loops").boolValue = true;
                e.FindPropertyRelative("oneShotSeconds").floatValue = 2f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Saving the prefab refreshes its NetworkAnimator's parameter list (Chained / Struggle / Tablet synced).
        private static void ResaveCatPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CatPrefabPath);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
