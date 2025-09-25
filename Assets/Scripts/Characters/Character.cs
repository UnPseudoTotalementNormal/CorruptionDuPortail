#region

using System;
using Extensions;
using FMODUnity;
using GameLogic;
using Network;
using Unity.Netcode;
using UnityEngine;

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
        public bool isFake => ownerClientId.Value.IsFakeClientId();
        
        public event Action onCharacterAwakened;
        public event Action onCharacterSleep;
        
        public void UpdateCharacter(Character _newCharacter)
        {
            ownerClientId.Value = _newCharacter.ownerClientId.Value;
            isChained.Value = _newCharacter.isChained.Value;
            isCorrupted.Value = _newCharacter.isCorrupted.Value;
            isBlessed.Value = _newCharacter.isBlessed.Value;
            messageLeft.Value = _newCharacter.messageLeft.Value;
            isEliminated.Value = _newCharacter.isEliminated.Value;
            
            if (role == null)
            {
                role = new Role();
            }
            
            role.UpdateRole(_newCharacter.role);
        }
        
        public void AwakenCharacter()
        {
            role.AwakenRole();
            onCharacterAwakened?.Invoke();
        }
        
        public void SleepCharacter()
        {
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
