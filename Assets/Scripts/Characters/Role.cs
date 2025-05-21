#region

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

#endregion

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
    [SerializeReference, Polymorphic] public List<WinningCondition> winningConditions = new();
    
    public bool isAwakened = false;
    
    public ulong ownerClientId;

    // Ajout d'un constructeur sans paramètre pour la désérialisation réseau
    public Role()
    {
    }
    
    public bool IsTheSameRole(Role _otherRole)
    {
        return roleName == _otherRole.roleName;
    }
    
    public virtual void AwakenRole()
    {
        isAwakened = true;
        foreach (var _power in powers) 
        {
            _power.ownerClientId = ownerClientId; //just to be sure
            _power.powerUseLeft = 1; //TODO: REPLACE 1 WITH SCRIPTABLE OBJECT VALUE
        }
    }
    
    public void SleepRole()
    {
        isAwakened = false;
        foreach (var _power in powers)
        {
            _power.Cancel();
        }
    }
    
    public void UpdateRole(Role _newCharacterRole)
    {
        roleName = _newCharacterRole.roleName;
        roleType = _newCharacterRole.roleType;
        factionType = _newCharacterRole.factionType;
        roleDifficulty = _newCharacterRole.roleDifficulty;
        rolePortrait = _newCharacterRole.rolePortrait;
        isAwakened = _newCharacterRole.isAwakened;
        ownerClientId = _newCharacterRole.ownerClientId;

        powers = _newCharacterRole.powers;
        foreach (Power _power in powers)
        {
            _power.ownerClientId = _newCharacterRole.ownerClientId;
        }
        
        winningConditions = new List<WinningCondition>(_newCharacterRole.winningConditions);
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
        _newRole.ownerClientId = ownerClientId;
        
        foreach (Power _power in powers)
        {
            _newRole.powers.Add((Power)_power.Clone());
        }

        return _newRole;
    }
    
    public async UniTask<Sprite> GetRolePortrait()
    {
        AsyncOperationHandle<Sprite> _operation = Addressables.LoadAssetAsync<Sprite>(CharacterPortraitsValues.values[rolePortrait]);
        await _operation.Task;
        return _operation.Result;
    }
    
    public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
    {
        _serializer.SerializeValue(ref roleName);
        _serializer.SerializeValue(ref roleType);
        _serializer.SerializeValue(ref factionType);
        _serializer.SerializeValue(ref roleDifficulty);
        _serializer.SerializeValue(ref rolePortrait);
        _serializer.SerializeValue(ref isAwakened);
        
        int _powersCount = powers.Count;
        _serializer.SerializeValue(ref _powersCount);
        if (_serializer.IsReader)
        {
            powers = new List<Power>(_powersCount);
            for (int i = 0; i < _powersCount; i++)
            {
                string _powerTypeName = string.Empty;
                _serializer.SerializeValue(ref _powerTypeName);
                Type _powerType = Type.GetType(_powerTypeName);
                Power _power = (Power)Activator.CreateInstance(_powerType);
                _power.NetworkSerialize(_serializer);
                powers.Add(_power);
            }
        }
        else
        {
            foreach (var _power in powers)
            {
                string _powerTypeName = _power.GetType().AssemblyQualifiedName;
                _serializer.SerializeValue(ref _powerTypeName);
                _power.NetworkSerialize(_serializer);
            }
        }

        int _winningConditionsCount = 0;
        _winningConditionsCount = winningConditions.Count;
        _serializer.SerializeValue(ref _winningConditionsCount);
        if (_serializer.IsReader)
        {
            winningConditions = new List<WinningCondition>(_winningConditionsCount);
            for (int i = 0; i < _winningConditionsCount; i++)
            {
                string _conditionTypeName = string.Empty;
                _serializer.SerializeValue(ref _conditionTypeName);
                Type _conditionType = Type.GetType(_conditionTypeName);
                WinningCondition _condition = (WinningCondition)Activator.CreateInstance(_conditionType);
                _condition.NetworkSerialize(_serializer);
                winningConditions.Add(_condition);
            }
        }
        else
        {
            foreach (var _condition in winningConditions)
            {
                string _conditionTypeName = _condition.GetType().AssemblyQualifiedName;
                _serializer.SerializeValue(ref _conditionTypeName);
                _condition.NetworkSerialize(_serializer);
            }
        }

        foreach (var _winningCondition in winningConditions)
        {
            _winningCondition.ownerClientId = ownerClientId;
        }
    }
}

