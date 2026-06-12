using System.Collections;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using GameLogic;
using AudioSystem;
using RoleTarget;
using Board;
using ChatSystem;
using CorruptionDuPortail.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    /// <summary>
    /// Story 4.0 — the GLOBAL GOLDEN power-effect trace. Pins, per power, the ORDERED list of
    /// <see cref="EffectDescriptor"/> intentions the CURRENT (inline, pre-PowerResolver) code
    /// emits, observed via the <see cref="PowerEffectTrace"/> seam with a recording observer.
    /// The real singleton/RPC effects still run verbatim alongside (observation seam, no behavior
    /// change). This is the standing oracle the per-power stories 4.1–4.4 reproduce bit-for-bit
    /// after each power's resolution moves into a Domain PowerResolver + adapter dispatch switch.
    ///
    /// Every RPC-emitting power is driven with a target clientId >= 100 (the bot range), so the
    /// recorded descriptor carries the bot slot — the clientId>=100 oracle case (NFR5). The
    /// interception assertion (intercepted-as-bot vs sent-over-wire) is a 4.1+ dispatch concern;
    /// here the bot-target INTENTION is pinned.
    /// </summary>
    [Category("PowerGolden")]
    public class PowerGoldenTraceTests
    {
        private const ulong Owner = 0;   // == NetworkManager.ServerClientId on the host harness
        private const ulong BotTarget = 100; // clientId >= 100 → simulated-bot range

        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _revealerGo;
        private GameInfoRevealer _revealer;
        private GameObject _compositionRootGo;

        private GameObject _dummyCharPrefab;

        private readonly RecordingObserver _observer = new();

        private sealed class RecordingObserver : IPowerEffectObserver
        {
            public readonly List<EffectDescriptor> Trace = new();
            public void Record(EffectDescriptor effect) => Trace.Add(effect);
            public void Clear() => Trace.Clear();
        }

        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                EnableSceneManagement = false
            };

            _dummyCharPrefab = new GameObject("CharacterPrefab_Golden");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();

            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            _revealerGo = new GameObject("GameInfoRevealer");
            _revealerGo.AddComponent<NetworkObject>();
            _revealer = _revealerGo.AddComponent<GameInfoRevealer>();
            _revealer.GetComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_gameManager, "gameInfoRevealer", _revealer);
            ReflectionHelper.SetPrivateField(_revealer, "characterManager", _characterManager);
            // Story 8.3: GameInfoRevealer's game-loop reads now resolve through an injected [SerializeField]
            // gameManager (lane A), so the harness wires it like the production scene does.
            ReflectionHelper.SetPrivateField(_revealer, "gameManager", _gameManager);

            // Story 7.5: powers spawned in the test body resolve the revealer via
            // CompositionRoot.For(nm).GameInfoRevealer (the GameManager pass-through is gone), so the
            // harness registers a CompositionRoot for this NM.
            _compositionRootGo = NetworkTestHelper.RegisterCompositionRoot(_gameManager, _characterManager, _revealer);

            // Plain singletons. BoardManager BEFORE CardEffectManager (its Start subscribes to it).
            new GameObject("RoleTargetSystem").AddComponent<RoleTargetSystem>().gameObject.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(new GameObject("BoardManager").AddComponent<BoardManager>(), "characterManager", _characterManager);
            new GameObject("AudioManager").AddComponent<GameAudioManager>();
            new GameObject("CardEffectManager").AddComponent<CardEffectManager>();
            new GameObject("ChatManager").AddComponent<ChatManager>().gameObject.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _revealer);

            PowerEffectTrace.Observer = _observer;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PowerEffectTrace.Reset();

            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(GameAudioManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChatManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_revealerGo);
            Object.Destroy(_compositionRootGo);
            Object.Destroy(GameObject.Find("RoleTargetSystem"));
            Object.Destroy(GameObject.Find("BoardManager"));
            Object.Destroy(GameObject.Find("AudioManager"));
            Object.Destroy(GameObject.Find("CardEffectManager"));
            Object.Destroy(GameObject.Find("ChatManager"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        private T CreatePower<T>(string name) where T : Power
        {
            GameObject powerGo = new GameObject(name);
            var netObj = powerGo.AddComponent<NetworkObject>();
            T power = powerGo.AddComponent<T>();
            netObj.Spawn();
            power.ownerClientId.Value = Owner;
            return power;
        }

        // ---- POmniscience (the hack) — reveal role to owner + store hacked target -----------
        [UnityTest]
        public IEnumerator POmniscience_Trace_IsPinned()
        {
            Character owner = _characterManager.AddNewCharacter(Owner);
            Character target = _characterManager.AddNewCharacter(BotTarget);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            var power = CreatePower<POmniscience>("Omniscience");
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);
            _observer.Clear();

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", BotTarget);
            yield return null;

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting((int)Owner, (int)BotTarget),
                new StoreHackTarget((int)BotTarget),
                new RevealInfo((int)BotTarget, RevealField.RoleRevealed, RevealVisibility.Personal, (int)Owner, true),
                RequestCharacterRefresh.Instance,
            }, _observer.Trace, Describe(_observer.Trace));
        }

        // ---- PCorruptingMark — corrupt the target (server body) -----------------------------
        [UnityTest]
        public IEnumerator PCorruptingMark_Trace_IsPinned()
        {
            Character owner = _characterManager.AddNewCharacter(Owner);
            Character target = _characterManager.AddNewCharacter(BotTarget);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            var power = CreatePower<PCorruptingMark>("CorruptingMark");
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);
            _observer.Clear();

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", BotTarget);
            yield return null;

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting((int)Owner, (int)BotTarget),
                new StoreLastCorrupted((int)BotTarget),
                new CorruptionSucceeded((int)BotTarget),
                new CorruptPlayer((int)BotTarget),
            }, _observer.Trace, Describe(_observer.Trace));
        }

        // ---- PBoundByInk (entrapment) — discover the private ink chat for the target --------
        [UnityTest]
        public IEnumerator PBoundByInk_Trace_IsPinned()
        {
            Character owner = _characterManager.AddNewCharacter(Owner);
            Character target = _characterManager.AddNewCharacter(BotTarget);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            var power = CreatePower<PBoundByInk>("BoundByInk");
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);
            _observer.Clear();

            // powerChatId is unassigned (-1) until AttributeBoundByInkChat; the server-click body
            // pins the targeting + discover + register sequence as-is.
            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", BotTarget);
            yield return null;

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting((int)Owner, (int)BotTarget),
                new DiscoverChat(-1, "Lié par l'encre", PowerEffectAudience.Specific((int)BotTarget)),
                new RegisterInkTarget((int)BotTarget),
            }, _observer.Trace, Describe(_observer.Trace));
        }

        // ---- PCursedVision — corrupt+reveal target & owner, branch on chosen ----------------
        [UnityTest]
        public IEnumerator PCursedVision_Trace_IsPinned_NonChosenTarget()
        {
            Character owner = _characterManager.AddNewCharacter(Owner);
            Character target = _characterManager.AddNewCharacter(BotTarget);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);
            target.role = new Role { factionType = FactionType.anomaly }; // NOT chosen → "n'est pas un élu"

            // No CursedVision card effect is configured in this bare harness — the real
            // AddCardEffect logs an error and returns; the INTENTION is still recorded (the
            // Record fires before the real call). Ignore benign effect-IO logs (trace test, not a
            // log test). Set inside the body: the framework resets LogAssert state after SetUp.
            LogAssert.ignoreFailingMessages = true;

            var power = CreatePower<PCursedVision>("CursedVision");
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);
            _observer.Clear();

            // Effect logic lives in OnCharacterPicked; CheckIsTargetValid(100) passes (non-local,
            // unrevealed, uncorrupted). OnUsed() tails the base plumbing bricks.
            ReflectionHelper.InvokePrivateMethod(power, "OnCharacterPicked", target);
            yield return null;

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting((int)Owner, (int)BotTarget),
                new CorruptPlayer((int)BotTarget),
                new RevealInfo((int)BotTarget, RevealField.CorruptRevealed, RevealVisibility.Personal, (int)Owner, false),
                new AddCardEffect(2, (int)BotTarget, true), // CardEffectID.CursedVision == 2, hidden (not chosen)
                new ChatLocal($"{target.GetOwnerPseudo()} n'est pas un élu.", -1),
                new CorruptPlayer((int)Owner),
                new RevealInfo((int)Owner, RevealField.CorruptRevealed, RevealVisibility.Personal, (int)Owner, false),
                new StopLoopingSound("PowerCanalisationSound"),
                DecrementUses.Instance,
                RequestCharacterRefresh.Instance,
            }, _observer.Trace, Describe(_observer.Trace));
        }

        private static string Describe(IEnumerable<EffectDescriptor> trace) =>
            "Observed trace: " + string.Join(" | ", trace);
    }
}
