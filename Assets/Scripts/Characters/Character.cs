#region

using System;
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
        
        [Header("Variables")] //quand de nouvelle variable son ajoutée, il faut mettre à jour le UpdateCharacter
        public NetworkVariable<bool> isChained = new(false);
        public NetworkVariable<bool> isCorrupted = new(false);
        public NetworkVariable<bool> isEliminated = new(false);
        public NetworkVariable<bool> isBlessed = new(false);
        public NetworkVariable<int> messageLeft = new(1);
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
            onRoleUpdated?.Invoke();
        }
        
        public void AwakenCharacter()
        {
            isAwakened.Value = true;
            role.AwakenRole();
            onCharacterAwakened?.Invoke();
        }
        
        public void SleepCharacter()
        {
            isAwakened.Value = false;
            role.SleepRole();
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

        public void CorruptPlayer()
        {
            GameManager.instance.CorruptPlayerRpc(ownerClientId.Value);
        }

        public void HealPlayer()
        {
            GameManager.instance.HealPlayerRpc(ownerClientId.Value);
        }
    }
}
