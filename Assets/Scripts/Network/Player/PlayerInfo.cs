#region

using System;
using Unity.Collections;
using Unity.Netcode;

#endregion

namespace Network.Player
{
    // Story 13.0 (Epic 13): the replicated player DTO. An unmanaged, IEquatable<PlayerInfo> value type —
    // the NetworkList<PlayerInfo> constraint (unmanaged + IEquatable) is satisfied because every field is
    // unmanaged (FixedString64Bytes/ulong). The manual NetworkSerialize stays: NGO cannot auto-generate it.
    //
    // NOTE (13.0): a `record struct` (compiler-generated value equality, the cleanest one-line-add vehicle)
    // was attempted first but Unity 6000.2.6f2's compiler is C# 9.0 and rejects it (CS8773 — record structs
    // need C# 10). So equality stays hand-written, but is kept to a SINGLE tidy IEquatable impl: adding a
    // field is now field + SerializeValue line + one term in Equals + one arg to HashCode.Combine.
    //
    // Future grouping (DO NOT build now — lands with the first real appearance var in a later 13.x story):
    // a nested `public PlayerCustomization customization;` where
    // `PlayerCustomization : INetworkSerializable, IEquatable<PlayerCustomization>` holds the appearance
    // vars. PlayerInfo then gains the field + one `serializer.SerializeValue(ref customization)` line + one
    // term here, and every appearance var after that is added inside PlayerCustomization alone — the
    // personalization home, so the per-variable churn stays in one place.
    public struct PlayerInfo : INetworkSerializable, IEquatable<PlayerInfo>
    {
        public FixedString64Bytes playerName;
        // Steam-reserved (DO5): currently write-only (no reader) until Steam is wired — keep, do not delete.
        public FixedString64Bytes playerFullName;
        public ulong playerClientId;
        // Steam-reserved (DO5): currently write-only (no reader) until Steam is wired — keep, do not delete.
        public ulong playerSteamId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref playerClientId);
            serializer.SerializeValue(ref playerFullName);
            serializer.SerializeValue(ref playerSteamId);
        }

        public bool Equals(PlayerInfo _other)
        {
            return playerName.Equals(_other.playerName)
                   && playerFullName.Equals(_other.playerFullName)
                   && playerClientId == _other.playerClientId
                   && playerSteamId == _other.playerSteamId;
        }

        public override bool Equals(object _obj) => _obj is PlayerInfo _other && Equals(_other);

        public override int GetHashCode() => HashCode.Combine(playerName, playerFullName, playerClientId, playerSteamId);
    }
}
