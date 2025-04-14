using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Assets;
using Characters.Powers;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;
using UnityEngine.UI;


[Serializable]
public class Role : INetworkSerializable
{
    [Header("Role Settings")]
    public FixedString64Bytes roleName;
    public CharacterType roleType;
    public FactionType factionType;
    public CharacterPortraitsValues.CharacterPortraits rolePortrait;
    [UnityEngine.Range(1, 3)] public int roleDifficulty;
    
    [SerializeField] public List<Power> powers = new();
    public List<WinningCondition> winningConditions = new();
    
    public bool IsTheSameRole(Role otherRole)
    {
        return roleName == otherRole.roleName;
    }
    
    public virtual void AwakenRole()
    {
        Debug.Log($"{roleName} has awakened!");
    }
    
    public Role CopyRole()
    {
        Role _newRole = (Role)Activator.CreateInstance(GetType());
        _newRole.roleName = roleName;
        _newRole.roleType = roleType;
        _newRole.factionType = factionType;
        _newRole.roleDifficulty = roleDifficulty;
        _newRole.powers = new List<Power>();
        _newRole.winningConditions = winningConditions.ToList();
        _newRole.rolePortrait = rolePortrait;
        
        foreach (Power _power in powers)
        {
            _newRole.powers.Add(_power.CopyPower());
        }

        return _newRole;
    }
    
    public async UniTask<Sprite> GetRolePortrait()
    {
        AsyncOperationHandle<Sprite> _operation = Addressables.LoadAssetAsync<Sprite>(CharacterPortraitsValues.values[rolePortrait]);
        await _operation.Task;
        return _operation.Result;
    }
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref roleName);
        serializer.SerializeValue(ref roleType);
        serializer.SerializeValue(ref factionType);
        serializer.SerializeValue(ref roleDifficulty);
        serializer.SerializeValue(ref rolePortrait);
        
        int powersCount = powers.Count;
        serializer.SerializeValue(ref powersCount);
        if (serializer.IsReader)
        {
            powers = new List<Power>(powersCount);
            for (int i = 0; i < powersCount; i++)
            {
                string powerTypeName = string.Empty;
                serializer.SerializeValue(ref powerTypeName);
                Type powerType = Type.GetType(powerTypeName);
                Power power = (Power)Activator.CreateInstance(powerType);
                power.NetworkSerialize(serializer);
                powers.Add(power);
            }
        }
        else
        {
            foreach (var power in powers)
            {
                string powerTypeName = power.GetType().AssemblyQualifiedName;
                serializer.SerializeValue(ref powerTypeName);
                power.NetworkSerialize(serializer);
            }
        }

        int winningConditionsCount = winningConditions.Count;
        serializer.SerializeValue(ref winningConditionsCount);
        if (serializer.IsReader)
        {
            winningConditions = new List<WinningCondition>(winningConditionsCount);
            for (int i = 0; i < winningConditionsCount; i++)
            {
                string conditionTypeName = string.Empty;
                serializer.SerializeValue(ref conditionTypeName);
                Type conditionType = Type.GetType(conditionTypeName);
                WinningCondition condition = (WinningCondition)Activator.CreateInstance(conditionType);
                condition.NetworkSerialize(serializer);
                winningConditions.Add(condition);
            }
        }
        else
        {
            foreach (var condition in winningConditions)
            {
                string conditionTypeName = condition.GetType().AssemblyQualifiedName;
                serializer.SerializeValue(ref conditionTypeName);
                condition.NetworkSerialize(serializer);
            }
        }
    }
}
