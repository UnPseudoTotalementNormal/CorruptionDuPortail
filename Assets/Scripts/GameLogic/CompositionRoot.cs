using System.Collections.Generic;
using Characters;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    /// <summary>
    /// Story 6.3 / Epic 6 (D0) — THE ONE surviving project static of the
    /// despaghettification track (refactor-architecture-despaghetti.md §4). Scene-placed in
    /// GameScene; its manager references are lane A <c>[SerializeField]</c> fields covered by
    /// SceneWiringGuard. It is the resolution point for lane C (NGO-spawned) consumers, which
    /// call <see cref="For"/> ONCE in <c>OnNetworkSpawn</c> — the guard-whitelisted exception
    /// to the no-locator rule (§3 lane C).
    ///
    /// RESOLUTION DESIGN (recorded per Task 1 / the staleness check): <see cref="For"/> returns a
    /// lightweight value resolver (<see cref="Services"/>) that DELEGATES to the per-NetworkManager
    /// registries already built in 5.0c/5.0d (<see cref="CharacterManager.For"/> /
    /// <see cref="GameManager.For"/>). A root *instance* is NOT required for resolution: the static
    /// can answer for any NM the manager registries know — which is exactly what lets the
    /// MultiClientGameFixture's second in-process NM (no scene-placed root of its own) resolve its
    /// own graph with zero fixture changes. The scene-placed root instance is the production lane-A
    /// wiring carrier (the SceneWiringGuard target) and is Awake-registered per NM so a future
    /// consumer can also reach the concrete root by NM; production has exactly one NM, so
    /// <c>For(Singleton)</c> resolves to the same managers the scene root holds — behaviour-identical
    /// to the historical locator.
    /// </summary>
    public class CompositionRoot : MonoBehaviour
    {
        // Lane A wiring carrier: SceneWiringGuard asserts these are non-null in GameScene. The
        // accessors below resolve through the per-NM registries (not these fields directly), so
        // production (one NM) and the fixture (two NMs) share one code path; in production
        // For(Singleton) returns exactly these wired managers.
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CharacterManager characterManager;
        // Story 7.3: GameInfoRevealer is NOT de-singletonised (no GameInfoRevealer.For(nm)), so the
        // root carries it as a lane-A scene ref and resolves it from the registered scene root.
        [SerializeField] private GameInfoRevealer gameInfoRevealer;

        private NetworkManager _networkManager;

        // Per-NetworkManager registry of scene-placed roots, mirroring the proven GameManager.For
        // pattern. Registered at Awake (NEVER OnNetworkSpawn — cross-object spawn order is not
        // guaranteed, and this is deliberately not a NetworkBehaviour). Used as the production
        // fast-path / future direct-root lookup; resolution itself does not require it.
        private static readonly Dictionary<NetworkManager, CompositionRoot> s_byNetworkManager = new();

        /// <summary>
        /// Resolves the service graph for the given NetworkManager. Returns a lightweight value
        /// resolver delegating to the per-NM manager registries (5.0c/5.0d), so it answers for ANY
        /// nm those registries know — with or without a scene-placed root instance for that nm.
        /// </summary>
        public static Services For(NetworkManager _networkManager) => new Services(_networkManager);

        // Typed accessors over the scene-placed root (AC1). Delegate to the registries so the root
        // and For(nm) share one resolution path.
        public CharacterManager CharacterManager => Characters.CharacterManager.For(_networkManager);
        // Story 9.1 (Epic 9 / D3): the narrow read slice of the resolved CharacterManager.
        public ICharacterQuery CharacterQuery => Characters.CharacterManager.For(_networkManager);
        public GameManager GameManager => GameLogic.GameManager.For(_networkManager);
        public GameInfoRevealer GameInfoRevealer => gameInfoRevealer;
        // Story 8.1 (Epic 8 / D2): the narrow game-loop / state-query slices of the resolved GameManager,
        // so consumers can depend on the intent (IGameLoop / IGameStateQuery) instead of the whole hub.
        public IGameLoop GameLoop => GameLogic.GameManager.For(_networkManager);
        public IGameStateQuery GameStateQuery => GameLogic.GameManager.For(_networkManager);

        // GameInfoRevealer has no per-NM registry of its own, so it is resolved from the scene root
        // registered for the NM (production: the single Singleton-bound root). Returns null for NMs
        // with no scene root (e.g. the fixture's second NM, which spawns no revealer-using consumer).
        private static GameInfoRevealer ResolveGameInfoRevealer(NetworkManager _networkManager)
        {
            if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _root) && _root != null && _root.gameInfoRevealer != null)
            {
                return _root.gameInfoRevealer;
            }
            // Story 7.5: the GameManager.gameInfoRevealer pass-through fallback was removed together
            // with the rest of the GameManager hub (D-NFR4). Production always has a scene-placed
            // CompositionRoot registered above, so the scene root answers. An NM with no registered
            // root (a PlayMode harness) must register a CompositionRoot of its own to resolve the
            // revealer — see the power test harnesses. (A diagnostic warning here was considered in
            // code review but rejected: Power.gameInfoRevealer is null-tolerant by design, so a null
            // is legitimate for many consumers and the warning would cry wolf — see deferred-work.md.)
            return null;
        }

        private void Awake()
        {
            // Non-networked init: bind to the production NM (Singleton) and register the scene root.
            // The escape hatch in project-context.md:157 — a plain MonoBehaviour may init at Awake;
            // the lane C CONSUMERS resolve in OnNetworkSpawn, the root does not.
            _networkManager = NetworkManager.Singleton;

            Assert.IsNotNull(gameManager,
                "CompositionRoot.gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(characterManager,
                "CompositionRoot.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameInfoRevealer,
                "CompositionRoot.gameInfoRevealer is not wired — wire it in GameScene (the composition root).");

            if (_networkManager != null)
            {
                s_byNetworkManager[_networkManager] = this;
            }
        }

        private void OnDestroy()
        {
            // Value-scan unregister: NetworkManager.Singleton may already be null during shutdown
            // teardown, so never key off it (mirrors GameManager.UnregisterFromRegistry).
            NetworkManager _key = null;
            foreach (var _pair in s_byNetworkManager)
            {
                if (_pair.Value == this)
                {
                    _key = _pair.Key;
                    break;
                }
            }
            if (_key != null)
            {
                s_byNetworkManager.Remove(_key);
            }
        }

#if UNITY_EDITOR
        // Domain reload is disabled in this project — statics survive Play sessions. Drop any
        // registry entry left from a prior session at Play entry (model: GameManager.cs SubsystemRegistration reset).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
        }
#endif

        /// <summary>
        /// Lightweight per-NetworkManager resolver returned by <see cref="For"/>. Holds nothing but
        /// the NM and delegates every accessor to the per-NM manager registries, so it allocates
        /// nothing on the heap and needs no root instance to answer.
        /// </summary>
        public readonly struct Services
        {
            private readonly NetworkManager _networkManager;

            public Services(NetworkManager networkManager)
            {
                _networkManager = networkManager;
            }

            public CharacterManager CharacterManager => Characters.CharacterManager.For(_networkManager);
            // Story 9.1 (Epic 9 / D3): narrow read slice of the resolved CharacterManager.
            public ICharacterQuery CharacterQuery => Characters.CharacterManager.For(_networkManager);
            public GameManager GameManager => GameLogic.GameManager.For(_networkManager);
            public GameInfoRevealer GameInfoRevealer => ResolveGameInfoRevealer(_networkManager);
            // Story 8.1 (Epic 8 / D2): narrow game-loop / state-query slices of the resolved GameManager.
            public IGameLoop GameLoop => GameLogic.GameManager.For(_networkManager);
            public IGameStateQuery GameStateQuery => GameLogic.GameManager.For(_networkManager);
        }
    }
}
