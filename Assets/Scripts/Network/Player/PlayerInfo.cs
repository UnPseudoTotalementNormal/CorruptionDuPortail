using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Network.Player
{
    public struct PlayerInfo : INetworkSerializable, IEquatable<PlayerInfo>
    {
        public FixedString64Bytes playerName;
        public ulong playerClientId;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsWriter)
            {
                serializer.SerializeValue(ref playerName);
                serializer.SerializeValue(ref playerClientId);
            }
            else
            {
                serializer.SerializeValue(ref playerName);
                serializer.SerializeValue(ref playerClientId);
            }
        }

        public bool Equals(PlayerInfo other)
        {
            return playerName.Equals(other.playerName) && playerClientId == other.playerClientId;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(playerName, playerClientId);
        }
    }
}