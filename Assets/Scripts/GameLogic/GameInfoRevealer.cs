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

            // Own-role reveal is no longer stamped here. It used to be computed
            // from a fragile "ownerClientId == localId" snapshot taken the instant
            // roles were attributed: if any id was still mid-replication, Personal
            // could land on the wrong card and was never reconciled. The invariant
            // "a player always sees their own role" is now enforced at read time in
            // GetCharacterInfo (see investigation role-reveal-wrong-card).
            charactersInfoRevealed.TryAdd(_character.ownerClientId.Value, new CharacterInfoReveal());
        }

        private void AddCharacterToSimulatedInfoList(Character _character, ulong _observerId)
        {
            if (!simulationsKnowledge.ContainsKey(_observerId))
            {
                simulationsKnowledge[_observerId] = new Dictionary<ulong, CharacterInfoReveal>();
                // Prefill with existing public knowledge if needed
                foreach (var _c in GameManager.instance.characterManager.GetCharacters())
                {
                    // Own-role reveal enforced at read time (GetCharacterInfo), not stamped here.
                    simulationsKnowledge[_observerId].TryAdd(_c.ownerClientId.Value, new CharacterInfoReveal());
                }
            }

            if (!simulationsKnowledge[_observerId].ContainsKey(_character.ownerClientId.Value))
            {
                simulationsKnowledge[_observerId].TryAdd(_character.ownerClientId.Value, new CharacterInfoReveal());
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
                var _simInfo = _observerBrain[_clientId];
                EnsureOwnRoleRevealed(_clientId, _observerId, _simInfo);
                return _simInfo;
            }

            if (!charactersInfoRevealed.ContainsKey(_clientId))
            {
                AddCharacterToInfoList(GameManager.instance.characterManager.GetCharacter(_clientId, false), _observerId);
            }
            var _info = charactersInfoRevealed[_clientId];
            // Real (non-simulated) brain: "self" is the actual local client, NOT the
            // _observerId passed in. The RPC write-path hardcodes _observerId = 0 as a
            // "local main dict" sentinel, which collides with the host's real clientId 0;
            // using it here would reveal the host's role to every other client.
            EnsureOwnRoleRevealed(_clientId, CharacterManager.instance.GetLocalClientId(), _info);
            return _info;
        }

        // Invariant: a player always sees their own role. Evaluated at read time so
        // a transient identity/replication hiccup at role attribution can never
        // reveal another player's card (see investigation role-reveal-wrong-card).
        // _selfId is the id that owns this knowledge: the local client for the real
        // brain, or the simulated client (>=100) for a bot brain.
        private static void EnsureOwnRoleRevealed(ulong _clientId, ulong _selfId, CharacterInfoReveal _info)
        {
            if (_clientId == _selfId && _info.isRoleRevealed < RevealLevel.Personal)
            {
                _info.isRoleRevealed = RevealLevel.Personal;
            }
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
                if (BoardManager.instance != null && BoardManager.instance.visibleCards != null)
                {
                    _ = BoardManager.instance.visibleCards.Find(_card => _card.characterInfo.ownerClientId.Value == _clientId)
                        ?.ShowPseudoWithRevealedInfo(true);
                }
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
                    // Own-role reveal enforced at read time (GetCharacterInfo), not stamped here.
                    simulationsKnowledge[_id].TryAdd(_c.ownerClientId.Value, new CharacterInfoReveal());
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
