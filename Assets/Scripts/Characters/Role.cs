#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Assets;
using Characters.Powers;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using Extensions;
using FMODUnity;
using GameLogic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

#endregion

[Serializable]
public class Role : INetworkSerializable, ICloneable
{
    [Header("Role Settings")]
    public FixedString64Bytes roleName;
    public CharacterType roleType;
    public FactionType factionType;
    public RoleID roleID;
    public CharacterPortraitsValues.CharacterPortraits rolePortrait;
    [UnityEngine.Range(1, 3)] public int roleDifficulty;
    
    public List<Power> powers => GameManager.instance.characterManager.GetCharacter(ownerClientId, false).GetComponentsInChildren<Power>().ToList(); //todo: BIG TEMPORARY
    [SerializeReference, Polymorphic] public List<WinningCondition> winningConditions = new();

    [Header("Sounds")] 
    public EventReference onChainingSound;
    public EventReference onGameStartRoleRevealSound;
    
    public ulong ownerClientId;

    public Role()
    {
    }
    
    public bool IsTheSameRole(Role _otherRole)
    {
        return roleName == _otherRole.roleName;
    }
    
    public virtual void AwakenRole()
    {
        foreach (var _power in powers) 
        {
            _power.ownerClientId = ownerClientId;
            _power.powerUseLeft.Value = _power.maxPowerUse;
        }
    }
    
    public void SleepRole()
    {
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
        ownerClientId = _newCharacterRole.ownerClientId;
        roleID = _newCharacterRole.roleID;

        foreach (Power _power in powers)
        {
            _power.ownerClientId = _newCharacterRole.ownerClientId;
        }
        
        winningConditions = new List<WinningCondition>(_newCharacterRole.winningConditions);
        onChainingSound = _newCharacterRole.onChainingSound;
        onGameStartRoleRevealSound = _newCharacterRole.onGameStartRoleRevealSound;
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
        _serializer.SerializeValue(ref roleID);
        _serializer.SerializeValue(ref ownerClientId);
        onChainingSound.NetworkSerialize(_serializer);
        onGameStartRoleRevealSound.NetworkSerialize(_serializer);
        
        int _powersCount = powers.Count;
        _serializer.SerializeValue(ref _powersCount);

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

    public object Clone()
    {
        Role _newRole = (Role)this.MemberwiseClone();
        _newRole.winningConditions = new List<WinningCondition>();
        foreach (WinningCondition _condition in winningConditions)
        {
            if (_condition is ICloneable _cloneable)
                _newRole.winningConditions.Add((WinningCondition)_cloneable.Clone());
            else
                _newRole.winningConditions.Add(_condition); // fallback: shallow copy
        }
        return _newRole;
    }
}
