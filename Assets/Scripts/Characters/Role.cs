using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using Characters.WinningConditions;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;


[Serializable]
public class Role : INetworkSerializable
{
    [Header("Role Settings")]
    public FixedString64Bytes roleName;
    public CharacterType roleType;
    public FactionType factionType;
    [UnityEngine.Range(1, 3)] public int roleDifficulty;
    
    [SerializeField] public List<Power> powers = new();
    //public List<WinningCondition> winningConditions = new();
        
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

    public Role CopyRole()
    {
        Role _newRole = (Role)Activator.CreateInstance(GetType());
        _newRole.roleName = roleName;
        _newRole.roleType = roleType;
        _newRole.factionType = factionType;
        _newRole.roleDifficulty = roleDifficulty;
        _newRole.isChained = isChained;
        _newRole.powers = new List<Power>();
        //_newRole.winningConditions = winningConditions.ToList();
        
        foreach (Power _power in powers)
        {
            _newRole.powers.Add(_power.CopyPower());
        }

        return _newRole;
    }
}
