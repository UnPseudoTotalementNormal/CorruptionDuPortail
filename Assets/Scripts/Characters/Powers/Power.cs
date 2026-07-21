#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using ChatSystem;
using Cysharp.Threading.Tasks;
using RoleTarget;
using Characters.Powers.PowerComponents;
using Characters.Powers.Runtime;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using GameLogic.Validation;
using UI.BoardUI.Selection;
using Network.Action;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using static Characters.Powers.Target.TargetUtils;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class Power : NetworkBehaviour
    {
        public NetworkVariable<ulong> ownerClientId = new();

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public GameObject power3DObjectPrefab;
        public float maxWaitTime;

        public bool isPassive;

        // Server-set to the source PREFAB this instance was cloned from (see CharacterManager.GivePowerToCharacter).
        // Copiers (Ugues/Luma/Réincarnation) clone this base prefab instead of the live, possibly-mutated instance,
        // so a copy always starts at base state. [NonSerialized]: a prefab ref, never replicated; server-only reads.
        [System.NonSerialized] public Power basePrefab;

        // The design-time value of isPassive, snapshotted at Awake before any runtime flip. PReincarnation
        // mutates the LIVE isPassive to true post-use (to disable re-use + skip awakening), which is a gameplay
        // state, NOT a change of the power's authored nature. The RoleCard categorizes active/passive by THIS
        // so a spent Réincarnation still reads as its authored active power instead of jumping to the passive
        // list. Presentation-only; the live isPassive still drives usability/awakening/power-bar.
        [NonSerialized] public bool authoredIsPassive;

        // Authored (base) passive value, used by copier eligibility: a base-active power that turned passive at
        // runtime (e.g. a used Réincarnation) stays copiable and comes back fresh. Reuses the Awake snapshot
        // (authoredIsPassive) — replicated-independent and correct on every instance. Provenance (isCopiedPower)
        // is still read live.
        public bool BaseIsPassive => authoredIsPassive;
        [Tooltip("Hide this power from the role-presentation card (e.g. a faction win-objective that isn't personal kit). Gameplay-neutral: presentation only.")]
        public bool hideFromRoleCard;
        public bool hasToBeAwakened = true;
        
        public TargetIncludeFlags targetIncludeFlags;
        public bool needTargetSelection => targetIncludeFlags != 0;
        
        public NetworkVariable<int> powerUseLeft = new();
        public int maxPowerUse = 1;
        [Tooltip("-1 == maxUse")] public int powerUseRegenPerAwakening = -1;
        
        /// <summary>
        /// A generic validator for target selection. Add your rules here in Awake/Start/OnNetworkSpawn().
        /// Example: targetValidator.AddRule(ctx => ctx.targetId != ownerClientId.Value);
        /// </summary>
        protected Validator<(ulong targetId, TargetType targetType)> targetValidator = new();

        // Story 11.1 (Epic 11 / D5): the base CanUse eligibility rule chain extracted to a pure,
        // EditMode-tested Domain POCO. This adapter builds a plain-value snapshot from the owner
        // Character's NetworkVariables + this power's state and delegates the decision; the expensive
        // target enumeration stays here, passed as a lazy delegate to preserve the exact short-circuit.
        private readonly CorruptionDuPortail.Domain.PowerUsability _usability = new();

        // Story 7.1 lane C: CharacterManager resolved ONCE in OnNetworkSpawn (via the composition
        // root) and consumed by this base AND every concrete power, replacing the GameManager hub-hop.
        protected CharacterManager characterManager;
        // Story 7.3 lane C: GameInfoRevealer, same seam. Null-tolerant (no Assert) — not every power
        // uses it, and minimal harnesses spawn bare powers with no revealer; reveal-using powers
        // always have it in production (scene root) and in their own harnesses.
        protected GameInfoRevealer gameInfoRevealer;
        // Story 10.1 lane C: the chat manager, resolved through the composition root and consumed by
        // chatting powers' send/notify surface instead of the global. Null-tolerant like the revealer
        // (no Assert) — not every power chats, and minimal harnesses spawn bare powers with no chat
        // manager; chatting powers always have one in production and in their own harnesses.
        protected ChatManager chatManager;
        // Story 10.2 lane C: the targeting system, same seam. Null-tolerant — not every power records a
        // targeting; targeting powers always have one in production and in their own harnesses.
        protected RoleTargetSystem roleTargetSystem;
        // Story 10.4 lane C: the chaining manager, same seam. Null-tolerant — only chaining powers use it.
        protected ChainingManager chainingManager;
        // Story 10.4 lane C: the lobby player-info holder (player names), same seam. Null-tolerant — only
        // the player-name powers read it, and in production/their harnesses the global is always present.
        protected Network.LobbyPlayerInfoHolder lobbyPlayerInfoHolder;
        // Story 10.5 lane C: the selection-flow service (a POCO singleton, eager new() → never null) and
        // the focus manager (scene singleton). Resolved through the composition root like the rest; the
        // targeting powers drive their click-to-pick choreography through these instead of the globals.
        // focusManager is null-tolerant (StopUse already null-guards it); selectionFlowService is a POCO
        // singleton so the field is always set in production and in any context the root can reach.
        protected SelectionFlowService selectionFlowService;
        protected FocusManager focusManager;

        [Header("Sounds")] 
        public EventReference canalisationSound;
        public EventReference onUsedSound;
        
        public const string CANALISATION_SOUND_KEY = "PowerCanalisationSound";
        [NonSerialized] public bool isCurrentlyUsed;

        // LIFETIME marker. Server-set on a one-shot copy handed to a thief (Ugues' Marque d'Hurluberluges, Luma's
        // fake-card copy). Replicated. When such a copy is spent (powerUseLeft 0) the server despawns it for good —
        // see OnUsed — so "temporaire = perdu" holds literally instead of leaving a greyed-out husk. Distinct from
        // isCopiedPower: a one-shot copy IS a copy, but l'Incomplet's permanent grants are copies that never despawn.
        public NetworkVariable<bool> isStolenCopy = new();

        // PROVENANCE marker. Server-set on ANY power that is a copy of another role's power — one-shot thief copies
        // (Ugues/Luma) AND l'Incomplet's Réincarnation grants (permanent OR one-shot). Feeds the copier eligibility
        // filter: a copy can never itself be re-copied. Orthogonal to isStolenCopy (lifetime). Marker only.
        public NetworkVariable<bool> isCopiedPower = new();

        // Server-set on a power GRANTED at runtime that must not leak in the RoleCard (e.g. L'Incomplet's
        // Réincarnation, which grants a chosen role's full power set). Distinct from isStolenCopy: these are
        // permanent, non-one-shot powers, so they must NOT get the power-bar-hide-when-spent / non-stealable
        // semantics of isStolenCopy. Replicated so the inspecting client's RoleCard filter can honour it.
        // Marker only — authority unchanged.
        public NetworkVariable<bool> hideFromRoleCardRuntime = new();

        // APPENDED (feat/targeting-icons) — never reorder/rename the fields above it. The sprite this power
        // draws on a marked player's CharactersBar thumbnail through the private player-icon channel. The
        // icon identifies THE POWER that produced the information, not a state of the marked player.
        // Deliberately left null on every power today: the system ships without its first consumer, and a
        // marker whose power has no sprite simply draws nothing (no error log — that is a normal case).
        [Header("Bar icon (private player-icon channel)")]
        [Tooltip("Sprite shown on a marked player's CharactersBar thumbnail, to the ONE viewer this power " +
                 "marked them for. Leave empty for powers that place no icon.")]
        [SerializeField] private Sprite barIcon;

        /// <summary>The private-marker sprite for this power (null when the power places no icon).</summary>
        public Sprite BarIcon => barIcon;

        public static event Action<Power> onPowerSpawned;
        public event Action onPowerUsedServer;
        public NetworkAction onPowerUsed;
        public event Action onPowerReparented;
        public event Action onPowerGameStartedServerTriggered;
        public event Action onStartUse;
        public event Action onStopUse;

        /// <summary>
        /// CLIENT-SIDE, CASTER-ONLY. Raised on the owner's client with the grade of the use that just
        /// resolved — the "did my power work?" signal the playtesters asked for. Never raised for
        /// <see cref="PowerVerdict.None"/>, so the unconditional-effect powers stay silent.
        ///
        /// Deliberately NOT piggybacked on onStopUse / OnUsedClientRpc: most powers call OnUsed() BEFORE
        /// their decision runs (see PBlessing, PTruthChains, POmniscience, …), so at OnUsed time the verdict
        /// does not exist yet. This is its own channel, emitted from the decision seam instead.
        ///
        /// Private by design: the grade goes to the caster only, never broadcast — a public "raté" would
        /// hand free deduction information to the table.
        /// </summary>
        public event Action<PowerVerdict> onPowerVerdict;

        public List<PowerComponent> powerComponents = new();

        public Character ownerCharacter => characterManager.GetCharacter(ownerClientId.Value, false);
        
        [HideInInspector] public ulong idHolderServer;
        // Snapshot the authored isPassive before any runtime flip (PReincarnation's post-use ChangeIsPassiveRpc).
        // Awake runs at instantiation on every instance (host + clients), before RPCs can fire.
        protected virtual void Awake() => authoredIsPassive = isPassive;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            characterManager = CompositionRoot.For(NetworkManager).CharacterManager;
            Assert.IsNotNull(characterManager,
                "Power.characterManager unresolved — CompositionRoot.For(NetworkManager) returned no CharacterManager. " +
                "Did a subclass override OnNetworkSpawn without calling base.OnNetworkSpawn()?");
            gameInfoRevealer = CompositionRoot.For(NetworkManager).GameInfoRevealer;
            chatManager = CompositionRoot.For(NetworkManager).ChatManager;
            roleTargetSystem = CompositionRoot.For(NetworkManager).RoleTargetSystem;
            chainingManager = CompositionRoot.For(NetworkManager).ChainingManager;
            lobbyPlayerInfoHolder = CompositionRoot.For(NetworkManager).LobbyPlayerInfoHolder;
            selectionFlowService = CompositionRoot.For(NetworkManager).SelectionFlowService;
            focusManager = CompositionRoot.For(NetworkManager).FocusManager;
            if (IsServer)
            {
                ownerClientId.Value = idHolderServer;
                onPowerUsed = new NetworkAction("OnPowerUsed_" + powerName, this);
            }
            onPowerSpawned?.Invoke(this);
        }
        
        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName;
        }

        // Powers-POCO v2 in-place wiring (Phase 3): run this power's pure decision and dispatch its
        // effect intentions through the shared executor registry. Server-only — the decision is pure
        // (returns intentions), the executors carry the NGO side effects. The uses decrement stays in
        // each power's own use flow (this helper only realises effects); pass a state resolver for the
        // ~4 stateful powers that write their own replicated carrier.
        // Returns the outcome's verdict (None when it did not run or the power has no notion of correctness)
        // so an adapter that still owns engine-coupled bookkeeping can key off the same grade instead of
        // re-deriving it — see PDroolyHealing's per-night healed roster.
        protected PowerVerdict RunDecisionEffects(IPowerDecision decision, in PowerContext context,
            IPowerStateResolver state = null)
        {
            if (!IsServer) return PowerVerdict.None;
            PowerOutcome outcome = decision.Decide(context);
            if (!outcome.Accepted) return PowerVerdict.None;
            PowerDispatcherHost.Dispatcher.Dispatch(outcome.Effects, new EffectRuntime(NetworkManager, state));
            EmitVerdictServer(outcome.Verdict);
            return outcome.Verdict;
        }

        // Client-runtime variant of RunDecisionEffects for the handful of powers whose effect runs on a
        // SPECIFIC client rather than the server (LackOfAffection — the decision resolves on the contacted
        // target's own client, keyed by PowerContext.IsTrueLocalTarget). No IsServer guard: the caller is
        // already inside a client-scoped RPC body and has done its own locality check. The dispatch + state
        // threading are otherwise identical.
        protected PowerVerdict RunClientDecisionEffects(IPowerDecision decision, in PowerContext context,
            IPowerStateResolver state = null)
        {
            PowerOutcome outcome = decision.Decide(context);
            if (!outcome.Accepted) return PowerVerdict.None;
            PowerDispatcherHost.Dispatcher.Dispatch(outcome.Effects, new EffectRuntime(NetworkManager, state));
            EmitVerdictFromClient(outcome.Verdict);
            return outcome.Verdict;
        }

        // ---- Caster-facing verdict channel ------------------------------------------------------------
        // Owner-only, one hop. Server path: raise locally when the owner IS the server (host / simulated
        // bot), else one targeted RPC. Client path: a client-runtime decision (RunClientDecisionEffects)
        // may resolve on the OWNER's client (CursedVision, EmbraceOfShadows) — raise straight away — or on
        // the CONTACTED TARGET's client (LackOfAffection) — bounce through the server so the grade still
        // lands on the caster. PowerVerdict.None never travels.

        /// <summary>Server-side entry: route an outcome's verdict to the caster. No-op for None.</summary>
        protected void EmitVerdictServer(PowerVerdict verdict)
        {
            if (verdict == PowerVerdict.None || !IsServer) return;

            ulong _owner = ownerClientId.Value;
            if (_owner == NetworkManager.ServerClientId)
            {
                onPowerVerdict?.Invoke(verdict);
                return;
            }
            // GetSafeRpcTarget keeps the simulated-bot flow intact (id >= 100 → host intercepts).
            OnPowerVerdictClientRpc(verdict, characterManager.GetSafeRpcTarget(_owner));
        }

        /// <summary>Client-side entry for the client-runtime decision path. No-op for None.</summary>
        protected void EmitVerdictFromClient(PowerVerdict verdict)
        {
            if (verdict == PowerVerdict.None) return;

            if (characterManager.IsLocalOrSimulated(ownerClientId.Value))
            {
                onPowerVerdict?.Invoke(verdict);
                return;
            }
            ReportVerdictServerRpc(verdict);
        }

        [Rpc(SendTo.Server)]
        private void ReportVerdictServerRpc(PowerVerdict _verdict) => EmitVerdictServer(_verdict);

        [Rpc(SendTo.SpecifiedInParams)]
        private void OnPowerVerdictClientRpc(PowerVerdict _verdict, RpcParams _params)
            => onPowerVerdict?.Invoke(_verdict);

        // Live read-only roster view for roster-reading decisions (faction / same-role / robot / healed /
        // pseudo). Built fresh per call over the already-resolved CharacterManager + lobby holder — cheap,
        // no state. The EditMode counterpart is FakeRoster.
        protected IRosterView Roster => new CharacterManagerRoster(characterManager, lobbyPlayerInfoHolder);

        // Live state resolver over this power itself: a state-carrier power implements its narrow ports
        // (IHackTargetState, ICorruptionEvents, …) and passes SelfState so the executors reach its own NVs.
        // Stateless powers never touch this. The EditMode counterpart is FakeState.
        protected IPowerStateResolver SelfState => new PowerStateAdapter(this);
        
        public List<ulong> GetValidTargets(TargetType _targetType = TargetType.Character)
        {
            List<ulong> _validTargets = characterManager.GetCharacters(false).Select(_c => _c.ownerClientId.Value).ToList();
            _validTargets = _validTargets.Where(_targetClientId => CheckIsTargetValid(_targetClientId, _targetType)).ToList();
            return _validTargets;
        }
        public bool CheckIsTargetValid(ulong _targetClientId, TargetType _targetType)
        {
            return targetValidator.Evaluate((_targetClientId, _targetType));
        }
        
        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = ownerCharacter;
            if (!_powerCharacter)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }

            var _context = new CorruptionDuPortail.Domain.PowerUsabilityContext(
                allComponentsAllowUse: !powerComponents.Any(_pc => !_pc.CanUsePower()),
                isPassive: isPassive,
                isCurrentlyUsed: isCurrentlyUsed,
                ignoreCurrentlyUsed: _ignoreCurrentlyUsed,
                isChained: _powerCharacter.isChained.Value,
                isEliminated: _powerCharacter.isEliminated.Value,
                hasToBeAwakened: hasToBeAwakened,
                isAwakened: _powerCharacter.isAwakened.Value,
                needsTargetSelection: needTargetSelection,
                powerUsesLeft: powerUseLeft.Value);

            // The target enumeration stays in the adapter (engine-coupled) and is passed lazily so it
            // runs only when every earlier rule has passed — identical to the original short-circuit.
            return _usability.CanUse(_context, () => GetValidTargets().Count > 0);
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
            GameAudioManager.instance.PlayEventInstance(canalisationSound.GetPath(), CANALISATION_SOUND_KEY);
            onStartUse?.Invoke();
        }
        
        [Rpc(SendTo.Server)]
        public void OnUsedServerRpc()
        {
            OnUsed(false);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void OnUsedClientRpc(RpcParams _params)
        {
            StopUse();
        }

        /// <summary>
        /// Triggers the use of the power. Calls the server if necessary, then executes server-side logic and notifies the owner client.
        /// </summary>
        /// <param name="_callToServer">IMPORTANT: If false, the method must be called directly on the server. if true, sends an RPC to the server to process the usage.</param>
        
        public void OnUsed(bool _callToServer = true) //NEEDS TO BE CALLED ON SERVER IF BOOL IS FALSE, it will call on owner client too after
        {
            StopUse();

            if (!IsServer)
            {
                if (_callToServer)
                {
                    OnUsedServerRpc();
                }
                return;
            }
            
            OnUsedServer();
            onPowerUsed?.Invoke();
            if (ownerClientId.Value != NetworkManager.ServerClientId) //notify owner client
            {
                OnUsedClientRpc(characterManager.GetSafeRpcTarget(ownerClientId.Value));
            }

            // A spent one-shot stolen copy (Ugues / Luma) despawns for good — "temporaire = perdu". DEFERRED one
            // frame, never synchronous: an "act-then-RPC" power issues a second ServerRpc on `this` right after
            // OnUsed() (Vision of the Impossible, Lack of Affection); despawning now would drop that RPC and lose
            // the effect. The frame's incoming RPCs run before the next-frame despawn, and clients get a frame to
            // catch powerUseLeft==0 for the bar's scale-out animation.
            if (isStolenCopy.Value && powerUseLeft.Value <= 0)
            {
                DespawnSpentCopyNextFrameServer().Forget();
            }
        }

        private bool _despawnScheduled;

        private async UniTaskVoid DespawnSpentCopyNextFrameServer()
        {
            if (_despawnScheduled)
            {
                return;
            }
            _despawnScheduled = true;

            ulong _owner = ownerClientId.Value;
            await UniTask.NextFrame();

            // `this` may have been despawned by another path in the meantime (game end, disconnect chain).
            if (this == null || !IsSpawned || !IsServer)
            {
                return;
            }
            characterManager.RemovePowerFromCharacter(_owner, this);
        }
        
        protected virtual void OnUsedServer()
        {
            powerUseLeft.Value -= 1;
            onPowerUsedServer?.Invoke();
            characterManager.AskForUpdateAllCharactersRpc();
        }

        // Server-only. Turns a freshly-granted copy into a single-use, non-regenerating stolen power that stays
        // spent forever (spent → despawned by OnUsed). Shared by Ugues (Marque d'Hurluberluges) and Luma (fake-card
        // copy) — pass as the GivePowerToCharacter onReady hook so both configure copies identically.
        public static void ConfigureAsOneShotStolenCopy(Power _copy)
        {
            if (_copy == null || !_copy.IsServer)
            {
                return;
            }
            _copy.isCopiedPower.Value = true; // a one-shot copy is also a copy → not re-copiable
            _copy.isStolenCopy.Value = true;
            _copy.maxPowerUse = 1;
            _copy.powerUseRegenPerAwakening = 0; // never refilled on awaken → spent means spent ("perdu").
            _copy.powerUseLeft.Value = 1;
        }

        // Server-only. Marks a freshly-granted copy as a permanent copy — usable/regenerating like a normal power,
        // but flagged so no copier can re-copy it. Used by l'Incomplet's own (non-copied) Réincarnation grants:
        // permanent (Q3) yet still "a copy" (Q1). Pass as the GivePowerToCharacter onReady hook.
        public static void MarkAsPermanentCopy(Power _copy)
        {
            if (_copy == null || !_copy.IsServer)
            {
                return;
            }
            _copy.isCopiedPower.Value = true;
        }

        public virtual void Cancel()
        {
            if (!isCurrentlyUsed && !isPassive)
            {
                return;
            }
            StopUse();
        }

        protected virtual void StopUse()
        {
            if (isCurrentlyUsed) //only play if currently being used (avoid double sound effects when stopped by client and then by server)
            {
                if (!string.IsNullOrEmpty(onUsedSound.GetPath()))
                {
                    RuntimeManager.PlayOneShot(onUsedSound);
                }
                if (focusManager != null)
                {
                    focusManager.UnfocusAll();
                }
            }
            if (GameAudioManager.instance != null)
            {
                GameAudioManager.instance.StopEventInstance(CANALISATION_SOUND_KEY);
            }
            isCurrentlyUsed = false;
            onStopUse?.Invoke();
        }

        public virtual void UsingPowerUpdate() //note: please make it visuals only
        {
        }

        public virtual void PassivePowerUpdate()
        {
        }

        public virtual void OnGameStartedServer()
        {
            onPowerGameStartedServerTriggered?.Invoke();
        }

        public void OnReparentedServer()
        {
            if (!IsServer)
            {
                return;
            }
            ulong _oldOwnerId = ownerClientId.Value;
            ownerClientId.Value = GetComponentInParent<Character>().ownerClientId.Value;
            OnReparentedClientRpc(_oldOwnerId, ownerClientId.Value);
        }

        [Rpc(SendTo.Everyone)]
        public virtual void OnReparentedClientRpc(ulong _oldParentId, ulong _newParentId)
        {
            // SendTo.Everyone + fire-and-forget: this runs on every peer the instant the server
            // reparents, but a target Character may not be in a remote client's roster yet. At game
            // start the fake-client / bot owner (ownerClientId == GameValues.FAKE_CLIENT_ID =
            // ulong.MaxValue) lags replication, so GetCharacter returns null there while the host
            // (which simulates the bot synchronously) resolves it. Skipping the powers-list edit on
            // null is safe: Character.CheckForPowersRpc rebuilds role.powers from the reparented
            // Power children once the character syncs, so the client still converges. Mirrors the
            // null-guard in the sibling SendTo.Everyone RPC PowerManager.RemovePowerFromCharacterPowerListRpc.
            var _oldParentCharacter = characterManager.GetCharacter(_oldParentId, false);
            var _newParentCharacter = characterManager.GetCharacter(_newParentId, false);

            if (_oldParentCharacter && _oldParentCharacter.role != null)
            {
                _oldParentCharacter.role.powers.Remove(this);
                _oldParentCharacter.InvokeOnPowersUpdated();
            }

            if (_newParentCharacter && _newParentCharacter.role != null)
            {
                if (!_newParentCharacter.role.powers.Contains(this))
                {
                    _newParentCharacter.role.powers.Add(this);
                }
                _newParentCharacter.InvokeOnPowersUpdated();
            }

            onPowerReparented?.Invoke();
        }
    }
}

