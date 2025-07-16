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
    public class Character : INetworkSerializable
    {
        public Role role;
        public ulong ownerClientId;
        
        [Header("Variables")] //quand de nouvelle variable son ajoutée, il faut mettre à jour le UpdateCharacter
        public bool isChained;
        public bool isCorrupted;
        public bool isEliminated;
        public bool isBlessed;
        public int messageLeft = 1;
        public bool isFake => ownerClientId.IsFakeClientId();
        
        public event Action onCharacterAwakened;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ownerClientId);
            serializer.SerializeValue(ref isChained);
            serializer.SerializeValue(ref isCorrupted);
            serializer.SerializeValue(ref isBlessed);
            serializer.SerializeValue(ref messageLeft);
            serializer.SerializeValue(ref isEliminated);
            
            if (role == null)
            {
                role = new Role();
            }

            role.ownerClientId = ownerClientId; //for sender
            role.NetworkSerialize(serializer);
            role.ownerClientId = ownerClientId; //for receiver
        }
        
        public void UpdateCharacter(Character _newCharacter)
        {
            ownerClientId = _newCharacter.ownerClientId;
            isChained = _newCharacter.isChained;
            isCorrupted = _newCharacter.isCorrupted;
            isBlessed = _newCharacter.isBlessed;
            messageLeft = _newCharacter.messageLeft;
            isEliminated = _newCharacter.isEliminated;
            
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
        }
        
        public Role GetRole(bool ignoreOverride = false)
        {
            return role;
        }

        public string GetOwnerPseudo()
        {
            return LobbyPlayerInfoHolder.instance.GetPlayerInfo(ownerClientId).playerName.ToString();
        }

        public void CorruptPlayer()
        {
            GameManager.instance.CorruptPlayerRpc(ownerClientId);
        }

        public void HealPlayer()
        {
            GameManager.instance.HealPlayerRpc(ownerClientId);
        }
    }
}
