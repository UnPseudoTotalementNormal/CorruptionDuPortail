#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic.GameStates;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    public class GameInfoRevealer : NetworkBehaviour
    {
        public Dictionary<ulong, CharacterInfoReveal> charactersInfoRevealed = new();
        private Dictionary<ulong, Dictionary<ulong, CharacterInfoReveal>> simulationsKnowledge = new();
        
        public Action onCharacterInfoRevealedChanged;

        public void Start()
        {
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First().onStateEndClient += OnRolesAttributed;
        }

        private void OnRolesAttributed()
        {
            charactersInfoRevealed = new Dictionary<ulong, CharacterInfoReveal>();
            foreach (var _character in GameManager.instance.characterManager.GetCharacters())
            {
                AddCharacterToInfoList(_character);
            }
        }

        private void AddCharacterToInfoList(Character _character, ulong _observerId = ulong.MaxValue)
        {
            if (_observerId == ulong.MaxValue)
            {
                _observerId = CharacterManager.instance.GetLocalClientId();
                if (_observerId >= 100)
                {
                    AddCharacterToSimulatedInfoList(_character, _observerId);
                    return;
                }
            }

            var _characterInfoReveal = new CharacterInfoReveal();
            if (_character.ownerClientId.Value == _observerId)
            {
                _characterInfoReveal.isRoleRevealed = RevealLevel.Personal;
            }
            charactersInfoRevealed.TryAdd(_character.ownerClientId.Value, _characterInfoReveal);
        }

        private void AddCharacterToSimulatedInfoList(Character _character, ulong _observerId)
        {
            if (!simulationsKnowledge.ContainsKey(_observerId))
            {
                simulationsKnowledge[_observerId] = new Dictionary<ulong, CharacterInfoReveal>();
                // Prefill with existing public knowledge if needed
                foreach (var _c in GameManager.instance.characterManager.GetCharacters())
                {
                    var _info = new CharacterInfoReveal();
                    if (_c.ownerClientId.Value == _observerId) _info.isRoleRevealed = RevealLevel.Personal;
                    simulationsKnowledge[_observerId].TryAdd(_c.ownerClientId.Value, _info);
                }
            }
            
            if (!simulationsKnowledge[_observerId].ContainsKey(_character.ownerClientId.Value))
            {
                var _info = new CharacterInfoReveal();
                if (_character.ownerClientId.Value == _observerId) _info.isRoleRevealed = RevealLevel.Personal;
                simulationsKnowledge[_observerId].TryAdd(_character.ownerClientId.Value, _info);
            }
        }

        public CharacterInfoReveal GetCharacterInfo(ulong _clientId, ulong _observerId = ulong.MaxValue)
        {
            if (_observerId == ulong.MaxValue)
            {
                _observerId = CharacterManager.instance.GetLocalClientId();
            }
            
            if (_observerId >= 100)
            {
                if (!simulationsKnowledge.ContainsKey(_observerId))
                {
                    AddCharacterToSimulatedInfoList(GameManager.instance.characterManager.GetCharacter(_clientId, false), _observerId);
                }
                var _observerBrain = simulationsKnowledge[_observerId];
                if (!_observerBrain.ContainsKey(_clientId))
                {
                    AddCharacterToSimulatedInfoList(GameManager.instance.characterManager.GetCharacter(_clientId, false), _observerId);
                }
                return _observerBrain[_clientId];
            }

            if (!charactersInfoRevealed.ContainsKey(_clientId))
            {
                AddCharacterToInfoList(GameManager.instance.characterManager.GetCharacter(_clientId, false), _observerId);
            }
            return charactersInfoRevealed[_clientId];
        }
        
        public void SetRevealLevel(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, ulong _observerId, bool _showInfo = true)
        {
            var _field = typeof(CharacterInfoReveal).GetField(_revealVariableName.ToString());
            Assert.IsNotNull(_field, "Field not found: " + _revealVariableName);
            
            CharacterInfoReveal _info = GetCharacterInfo(_clientId, _observerId);
            RevealLevel _currentRevealLevel = (RevealLevel)_field.GetValue(_info);
            
            if ((int)_currentRevealLevel >= (int)_revealLevel)
            {
                return;
            }
            
            _field.SetValue(_info, _revealLevel);

            if (_showInfo && _observerId == CharacterManager.instance.GetLocalClientId())
            {
                _ = BoardManager.instance.visibleCards.Find(_card => _card.characterInfo.ownerClientId.Value == _clientId)
                    ?.ShowPseudoWithRevealedInfo(true);
            }
            
            if (_observerId == CharacterManager.instance.GetLocalClientId())
            {
                onCharacterInfoRevealedChanged?.Invoke();
            }
        }

        public void SendRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, ulong _toObserverId, bool _showInfo = true)
        {
            if (!IsServer) return;

            var _target = RpcTarget.Single(_toObserverId, RpcTargetUse.Persistent);
            if (_toObserverId >= 100)
            {
                _target = RpcTarget.Single(0, RpcTargetUse.Persistent); // Redirect to Host
                SetRevealLevelSimulatedRpc(_clientId, _revealVariableName, _revealLevel, _toObserverId, _showInfo, _target);
            }
            else
            {
                SetRevealLevelRpc(_clientId, _revealVariableName, _revealLevel, _showInfo, _target);
            }
        }

        [Rpc(SendTo.Everyone, AllowTargetOverride = true)]
        public void SetRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, bool _showInfo = true, RpcParams _rpcParams = default)
        {
            if (_revealLevel == RevealLevel.Public)
            {
                SetRevealLevel(_clientId, _revealVariableName, _revealLevel, 0, _showInfo);
                foreach (var _simulId in simulationsKnowledge.Keys.ToList())
                {
                    SetRevealLevel(_clientId, _revealVariableName, _revealLevel, _simulId, _showInfo);
                }
            }
            else
            {
                SetRevealLevel(_clientId, _revealVariableName, _revealLevel, 0, _showInfo);
            }
        }

        [Rpc(SendTo.Everyone, AllowTargetOverride = true)]
        public void SetRevealLevelSimulatedRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, ulong _intendedReceiverId, bool _showInfo = true, RpcParams _rpcParams = default)
        {
            if (_revealLevel == RevealLevel.Public)
            {
                SetRevealLevel(_clientId, _revealVariableName, _revealLevel, 0, _showInfo);
                foreach (var _simulId in simulationsKnowledge.Keys.ToList())
                {
                    SetRevealLevel(_clientId, _revealVariableName, _revealLevel, _simulId, _showInfo);
                }
            }
            else
            {
                SetRevealLevel(_clientId, _revealVariableName, _revealLevel, _intendedReceiverId, _showInfo);
            }
        }

        private Dictionary<ulong, CharacterInfoReveal> GetSimulatedBrain(ulong _id)
        {
            if (!simulationsKnowledge.ContainsKey(_id))
            {
                simulationsKnowledge[_id] = new Dictionary<ulong, CharacterInfoReveal>();
                foreach (var _c in GameManager.instance.characterManager.GetCharacters())
                {
                    var _info = new CharacterInfoReveal();
                    if (_c.ownerClientId.Value == _id) _info.isRoleRevealed = RevealLevel.Personal;
                    simulationsKnowledge[_id].TryAdd(_c.ownerClientId.Value, _info);
                }
            }
            return simulationsKnowledge[_id];
        }
    }

    [Serializable]
    public class CharacterInfoReveal
    {
        public RevealLevel isRoleRevealed = RevealLevel.False;
        public RevealLevel isCorruptRevealed = RevealLevel.False;
        public RevealLevel forceCorruptOnRoleRevealed = RevealLevel.False;
    }
    
    public enum RevealLevel
    {
        False = 0,
        Personal = 10,
        Public = 20,
    }
}
