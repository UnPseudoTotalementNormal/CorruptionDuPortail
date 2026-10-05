#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    /// <summary>
    /// What each player KNOWS about each character (role revealed, corruption revealed, hacked marker).
    ///
    /// NET-10 (epic-network-sync-hardening): knowledge is server-authoritative. Every reveal is written to the server's
    /// <see cref="KnowledgeLedger"/> first; the affected viewer then receives its FULL slice (versioned) and replaces
    /// its local view with it (the PlayerIconManager pattern). Knowledge used to exist only on each client, built by
    /// reveal RPCs, with a client-side wipe at role attribution: a missed, duplicated or misordered reveal could
    /// never be detected or repaired. Clients never write knowledge locally any more.
    ///
    /// Storage on each peer: <see cref="charactersInfoRevealed"/> = this peer's real player; on the HOST,
    /// <c>simulationsKnowledge</c> additionally holds one brain per simulated bot (clientId &gt;= 100), whose slices
    /// GetSafeRpcTarget routes to the host. Reads are unchanged (<see cref="GetCharacterInfo"/>), including the
    /// read-time invariant "a player always sees their own role".
    /// </summary>
    public class GameInfoRevealer : NetworkBehaviour
    {
        public Dictionary<ulong, CharacterInfoReveal> charactersInfoRevealed = new();
        private Dictionary<ulong, Dictionary<ulong, CharacterInfoReveal>> simulationsKnowledge = new();

        public Action onCharacterInfoRevealedChanged;

        // Story 7.3 lane A: GameInfoRevealer is itself a CONSUMER of CharacterManager; scene-wired here.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;
        // Story 8.3 lane A: the game-loop reads move off the per-NetworkManager registry hop onto a
        // scene-wired GameManager, narrowed to IGameLoop (onGameStarted) + IGameStateQuery (GetGameStates).
        // Behaviour-identical under production's single NetworkManager (the 8.2 lane-A precedent).
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;
        private IGameStateQuery Query => gameManager;
        // Story 10.3 lane C: BoardManager (still a singleton) resolved through the composition root in
        // OnNetworkSpawn, consumed by the local card-refresh (null-guarded at the call site).
        private BoardManager boardManager;

        // NET-10: server-only authoritative ledger + the last applied slice version per viewer (every peer).
        private readonly KnowledgeLedger _ledger = new();
        private readonly Dictionary<ulong, int> _appliedVersions = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            boardManager = CompositionRoot.For(NetworkManager).BoardManager;

            // A (re)spawned client asks for its slice instead of trusting whatever it may have missed.
            if (!IsServer)
            {
                RequestKnowledgeSliceServerRpc();
            }
        }

        public void Start()
        {
            Assert.IsNotNull(characterManager, "GameInfoRevealer.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "GameInfoRevealer.gameManager is not wired — wire it in GameScene (the composition root).");
            Loop.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            Query.GetGameStates(typeof(RoleAttributionState)).First().onStateEndClient += OnRolesAttributed;
        }

        // NET-10: a new game starts from empty knowledge. The SERVER resets its ledger and pushes the (empty) slices;
        // clients no longer wipe locally (a local wipe raced the reveals of the new game).
        private void OnRolesAttributed()
        {
            if (!IsServer)
            {
                return;
            }
            _ledger.Reset();
            foreach (ulong _viewer in AllViewers())
            {
                PushSlice(_viewer, ulong.MaxValue, false);
            }
        }

        // ---- reads (every peer) ---------------------------------------------------------------------------

        public CharacterInfoReveal GetCharacterInfo(ulong _clientId, ulong _observerId = ulong.MaxValue)
        {
            if (_observerId == ulong.MaxValue)
            {
                _observerId = CharacterQuery.GetLocalClientId();
            }

            Dictionary<ulong, CharacterInfoReveal> _store = StoreFor(_observerId);
            if (!_store.TryGetValue(_clientId, out CharacterInfoReveal _info))
            {
                _info = new CharacterInfoReveal();
                _store[_clientId] = _info;
            }

            // Real (non-simulated) brain: "self" is the actual local client, NOT the observer passed in (a storage
            // selector); a bot brain's self is the bot.
            EnsureOwnRoleRevealed(_clientId, _observerId >= 100 ? _observerId : CharacterQuery.GetLocalClientId(), _info);
            return _info;
        }

        private Dictionary<ulong, CharacterInfoReveal> StoreFor(ulong _observerId)
        {
            if (_observerId < 100)
            {
                return charactersInfoRevealed;
            }
            if (!simulationsKnowledge.TryGetValue(_observerId, out var _brain))
            {
                _brain = new Dictionary<ulong, CharacterInfoReveal>();
                simulationsKnowledge[_observerId] = _brain;
            }
            return _brain;
        }

        // Invariant: a player always sees their own role. Evaluated at read time so a transient identity/replication
        // hiccup at role attribution can never reveal another player's card (see investigation role-reveal-wrong-card).
        private static void EnsureOwnRoleRevealed(ulong _clientId, ulong _selfId, CharacterInfoReveal _info)
        {
            if (_clientId == _selfId && _info.isRoleRevealed < RevealLevel.Personal)
            {
                _info.isRoleRevealed = RevealLevel.Personal;
            }
        }

        // ---- writes (SERVER only) ----------------------------------------------------------------------------

        /// <summary>Server: reveal (target, field) at <paramref name="_revealLevel"/> to ONE viewer.</summary>
        public void SendRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, ulong _toObserverId, bool _showInfo = true)
        {
            if (!IsServer) return;

            KnowledgeField _field = FieldOf(_revealVariableName);
            bool _changed = _revealLevel == RevealLevel.Public
                ? _ledger.RaisePublic(_clientId, _field, (int)_revealLevel)
                : _ledger.Raise(_toObserverId, _clientId, _field, (int)_revealLevel);
            if (!_changed)
            {
                return;
            }

            if (_revealLevel == RevealLevel.Public)
            {
                PushToAllViewers(_clientId, _showInfo);
            }
            else
            {
                PushSlice(_toObserverId, _clientId, _showInfo);
            }
        }

        /// <summary>
        /// Server: reveal (target, field) to EVERY viewer — the public reveals (chaining, end of game, portal). Kept
        /// under its historical name (it used to be an Everyone-RPC); the optional params are ignored.
        /// </summary>
        public void SetRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, bool _showInfo = true, RpcParams _rpcParams = default)
        {
            if (!IsServer) return;

            KnowledgeField _field = FieldOf(_revealVariableName);
            if (_revealLevel == RevealLevel.Public)
            {
                if (_ledger.RaisePublic(_clientId, _field, (int)_revealLevel))
                {
                    PushToAllViewers(_clientId, _showInfo);
                }
                return;
            }

            foreach (ulong _viewer in AllViewers())
            {
                if (_ledger.Raise(_viewer, _clientId, _field, (int)_revealLevel))
                {
                    PushSlice(_viewer, _clientId, _showInfo);
                }
            }
        }

        /// <summary>
        /// Server: POmniscience expiry — clear the "hacked" marker for ONE viewer (the only non-monotonic write).
        /// </summary>
        public void SendClearHackedRpc(ulong _clientId, ulong _toObserverId)
        {
            if (!IsServer) return;
            if (_ledger.Clear(_toObserverId, _clientId, KnowledgeField.Hacked))
            {
                PushSlice(_toObserverId, _clientId, false);
            }
        }

        /// <summary>
        /// Legacy direct write kept for server-side callers: routes through the ledger like
        /// <see cref="SendRevealLevelRpc"/>. A client never writes knowledge (logged and ignored).
        /// </summary>
        public void SetRevealLevel(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, ulong _observerId, bool _showInfo = true)
        {
            if (!IsServer)
            {
                Debug.LogError($"[DESYNC] A client tried to write knowledge locally ({_revealVariableName} of {_clientId}); ignored — knowledge is server-authoritative.");
                return;
            }
            SendRevealLevelRpc(_clientId, _revealVariableName, _revealLevel, _observerId, _showInfo);
        }

        // Every viewer the server must keep current: connected players, the host, simulated bots, anyone the ledger knows.
        private IEnumerable<ulong> AllViewers()
        {
            var _viewers = new SortedSet<ulong> { NetworkManager.ServerClientId };
            foreach (ulong _id in NetworkManager.ConnectedClientsIds)
            {
                _viewers.Add(_id);
            }
            if (characterManager != null)
            {
                foreach (Character _character in characterManager.GetCharacters(false))
                {
                    ulong _owner = _character.ownerClientId.Value;
                    if (_owner >= 100 && !_character.isFake)
                    {
                        _viewers.Add(_owner);
                    }
                }
            }
            foreach (ulong _known in _ledger.KnownViewers)
            {
                _viewers.Add(_known);
            }
            return _viewers;
        }

        private void PushToAllViewers(ulong _hintTarget, bool _showInfo)
        {
            foreach (ulong _viewer in AllViewers())
            {
                PushSlice(_viewer, _hintTarget, _showInfo);
            }
        }

        private void PushSlice(ulong _viewer, ulong _hintTarget, bool _showInfo)
        {
            List<KnowledgeRow> _rows = _ledger.SliceFor(_viewer, out int _version);
            var _entries = new KnowledgeEntry[_rows.Count];
            for (int _i = 0; _i < _rows.Count; _i++)
            {
                _entries[_i] = KnowledgeEntry.From(_rows[_i]);
            }

            // A real viewer that is not connected (left) has nobody to receive it.
            if (_viewer < 100 && _viewer != NetworkManager.ServerClientId && !NetworkManager.ConnectedClientsIds.Contains(_viewer))
            {
                return;
            }
            // GetSafeRpcTarget: a simulated bot's slice (viewer >= 100) goes to the host, which keeps that bot's brain.
            RpcParams _target = characterManager != null
                ? characterManager.GetSafeRpcTarget(_viewer)
                : RpcTarget.Single(_viewer >= 100 ? NetworkManager.ServerClientId : _viewer, RpcTargetUse.Temp);
            ApplyKnowledgeSliceRpc(_viewer, _version, _entries, _hintTarget, _showInfo, _target);
        }

        [Rpc(SendTo.Server)]
        private void RequestKnowledgeSliceServerRpc(RpcParams _params = default)
        {
            PushSlice(_params.Receive.SenderClientId, ulong.MaxValue, false);
        }

        // ---- slice application (every peer) ------------------------------------------------------------------

        [Rpc(SendTo.SpecifiedInParams)]
        private void ApplyKnowledgeSliceRpc(ulong _viewer, int _version, KnowledgeEntry[] _entries, ulong _hintTarget, bool _showInfo, RpcParams _params = default)
        {
            ApplySlice(_viewer, _version, _entries, _hintTarget, _showInfo);
        }

        private void ApplySlice(ulong _viewer, int _version, KnowledgeEntry[] _entries, ulong _hintTarget, bool _showInfo)
        {
            // Stale or duplicated push (reordering can never roll knowledge back).
            if (_appliedVersions.TryGetValue(_viewer, out int _applied) && _version <= _applied)
            {
                return;
            }
            _appliedVersions[_viewer] = _version;

            Dictionary<ulong, CharacterInfoReveal> _store = StoreFor(_viewer);
            var _incoming = new Dictionary<ulong, KnowledgeEntry>();
            foreach (KnowledgeEntry _entry in _entries ?? Array.Empty<KnowledgeEntry>())
            {
                _incoming[_entry.target] = _entry;
            }

            // Replace IN PLACE (consumers may hold CharacterInfoReveal references): targets absent from the slice go back to False.
            bool _anyChange = false;
            bool _hintRaised = false;
            foreach (ulong _target in _store.Keys.Union(_incoming.Keys).ToList())
            {
                if (!_store.TryGetValue(_target, out CharacterInfoReveal _info))
                {
                    _info = new CharacterInfoReveal();
                    _store[_target] = _info;
                }
                _incoming.TryGetValue(_target, out KnowledgeEntry _new);

                bool _raised = (int)_new.roleRevealed > (int)_info.isRoleRevealed
                               || (int)_new.corruptRevealed > (int)_info.isCorruptRevealed
                               || (int)_new.forceCorruptOnRoleRevealed > (int)_info.forceCorruptOnRoleRevealed
                               || (int)_new.hacked > (int)_info.isHacked;
                bool _changed = _new.roleRevealed != _info.isRoleRevealed
                                || _new.corruptRevealed != _info.isCorruptRevealed
                                || _new.forceCorruptOnRoleRevealed != _info.forceCorruptOnRoleRevealed
                                || _new.hacked != _info.isHacked;

                _info.isRoleRevealed = _new.roleRevealed;
                _info.isCorruptRevealed = _new.corruptRevealed;
                _info.forceCorruptOnRoleRevealed = _new.forceCorruptOnRoleRevealed;
                _info.isHacked = _new.hacked;

                _anyChange |= _changed;
                if (_target == _hintTarget && _raised)
                {
                    _hintRaised = true;
                }
            }

            if (!_anyChange)
            {
                return;
            }

            // A bot's brain only drives the UI while the host is possessing that bot.
            bool _isLocalViewer = _viewer < 100 || RevealVisibilityRules.ShouldRefreshLocalUi(_viewer, CharacterQuery.GetLocalClientId());
            if (!_isLocalViewer)
            {
                return;
            }

            if (_showInfo && _hintRaised && boardManager != null && boardManager.visibleCards != null)
            {
                _ = boardManager.visibleCards.Find(_card => _card.characterInfo != null && _card.characterInfo.ownerClientId.Value == _hintTarget)
                    ?.ShowPseudoWithRevealedInfo(true);
            }
            onCharacterInfoRevealedChanged?.Invoke();
        }

        private static KnowledgeField FieldOf(FixedString64Bytes _revealVariableName)
        {
            string _name = _revealVariableName.ToString();
            if (_name == nameof(CharacterInfoReveal.isRoleRevealed)) return KnowledgeField.RoleRevealed;
            if (_name == nameof(CharacterInfoReveal.isCorruptRevealed)) return KnowledgeField.CorruptRevealed;
            if (_name == nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed)) return KnowledgeField.ForceCorruptOnRoleRevealed;
            if (_name == nameof(CharacterInfoReveal.isHacked)) return KnowledgeField.Hacked;
            throw new ArgumentException($"Unknown knowledge field: {_name}");
        }

        /// <summary>Wire form of one target's levels in a viewer's slice.</summary>
        public struct KnowledgeEntry : INetworkSerializable
        {
            public ulong target;
            public RevealLevel roleRevealed;
            public RevealLevel corruptRevealed;
            public RevealLevel forceCorruptOnRoleRevealed;
            public RevealLevel hacked;

            public static KnowledgeEntry From(KnowledgeRow _row) => new()
            {
                target = _row.Target,
                roleRevealed = (RevealLevel)_row.Levels[(int)KnowledgeField.RoleRevealed],
                corruptRevealed = (RevealLevel)_row.Levels[(int)KnowledgeField.CorruptRevealed],
                forceCorruptOnRoleRevealed = (RevealLevel)_row.Levels[(int)KnowledgeField.ForceCorruptOnRoleRevealed],
                hacked = (RevealLevel)_row.Levels[(int)KnowledgeField.Hacked],
            };

            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                _serializer.SerializeValue(ref target);
                _serializer.SerializeValue(ref roleRevealed);
                _serializer.SerializeValue(ref corruptRevealed);
                _serializer.SerializeValue(ref forceCorruptOnRoleRevealed);
                _serializer.SerializeValue(ref hacked);
            }
        }
    }

    [Serializable]
    public class CharacterInfoReveal
    {
        public RevealLevel isRoleRevealed = RevealLevel.False;
        public RevealLevel isCorruptRevealed = RevealLevel.False;
        public RevealLevel forceCorruptOnRoleRevealed = RevealLevel.False;
        // POmniscience (hack) : "cette carte est piratée", révélé Personal au seul Robot. Pilote le
        // glitch visuel côté client (CardHackGlitch). Aucun autre reveal ne l'écrit.
        public RevealLevel isHacked = RevealLevel.False;
    }

    public enum RevealLevel
    {
        False = 0,
        Personal = 10,
        Public = 20,
    }
}
