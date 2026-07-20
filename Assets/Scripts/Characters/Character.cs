using System;
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

            if (characterManager != null)
            {
                // Verify if identity is already set, otherwise listen for it
                if (!ownerClientId.Value.IsFakeClientId())
                {
                    characterManager.RegisterSpawnedCharacter(this);
                }
                else
                {
                    ownerClientId.OnValueChanged += OnIdentityChanged;
                }
            }

            isBlessed.OnValueChanged += OnBlessed;
        }

        private void OnIdentityChanged(ulong previousValue, ulong newValue)
        {
            if (!newValue.IsFakeClientId())
            {
                ownerClientId.OnValueChanged -= OnIdentityChanged;
                characterManager.RegisterSpawnedCharacter(this);
            }
        }

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

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void AskForRoleUpdateRpc()
        {
            Assert.IsTrue(IsServer, "AskForRoleUpdateRpc can only be called on server");
            UpdateRoleRpc(role);
        }
        
        [Rpc(SendTo.NotServer)]
        public void UpdateRoleRpc(Role _role)
        {
            role.UpdateRole(_role);
            CheckForPowersRpc();
            onRoleUpdated?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        public void CheckForPowersRpc()
        {
            var _foundPowers = GetComponentsInChildren<Power>();
            bool _newPowersFound = false;
            foreach (var _power in _foundPowers)
            {
                if (!role.powers.Contains(_power))
                {
                    role.powers.Add(_power);
                    _newPowersFound = true;
                }
            }
            if (_newPowersFound)
            {
                onPowersUpdated?.Invoke();
            }
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

        public string GetOwnerPseudo()
        {
            if (lobbyPlayerInfoHolder == null) return "Unknown";
            return lobbyPlayerInfoHolder.GetPlayerInfo(ownerClientId.Value).playerName.ToString();
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
