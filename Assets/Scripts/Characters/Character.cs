#region

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

#endregion

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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (CharacterManager.instance != null)
            {
                // Verify if identity is already set, otherwise listen for it
                if (!ownerClientId.Value.IsFakeClientId())
                {
                    CharacterManager.instance.RegisterSpawnedCharacter(this);
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
                CharacterManager.instance.RegisterSpawnedCharacter(this);
            }
        }

        private void OnBlessed(bool _previousValue, bool _newValue)
        {
            if (!_newValue)
            {
                return;
            }
            
            Character _localCharacter = GameManager.instance.characterManager.GetLocalCharacter();
            if (_localCharacter == null)
            {
                return;
            }

            if (_localCharacter.role.factionType == FactionType.anomaly ||
                _localCharacter.role.roleID == RoleID.Dryade)
            {
                CardEffectManager.instance.AddCardEffect(CardEffectID.Blessing, ownerClientId.Value);
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
            SleepCharacterClientRpc();
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
            return LobbyPlayerInfoHolder.instance.GetPlayerInfo(ownerClientId.Value).playerName.ToString();
        }

        
        [Rpc(SendTo.Server)]
        public void CorruptPlayerServerRpc()
        {
            isCorrupted.Value = true;
        }

        [Rpc(SendTo.Server)]
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
