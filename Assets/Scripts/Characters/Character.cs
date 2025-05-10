using System;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters
{
    [Serializable]
    public class Character : INetworkSerializable
    {
        public Role role;
        public ulong ownerClientId;
        
        [Header("Variables")]
        public bool isChained;
        public bool isCorrupted;
        public bool isBlessed;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ownerClientId);
            serializer.SerializeValue(ref isChained);
            serializer.SerializeValue(ref isCorrupted);
            serializer.SerializeValue(ref isBlessed);
            
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
            
            if (role == null)
            {
                role = new Role();
            }
            
            role.UpdateRole(_newCharacter.role);
        }
        
        public void AwakenCharacter()
        {
            role.AwakenRole();
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
