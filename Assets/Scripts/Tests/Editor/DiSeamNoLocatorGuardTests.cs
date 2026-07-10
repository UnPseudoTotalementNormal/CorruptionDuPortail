using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Story 6.1 / Epic 6 (D0) — the static-absence guard that makes the despaghettification
    /// track safe. Once a consumer is rerouted off the Service Locator
    /// (<c>GameManager.instance</c> / <c>CharacterManager.instance</c>) onto an injected
    /// dependency, it is added to <see cref="MigratedConsumers"/>. The guard then FAILS if a
    /// regression ever re-introduces a locator lookup in that type — letting Epic 7 migrate the
    /// 78 <c>.characterManager</c> + 31 <c>.gameInfoRevealer</c> sites in bulk without silently
    /// back-sliding.
    ///
    /// MECHANISM CHOICE (recorded per AC 5): SOURCE scan, not IL scan. The two locators are
    /// asymmetric — <c>GameManager.instance</c> is an auto-property (compiles to a
    /// <c>call get_instance</c>) while <c>CharacterManager.instance</c> is a public static FIELD
    /// (compiles to <c>ldsfld</c>). A single IL token scan would have to special-case both the
    /// <c>call</c> opcode + a method token AND the <c>ldsfld</c> opcode + a field token, and the
    /// operand tokens are module-scoped (brittle once a migrated consumer lives in another
    /// assembly). A source substring catches BOTH uniformly in one rule. It is not hard-path
    /// coupled: the <c>.cs</c> file is located by Unity's enforced "class name == file name"
    /// convention via a recursive search under the Assets tree.
    /// </summary>
    [Category("DiSeamGuard")]
    public class DiSeamNoLocatorGuardTests
    {
        // Curated set of consumers proven to no longer use the locator. Guard #1 (source scan)
        // covers All (also guard-#2 scene-wired) PLUS NoLocatorOnly (story 7.1 — clean types with
        // no scene/prefab instance that guard #2 cannot find). Epic 7+ appends migrated types to
        // one of the two lists as they are rerouted onto injection.
        private static readonly Type[] MigratedConsumers =
            DiSeamMigratedConsumers.All.Concat(DiSeamMigratedConsumers.NoLocatorOnly).ToArray();

        // The Service-Locator accessors a migrated consumer must never reference again.
        private static readonly string[] ForbiddenLocators =
        {
            "GameManager.instance",
            "CharacterManager.instance",
            // 6.3 (AC4a): the For() hub-hop blind spot. The powers population reaches managers via
            // GameManager.For(nm).characterManager / CharacterManager.For(nm) — NOT .instance. Without
            // these two entries an Epic 7 migration could "pass" the guard while still hub-hopping
            // through the locator. CompositionRoot itself legitimately calls For( — which is exactly
            // why it is NOT in DiSeamMigratedConsumers.All (it lives in SceneWiredOnly instead).
            "GameManager.For(",
            "CharacterManager.For(",
            // Story 10.1 (Epic 10 / D4): ChatManager injected into the powers/components via the
            // Power/PowerComponent.chatManager base field (lane C, resolved through the composition
            // root in OnNetworkSpawn). Lock the migrated consumers off the chat global so a future
            // power can't silently re-grab it. ChatManager stays a singleton (no .For(nm) — not
            // de-singletonised), so only the .instance form is forbidden here. The root itself
            // legitimately serves it (SceneWiredOnly, never source-scanned), as do the recorded
            // exceptions (the UI leaves) — none are registered.
            "ChatManager.instance",
            // Story 10.2 (Epic 10 / D4): RoleTargetSystem injected into the targeting powers via the
            // Power.roleTargetSystem base field + RobotBoardInfo via a lane-C field. Same shape as chat
            // (singleton, no .For(nm)) — lock the .instance form. The root serves it (SceneWiredOnly,
            // never source-scanned).
            "RoleTargetSystem.instance",
            // Story 10.3 (Epic 10 / D4): BoardManager injected into the game-loop GameStates (lane-B
            // push) + GameManager (root) + FocusManager/CardEffectManager (lane-A scene field) +
            // GameInfoRevealer (lane-C). Lock the global. Same singleton shape (no .For(nm)) — only the
            // .instance form. Recorded non-registered survivors: SelectionFlowService (POCO → Epic 11.2)
            // + CardPickerManager (UI leaf → Epic 12.2); neither is registered, so neither is scanned.
            "BoardManager.instance",
            // Story 10.4 (Epic 10 / D4): ChainingManager injected into the chaining powers (Power base
            // field) + VoteState (inherited GameState field); StatesCanvas into the lane-B GameState UI
            // host. Both singletons (no .For(nm)). Lock both forms. NOTE: GameAudioManager is the
            // explicit OPT-OUT (global FMOD façade, consumed from non-injectable contexts) — it is
            // deliberately NOT forbidden here (recorded §4); its consumers legitimately keep the global.
            "ChainingManager.instance",
            "StatesCanvas.Instance",
            // Story 10.4 (Epic 10 / D4): MessageManager injected into SendMessagePanel (lane C, resolved
            // through the composition root). Singleton (no .For(nm)) — lock the .instance form. The root
            // serves it (SceneWiredOnly, never source-scanned); the recorded non-registered survivors are
            // the two UI leaves (AwakeningRecapMessages, AnonymousRevealedMessagesComponent → Epic 12.2).
            // MessageManager.cs itself uses the bare `instance` self-reference, never "MessageManager.instance",
            // so locking the qualified form does not bite the manager (it is in the All registry from 8.3).
            "MessageManager.instance",
            // Story 10.4 (Epic 10 / D4): LobbyPlayerInfoHolder injected into the 4 player-name powers (Power
            // base field) + Character (lane C). Singleton (no .For(nm)) — lock the qualified instance form.
            // The root serves it (SceneWiredOnly, never source-scanned); recorded non-registered survivors
            // keep the global: PlayerButtonObject / ConnectedPlayerPanel / ChatPanel (UI → 12.2),
            // UlongExtensions (static ext → 10.5), CharacterManager.AddDebugPlayer (debug). The holder itself
            // is now registered (NoLocatorOnly) and uses the bare `instance` self-ref, so the lock does not bite it.
            "LobbyPlayerInfoHolder.instance",
            // Story 10.5 (Epic 10 / D4): SelectionFlowService (POCO singleton, fan-in 16) injected into the
            // 15 targeting powers (Power base field) + TakeDownThePortalState (GameState lane-B); FocusManager
            // (scene singleton) injected into Power + PVisionOfTheImpossible + TakeDownThePortalState. Both
            // singletons (no .For(nm)) — lock the qualified instance form. The root serves them
            // (SceneWiredOnly, never source-scanned). Unregistered survivors keep the global: SelectionFlowService
            // + CardPickerManager read FocusManager (→ Epic 11/12); SelectionFlowService itself is unregistered.
            // FocusManager.cs (in the All registry) uses the bare `instance` self-ref, so the lock does not bite it.
            "SelectionFlowService.instance",
            "FocusManager.instance",
        };

        [Test]
        public void MigratedConsumers_DoNotReferenceTheLocator()
        {
            foreach (var consumer in MigratedConsumers)
            {
                string source = ReadSource(consumer);
                string[] hits = FindForbiddenLocators(source);

                CollectionAssert.IsEmpty(hits,
                    $"{consumer.Name} references the Service Locator ({string.Join(", ", hits)}) — a migrated " +
                    "consumer must receive its dependency through injection (a [SerializeField] or Initialize(...)), " +
                    "never via a static instance lookup. See the Injection Seam Convention in " +
                    "refactor-architecture-despaghetti.md.");
            }
        }

        [Test]
        public void Guard_Bites_OnSyntheticLocatorUsage()
        {
            // Proves the detection mechanism actually fires — if LightManager (or any future
            // migrated consumer) still held a locator lookup, FindForbiddenLocators would flag it.
            const string badSource = "void Start() { GameManager.instance.currentGameStateIndex.OnValueChanged += X; }";
            CollectionAssert.IsNotEmpty(FindForbiddenLocators(badSource),
                "The guard failed to detect a known locator usage — the mechanism is broken.");

            const string cleanSource = "void Start() { gameManager.currentGameStateIndex.OnValueChanged += X; }";
            CollectionAssert.IsEmpty(FindForbiddenLocators(cleanSource),
                "The guard false-positived on injected access — it must only flag the static locator.");
        }

        [Test]
        public void MigratedConsumers_OnlyReferenceCompositionRootInsideOnNetworkSpawn()
        {
            // 6.3 (AC4b): the lane C whitelist. The one surviving static (CompositionRoot) may be
            // resolved by a migrated consumer ONLY inside OnNetworkSpawn (refactor-architecture-
            // despaghetti.md §3 lane C). Consumers that never touch it (LightManager) pass trivially.
            foreach (var consumer in MigratedConsumers)
            {
                string source = ReadSource(consumer);
                Assert.IsFalse(CompositionRootUsedOutsideOnNetworkSpawn(source),
                    $"{consumer.Name} references CompositionRoot outside OnNetworkSpawn — the lane C root may be " +
                    "resolved ONLY once inside OnNetworkSpawn, then stored in a field. See §3 lane C / §5.");
            }
        }

        [Test]
        public void Guard_Bites_OnForHubHopAndCompositionRootOutsideOnNetworkSpawn()
        {
            // (AC4a) the For() hub-hop must now be caught alongside .instance.
            const string badForSource = "void X() { var c = GameManager.For(NetworkManager).characterManager; }";
            CollectionAssert.IsNotEmpty(FindForbiddenLocators(badForSource),
                "The guard failed to detect a GameManager.For() hub-hop — the AC4a mechanism is broken.");

            const string badCmForSource = "void X() { var c = CharacterManager.For(NetworkManager); }";
            CollectionAssert.IsNotEmpty(FindForbiddenLocators(badCmForSource),
                "The guard failed to detect a CharacterManager.For() hub-hop — the AC4a mechanism is broken.");

            // (AC4b) CompositionRoot used outside OnNetworkSpawn is a violation...
            const string badRootSource = "void Start() { var s = CompositionRoot.For(NetworkManager); }";
            Assert.IsTrue(CompositionRootUsedOutsideOnNetworkSpawn(badRootSource),
                "The guard failed to flag CompositionRoot used outside OnNetworkSpawn — the AC4b mechanism is broken.");

            // ...but inside OnNetworkSpawn it is the sanctioned lane C resolution.
            const string goodRootSource =
                "public override void OnNetworkSpawn() { base.OnNetworkSpawn(); _cm = CompositionRoot.For(NetworkManager).CharacterManager; }";
            Assert.IsFalse(CompositionRootUsedOutsideOnNetworkSpawn(goodRootSource),
                "The guard false-positived on the sanctioned lane C resolution inside OnNetworkSpawn.");

            // A consumer that never references the root must trivially pass.
            const string noRootSource = "void Start() { gameManager.currentGameStateIndex.OnValueChanged += X; }";
            Assert.IsFalse(CompositionRootUsedOutsideOnNetworkSpawn(noRootSource),
                "The guard false-positived on a consumer that does not reference CompositionRoot at all.");
        }

        private static string[] FindForbiddenLocators(string source)
        {
            return ForbiddenLocators.Where(source.Contains).ToArray();
        }

        // (AC4b) MECHANISM (source scan, recorded per task 3): a migrated lane C consumer may
        // reference the one allowed static `CompositionRoot` ONLY inside its OnNetworkSpawn body.
        // We locate the OnNetworkSpawn method span by brace-matching from the first '{' after the
        // signature token "OnNetworkSpawn(" (the "(" excludes prose mentions like "// see
        // OnNetworkSpawn" from being mistaken for the signature), then require every
        // "CompositionRoot" occurrence to fall inside that span. A consumer that uses CompositionRoot
        // but has no OnNetworkSpawn (or a malformed body) is a violation; one with no "CompositionRoot"
        // at all passes trivially. Consistent with guard #1's substring-scan rigor.
        private static bool CompositionRootUsedOutsideOnNetworkSpawn(string source)
        {
            const string token = "CompositionRoot";
            if (!source.Contains(token))
            {
                return false;
            }

            int signatureIndex = source.IndexOf("OnNetworkSpawn(", StringComparison.Ordinal);
            if (signatureIndex < 0)
            {
                return true; // references the root but has no OnNetworkSpawn to host it.
            }

            int openBrace = source.IndexOf('{', signatureIndex);
            if (openBrace < 0)
            {
                return true;
            }

            int depth = 0;
            int closeBrace = -1;
            for (int i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBrace = i;
                        break;
                    }
                }
            }
            if (closeBrace < 0)
            {
                return true;
            }

            for (int i = source.IndexOf(token, StringComparison.Ordinal); i >= 0; i = source.IndexOf(token, i + token.Length, StringComparison.Ordinal))
            {
                if (i < openBrace || i > closeBrace)
                {
                    return true;
                }
            }
            return false;
        }

        private static string ReadSource(Type type)
        {
            // Unity enforces class name == file name for MonoBehaviours, so the source file is
            // <Type.Name>.cs somewhere under Assets. Search rather than hard-code a path.
            string fileName = type.Name + ".cs";
            string[] matches = Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories);

            Assert.IsNotEmpty(matches,
                $"Could not locate source file '{fileName}' for migrated consumer {type.Name} under Assets/.");
            Assert.AreEqual(1, matches.Length,
                $"Ambiguous source file for {type.Name}: found {matches.Length} '{fileName}' files.");

            return File.ReadAllText(matches[0]);
        }
    }
}
