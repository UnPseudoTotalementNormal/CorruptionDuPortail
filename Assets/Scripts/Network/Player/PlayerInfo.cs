#region

using System;
using Unity.Collections;
using Unity.Netcode;

#endregion

namespace Network.Player
{
    public struct PlayerInfo : INetworkSerializable, IEquatable<PlayerInfo>
    {
        public FixedString64Bytes playerName;
        public FixedString64Bytes playerFullName;
        public ulong playerClientId;
        public ulong playerSteamId;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref playerClientId);
            serializer.SerializeValue(ref playerFullName);
            serializer.SerializeValue(ref playerSteamId);
            if (serializer.IsWriter)
            {
            }
            else
            {
            }
        }

        public bool Equals(PlayerInfo _other)
        {
            return playerName.Equals(_other.playerName) 
                   && playerClientId == _other.playerClientId
                   && playerFullName.Equals(_other.playerFullName)
                   && playerSteamId == _other.playerSteamId;
        }

        public override bool Equals(object _obj)
        {
            return _obj is PlayerInfo _other && Equals(_other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(playerName, playerClientId, playerFullName, playerSteamId);
        }
    }
}