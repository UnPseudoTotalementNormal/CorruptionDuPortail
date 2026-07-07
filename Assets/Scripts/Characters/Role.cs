#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Assets;
using Characters.Powers;
using Characters.WinningConditions;
using Extensions;
using FMODUnity;
using GameLogic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

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

    public readonly List<Power> powers = new();
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
            int _useToSet = (_power.powerUseRegenPerAwakening == -1) 
                ? _power.maxPowerUse 
                : Math.Clamp(_power.powerUseLeft.Value + _power.powerUseRegenPerAwakening, 0, _power.maxPowerUse);
            _power.powerUseLeft.Value = _useToSet;
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
        
        winningConditions = new List<WinningCondition>(_newCharacterRole.winningConditions);
        onChainingSound = _newCharacterRole.onChainingSound;
        onGameStartRoleRevealSound = _newCharacterRole.onGameStartRoleRevealSound;
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

        int _winningConditionsCount = 0;
        if (!_serializer.IsReader)
            _winningConditionsCount = winningConditions.Count(_c => _c != null);
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
            foreach (var _condition in winningConditions.Where(_c => _c != null))
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
