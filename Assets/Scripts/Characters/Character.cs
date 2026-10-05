using System;
using System.Collections.Generic;
using Board;
using Characters.Powers;
using Extensions;
using FMODUnity;
using GameLogic;
using Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Characters
{
    [Serializable]
    public class Character : NetworkBehaviour
    {
        public Role role;
        public NetworkVariable<ulong> ownerClientId = new(GameValues.FAKE_CLIENT_ID);
        
        [Header("Variables")] 
        public NetworkVariable<bool> isChained = new(false);
        public NetworkVariable<bool> isCorrupted = new(false);
        public NetworkVariable<bool> isEliminated = new(false);
        public NetworkVariable<bool> isBlessed = new(false);
        public NetworkVariable<bool> isHealed = new(false);
        public NetworkVariable<int> messageLeft = new(1);
        public NetworkVariable<bool> hasSentMessageThisTurn = new(false);
        public NetworkVariable<bool> isAwakened = new(false);
        public bool isFake => ownerClientId.Value.IsFakeClientId();
        public event Action onPowersUpdated;
        public event Action onCharacterAwakened;
        public event Action onCharacterSleep;
        public event Action onRoleUpdated;
        // NET-03: raised on this peer when the replicated roster changes (a pseudo arrived, was renamed, or its
        // owner left) so name surfaces can re-apply the text without replaying any animation.
        public event Action onOwnerPseudoChanged;
        private LobbyPlayerInfoHolder _rosterSubscription;

        // Story 7.2 lane C: CharacterManager resolved once in OnNetworkSpawn via the composition root.
        // Kept null-tolerant (no Assert) — this consumer already guards on a null CharacterManager,
        // so the field preserves that behaviour. For(nm) is a stable per-NM singleton, so caching the
        // result is equivalent to the previous per-call re-resolution.
        private CharacterManager characterManager;
        // Story 10.4 lane C: the lobby player-info holder, resolved once here via the composition root.
        // Null-tolerant — GetOwnerPseudo already guards on a null holder, so the field preserves that
        // behaviour (returns "Unknown" when unresolved).
        private LobbyPlayerInfoHolder lobbyPlayerInfoHolder;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            characterManager = CompositionRoot.For(NetworkManager).CharacterManager;
            lobbyPlayerInfoHolder = CompositionRoot.For(NetworkManager).LobbyPlayerInfoHolder;

            isBlessed.OnValueChanged += OnBlessed;
            EnsureRosterSubscription();

            // NET-07: the role is replicated as state. A late or reordered value can no longer be lost: build it now
            // if it already arrived in the spawn payload, and on every later change.
            roleId.OnValueChanged += OnRoleIdChanged;
            if (!IsServer && HasRoleId)
            {
                ApplyReplicatedRole();
            }
        }

        public override void OnNetworkDespawn()
        {
            roleId.OnValueChanged -= OnRoleIdChanged;
            if (_rosterSubscription != null)
            {
                _rosterSubscription.onRosterChanged -= RaiseOwnerPseudoChanged;
                _rosterSubscription = null;
            }
            base.OnNetworkDespawn();
        }

        // ---- NET-07: role replication --------------------------------------------------------------------------

        /// <summary>
        /// NET-07 (epic-network-sync-hardening): the authoritative identity of this character's role. Server-write;
        /// every peer rebuilds <see cref="role"/> from <see cref="RoleRegistry"/>. 0 = no role yet (lobby).
        /// Replaces GiveRoleToCharacterRpc, whose per-peer await on a spawn promise could be orphaned forever
        /// (the role then stayed the serialized default: empty name, red faction, white portrait).
        /// </summary>
        public NetworkVariable<RoleID> roleId = new((RoleID)0);

        public bool HasRoleId => (int)roleId.Value != 0;

        /// <summary>Server: commits the role already assigned to <see cref="role"/> so every peer rebuilds it.</summary>
        public void CommitRoleServer()
        {
            if (!IsServer || role == null)
            {
                return;
            }
            role.ownerClientId = ownerClientId.Value;
            roleId.Value = role.roleID;
            onRoleUpdated?.Invoke();
        }

        private void OnRoleIdChanged(RoleID _previous, RoleID _current)
        {
            if (IsServer)
            {
                return; // the server owns the authoritative Role object already
            }
            ApplyReplicatedRole();
        }

        private void ApplyReplicatedRole()
        {
            Role _rebuilt = RoleRegistry.CreateRole(roleId.Value);
            if (_rebuilt == null)
            {
                Debug.LogError($"[ROLE] unknown roleId={roleId.Value} for character {ownerClientId.Value} on peer {NetworkManager.LocalClientId}.");
                return;
            }

            _rebuilt.ownerClientId = ownerClientId.Value;
            foreach (var _condition in _rebuilt.winningConditions)
            {
                if (_condition != null)
                {
                    _condition.ownerClientId = _rebuilt.ownerClientId;
                }
            }
            role = _rebuilt;
            CheckForPowersLocal();
            onRoleUpdated?.Invoke();
        }

        /// <summary>
        /// NET-07: re-runs, on THIS peer, what a role refresh used to trigger through the role RPC fan-out: the power
        /// list scan and the onRoleUpdated notification. Called by CharacterManager's refresh broadcast.
        /// </summary>
        public void RefreshLocalRoleViews()
        {
            CheckForPowersLocal();
            onRoleUpdated?.Invoke();
        }

        // Lane C: the holder is resolved in OnNetworkSpawn only. It is a scene-placed object, so it is spawned before
        // any (dynamically spawned) Character on every peer; called again from GetOwnerPseudo as a cheap no-op guard.
        private void EnsureRosterSubscription()
        {
            if (_rosterSubscription != null || lobbyPlayerInfoHolder == null)
            {
                return;
            }
            _rosterSubscription = lobbyPlayerInfoHolder;
            _rosterSubscription.onRosterChanged += RaiseOwnerPseudoChanged;
        }

        private void RaiseOwnerPseudoChanged() => onOwnerPseudoChanged?.Invoke();

        private void OnBlessed(bool _previousValue, bool _newValue)
        {
            if (!_newValue || characterManager == null)
            {
                return;
            }

            Character _localCharacter = characterManager.GetLocalCharacter();
            if (_localCharacter == null || _localCharacter.role == null)
            {
                return;
            }

            if (_localCharacter.role.factionType == FactionType.anomaly ||
                _localCharacter.role.roleID == RoleID.Dryade)
            {
                if (CardEffectManager.instance != null)
                {
                    CardEffectManager.instance.AddCardEffect(CardEffectID.Blessing, ownerClientId.Value);
                }
            }
        }

        /// <summary>
        /// NET-08 (epic-network-sync-hardening): rebuilds <see cref="role"/>.powers on THIS peer as the projection of
        /// every spawned Power whose replicated ownerClientId is this character, in replicated grant order. The list
        /// is identical on every peer by construction and never edited by an RPC (it used to be patched by three event
        /// RPCs plus a hierarchy scan that never removed stale entries). Raises onPowersUpdated only on a real change.
        /// </summary>
        public void CheckForPowersLocal()
        {
            if (role == null || NetworkManager == null)
            {
                return;
            }

            List<Power> _owned = Characters.Powers.Runtime.PowerRegistry.OwnedBy(NetworkManager, ownerClientId.Value);
            bool _changed = _owned.Count != role.powers.Count;
            for (int _i = 0; !_changed && _i < _owned.Count; _i++)
            {
                _changed = role.powers[_i] != _owned[_i];
            }
            if (!_changed)
            {
                return;
            }

            role.powers.Clear();
            role.powers.AddRange(_owned);
            onPowersUpdated?.Invoke();
        }
        
        public void InvokeOnPowersUpdated()
        {
            onPowersUpdated?.Invoke();
        }
        
        [Rpc(SendTo.Server)]
        public void AwakenCharacterServerRpc()
        {
            isAwakened.Value = true;
            hasSentMessageThisTurn.Value = false;
            role.AwakenRole();
            AwakenCharacterClientRpc();
        }

        [Rpc(SendTo.Everyone)]
        protected void AwakenCharacterClientRpc()
        {
            onCharacterAwakened?.Invoke();
        }
        
        [Rpc(SendTo.Server)]
        public void SleepCharacterServerRpc()
        {
            isAwakened.Value = false;
            role.SleepRole();
            SleepCharacterClientRpc();
        }
        

        [Rpc(SendTo.Everyone)]
        protected void SleepCharacterClientRpc()
        {
            onCharacterSleep?.Invoke();
        }
        
        public Role GetRole(bool ignoreOverride = false)
        {
            return role;
        }

        /// <summary>
        /// NET-03: never an empty string for a real player — a missing roster row reads "Joueur ?" and a player who
        /// left mid-game reads "&lt;name&gt; (parti)" (CorruptionDuPortail.Domain.PseudoDisplay). Fake characters
        /// have no player behind them and keep an empty pseudo.
        /// </summary>
        public string GetOwnerPseudo()
        {
            if (isFake)
            {
                return string.Empty;
            }
            EnsureRosterSubscription();
            if (lobbyPlayerInfoHolder == null)
            {
                return CorruptionDuPortail.Domain.PseudoDisplay.MissingLabel;
            }
            bool _hasEntry = lobbyPlayerInfoHolder.TryGetPlayerInfo(ownerClientId.Value, out Network.Player.PlayerInfo _info);
            return CorruptionDuPortail.Domain.PseudoDisplay.Format(_hasEntry, _info.playerName.ToString(), _info.hasLeft);
        }

        
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void CorruptPlayerServerRpc()
        {
            isCorrupted.Value = true;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void HealPlayerServerRpc()
        {
            if (isHealed.Value)
            {
                return;
            }
            isCorrupted.Value = false;
            isHealed.Value = true;
        }

        public void ChainCharacterServer()
        {
            if (!IsServer)
            {
                Debug.LogError("ChainCharacterServer can only be called on the server");
                return;
            }
            isChained.Value = true;
            isCorrupted.Value = true;
        }
    }
}
