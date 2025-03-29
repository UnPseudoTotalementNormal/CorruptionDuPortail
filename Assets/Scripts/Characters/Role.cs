using System;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using Characters.WinningConditions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewRole", menuName = "Roles/Role")]
public class Role : ScriptableObject, INetworkSerializable
{
    [Header("Role Settings")]
    [FormerlySerializedAs("characterName")] public string roleName;
    public Sprite roleArtwork;
    [FormerlySerializedAs("characterType")] public CharacterType roleType;
    public FactionType factionType;
    [FormerlySerializedAs("characterDifficulty")] [UnityEngine.Range(1, 3)] public int roleDifficulty;
    
    public List<Power> powers;
    public List<WinningCondition> winningConditions;
        
    [Header("Variables")]
    public bool isChained;
    
    public bool IsTheSameRole(Role otherRole)
    {
        return roleName == otherRole.roleName;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref roleName);
        serializer.SerializeValue(ref roleType);
        serializer.SerializeValue(ref factionType);
        serializer.SerializeValue(ref roleDifficulty);
        serializer.SerializeValue(ref isChained);
    }
}
