#region

using System;
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
        
        public event Action onCharacterAwakened;
        public event Action onCharacterSleep;
        public event Action onRoleUpdated;

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
            CheckForPowers();
            onRoleUpdated?.Invoke();
        }

        public void CheckForPowers()
        {
            var _foundPowers = GetComponentsInChildren<Power>();
            foreach (var _power in _foundPowers)
            {
                if (!role.powers.Contains(_power))
                {
                    role.powers.Add(_power);
                }
            }
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
            isCorrupted.Value = false;
            isHealed.Value = true;
        }
    }
}
