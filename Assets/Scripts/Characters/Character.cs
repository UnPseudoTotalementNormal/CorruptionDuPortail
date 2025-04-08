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
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ownerClientId);
            serializer.SerializeValue(ref isChained);
            
            if (role == null)
            {
                role = new Role();
            }

            role.NetworkSerialize(serializer);
        }
        
        public void AwakenCharacter()
        {
            role.AwakenRole();
        }
        
        public Role GetRole(bool ignoreOverride = false)
        {
            return role;
        }

        public string GetOwnerPseudo()
        {
            return LobbyPlayerInfoHolder.instance.GetPlayerInfo(ownerClientId).playerName.ToString();
        }
    }
}
