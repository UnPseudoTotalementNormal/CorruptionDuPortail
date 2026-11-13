using System.Collections.Generic;
using System.Linq;
using Avatars;
using Avatars.Cosmetics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The artist's cat animations as wired in the game: every clip has a state, every emote of the wheel reaches its
    /// state, the chained / tablet animations are reachable, their parameters replicate, and the tears are wired.
    /// </summary>
    [Category("CatCustomization")]
    public class CatAnimationsTests
    {
        private const string ControllerPath = "Assets/Animators/Cat_Avatar.controller";
        private const string AnimationsPath = "Assets/Art/Models/Cat_Animations_v2.fbx";
        private const string EmoteSetPath = "Assets/Data/Emotes/EmoteSet.asset";
        private const string CatPrefabPath = "Assets/Prefabs/Avatars/Cat_Avatar.prefab";

        private static AnimatorStateMachine Machine =>
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath).layers[0].stateMachine;

        private static AnimatorState State(string name) =>
            Machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        [Test]
        public void EveryArtistClip_IsPlayedByAState_AndIdleIsTheArtistsIdle()
        {
            List<AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(AnimationsPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToList();
            Assert.AreEqual(13, clips.Count, "Cat_Animations_v2.fbx should hold the 13 actions of the artist's files");
            var played = new HashSet<Motion>(Machine.states.Select(s => s.state.motion));
            // "Fiery Idle Test" played « Pose ardente », removed from the game (T3, 2026-11-05).
            foreach (AnimationClip clip in clips.Where(c => c.name != "Fiery Idle Test"))
                Assert.IsTrue(played.Contains(clip), $"clip '{clip.name}' is played by no state");
            Assert.AreEqual("Idle", Machine.defaultState.name);
            Assert.AreEqual("Idle", Machine.defaultState.motion.name);
        }

        // Found by the autoplay demo (2026-10-13): a skeleton-only FBX lost its root node on import, so every curve was
        // bound to "Root/..." (not the cat's "BW Rig/Root/...": no bone moved) and the rig's own curve moved the whole cat.
        [Test]
        public void EveryClipCurve_TargetsATransformOfTheCat_AndTheRigDoesNotMove()
        {
            GameObject cat = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            var paths = new HashSet<string>(cat.GetComponentsInChildren<Transform>(true)
                .Select(t => AnimationUtility.CalculateTransformPath(t, cat.transform)));
            Transform rig = cat.transform.Find("BW Rig");
            Assert.IsNotNull(rig, "Cat_Avatar has no BW Rig");
            foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(AnimationsPath).OfType<AnimationClip>()
                         .Where(c => !c.name.StartsWith("__preview")))
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.IsTrue(bindings.Any(b => b.path == "BW Rig/Root/Spine1"), $"{clip.name}: the spine is not animated");
                foreach (EditorCurveBinding binding in bindings)
                {
                    Assert.IsTrue(paths.Contains(binding.path), $"{clip.name}: curve on '{binding.path}', not on the cat");
                    Assert.AreNotEqual("", binding.path, $"{clip.name}: a curve moves the cat's root");
                    if (binding.path != "BW Rig" || !binding.propertyName.StartsWith("m_LocalPosition")) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    float rest = binding.propertyName.EndsWith(".x") ? rig.localPosition.x
                        : binding.propertyName.EndsWith(".y") ? rig.localPosition.y : rig.localPosition.z;
                    Assert.AreEqual(rest, curve.Evaluate(0f), 0.01f, $"{clip.name}: the rig is moved ({binding.propertyName})");
                }
            }
        }

        [Test]
        public void EveryWheelEmote_LeadsFromIdle_ToAStateOfItsOwn()
        {
            var set = AssetDatabase.LoadAssetAtPath<EmoteSet>(EmoteSetPath);
            Assert.GreaterOrEqual(set.Count, 3);
            // « Pose ardente » removed at the team's request (T3, 2026-11-05): neither in the wheel nor in the Animator.
            Assert.IsFalse(set.Emotes.Any(e => e.animatorEmoteId == 4 || e.displayName == "Pose ardente"), "Pose ardente is back in the wheel");
            Assert.IsFalse(Machine.states.Any(s => s.state.name == "Fiery Idle"), "Fiery Idle is back in the Animator");
            foreach (EmoteDefinition emote in set.Emotes)
            {
                StringAssert.DoesNotContain("—", emote.displayName);
                bool reached = State("Idle").transitions.Any(t =>
                    t.conditions.Any(c => c.parameter == "EmoteId" && c.mode == AnimatorConditionMode.Equals
                                          && Mathf.Approximately(c.threshold, emote.animatorEmoteId))
                    && t.conditions.Any(c => c.parameter == (emote.loops ? "Emoting" : "Emote")));
                Assert.IsTrue(reached, $"emote '{emote.displayName}' ({emote.animatorEmoteId}) reaches no state from Idle");
            }
            Assert.AreEqual("Crying", State("Crying").tag, "the tears follow the Crying tag");
            Assert.AreEqual("Angry", State("Accusing").tag, "the frown follows the Angry tag");
        }

        [Test]
        public void ChainedAndTablet_AreReachable_AndChainedWinsOverEmotes()
        {
            foreach (string from in new[] { "Idle", "WaveLoop", "Crying", "Accused Idle", "Look at Tablet Idle" })
            {
                AnimatorStateTransition first = State(from).transitions.First();
                Assert.AreEqual("Getting Chained", first.destinationState.name, $"{from}: chained must be checked first");
                Assert.IsTrue(first.conditions.Any(c => c.parameter == "Chained" && c.mode == AnimatorConditionMode.If));
            }
            // T3 (2026-11-05): a chained cat may only cry; the struggle stays, played by the chained pose itself.
            AnimatorStateTransition[] chained = State("Chained Idle").transitions;
            Assert.IsTrue(chained.Any(t => t.destinationState.name == "Chained Crying"
                && t.conditions.Any(c => c.parameter == "Emoting" && c.mode == AnimatorConditionMode.If)
                && t.conditions.Any(c => c.parameter == "EmoteId" && Mathf.Approximately(c.threshold, 2f))), "a chained cat must cry");
            Assert.IsTrue(chained.Any(t => t.destinationState.name == "Chained Struggle" && t.hasExitTime && t.conditions.Length == 0),
                "the chained pose must play the struggle on its own");
            Assert.IsFalse(chained.Any(t => t.destinationState.name != "Chained Crying"
                && t.conditions.Any(c => c.parameter == "Emoting" || c.parameter == "Emote")), "only crying answers an emote while chained");
            Assert.AreEqual("Crying", State("Chained Crying").tag, "the tears follow the Crying tag");
            Assert.IsTrue(State("Chained Crying").transitions.Any(t => t.destinationState.name == "Idle"
                && t.conditions.Any(c => c.parameter == "Chained" && c.mode == AnimatorConditionMode.IfNot)), "unchained while crying");
            Assert.IsTrue(PlayerAvatar.EmoteAllowedWhileChained(2));
            Assert.IsFalse(PlayerAvatar.EmoteAllowedWhileChained(1) || PlayerAvatar.EmoteAllowedWhileChained(3));
            Assert.IsTrue(State("Idle").transitions.Any(t => t.destinationState.name == "Pulls Tablet Out"
                                                             && t.conditions.Any(c => c.parameter == "Tablet")));
            Assert.IsTrue(State("Look at Tablet Idle").transitions.Any(t => t.destinationState.name == "Putting Table Away"));
        }

        [Test]
        public void TheAccusingArm_IsWired_AndTurnsLikeTheHead()
        {
            // T3 (Wouh 2026-11-05): "la tête tourne mais pas le bras". The arm the clip points with follows the look.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            var aim = prefab.GetComponent<AvatarArmAim>();
            Assert.IsNotNull(aim, "Cat_Avatar has no AvatarArmAim");
            var so = new SerializedObject(aim);
            var arm = (Transform)so.FindProperty("_armBone").objectReferenceValue;
            Assert.AreEqual("Arm1.L", arm != null ? arm.name : null, "the Accusing clip points with the left arm");
            Assert.IsNotNull(so.FindProperty("_animator").objectReferenceValue);
            foreach (string accusing in new[] { "Accusing", "Accused Idle" })
                Assert.AreEqual(AvatarArmAim.AccuseTag, State(accusing).tag, $"{accusing}: the arm aims on this tag");

            // The arm hangs on -x: a positive yaw swings it across the body, held at 40°; away from it, up to 75°.
            Assert.AreEqual(40f, AvatarArmAim.ClampYaw(70f, 1f, 40f, 75f), 1e-4f);
            Assert.AreEqual(-75f, AvatarArmAim.ClampYaw(-90f, 1f, 40f, 75f), 1e-4f);
            Assert.AreEqual(-40f, AvatarArmAim.ClampYaw(-70f, -1f, 40f, 75f), 1e-4f);
            Assert.AreEqual(20f, AvatarArmAim.ClampYaw(20f, 1f, 40f, 75f), 1e-4f);

            // Same turn as the head: a 90° yaw in the body frame takes a forward-pointing bone to the body's right.
            var body = new GameObject("body").transform;
            var bone = new GameObject("bone").transform;
            try
            {
                body.rotation = Quaternion.Euler(0f, 30f, 0f);
                bone.SetParent(body, false);
                AvatarArmAim.Aim(bone, body, 90f, 0f);
                Assert.Less(Vector3.Distance(body.right, bone.forward), 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(body.gameObject);
            }
        }

        [Test]
        public void TheNewParameters_AreSynchronizedByTheNetworkAnimator()
        {
            GameObject cat = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            Component networkAnimator = cat.GetComponents<Component>().First(c => c != null && c.GetType().Name == "NetworkAnimator");
            SerializedProperty entries = new SerializedObject(networkAnimator).FindProperty("AnimatorParameterEntries.ParameterEntries");
            var synced = new HashSet<string>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("Synchronize").boolValue) synced.Add(e.FindPropertyRelative("name").stringValue);
            }
            foreach (string parameter in new[] { "EmoteId", "Emoting", "Chained", "Tablet" })
                Assert.IsTrue(synced.Contains(parameter), $"'{parameter}' is not synchronized");
        }

        [Test]
        public void Tears_AreWiredOnTheCat()
        {
            var tears = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath).GetComponent<CatTears>();
            Assert.IsNotNull(tears, "Cat_Avatar has no CatTears");
            var so = new SerializedObject(tears);
            Assert.IsNotNull(so.FindProperty("animator").objectReferenceValue, "animator");
            Assert.IsNotNull(so.FindProperty("tearMesh").objectReferenceValue, "tear mesh");
            Assert.IsNotNull(so.FindProperty("tearMaterial").objectReferenceValue, "tear material");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath).GetComponent<CatBlink>(), "no CatBlink");
            var frown = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath).GetComponent<CatFrown>();
            Assert.IsNotNull(frown, "no CatFrown");
            Assert.IsNotNull(new SerializedObject(frown).FindProperty("animator").objectReferenceValue, "CatFrown.animator");
        }
    }
}
