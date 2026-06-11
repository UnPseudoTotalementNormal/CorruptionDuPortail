using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    public class ChainingManager : NetworkBehaviour
    {
        public static ChainingManager instance;
        
        public NetworkList<ulong> chainingPlayers = new();
        public Power takeDownThePortalPowerDataObject;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }
        
        public void AddCharacterToChainingList(ulong _characterId)
        {
            if (!IsServer)
            {
                Debug.LogError("AddCharacterToChainingList can only be called on the server");
                return;
            }
            
            // Story 2.10 — the dedup/membership rule is a pure Domain POCO (ChainingResolver). The adapter snapshots
            // the replicated list and applies the decision; behavior identical to the previous Contains-guard.
            var _current = new List<ulong>();
            foreach (var _id in chainingPlayers)
            {
                _current.Add(_id);
            }

            if (new ChainingResolver().IsNewMember(_current, _characterId))
            {
                chainingPlayers.Add(_characterId);
            }
        }

        [Rpc(SendTo.Server)]
        public void ChainCharacterRpc(ulong _characterId)
        {
            var _gameManager = GameManager.For(NetworkManager);
            var _character = _gameManager.characterManager.GetCharacter(_characterId);
            
            _character.ChainCharacterServer();
            _gameManager.gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, false);
            
            if (_character.role.powers.Any(_p => _p.IsTheSamePower(takeDownThePortalPowerDataObject)))
            {
                var _portalState = (TakeDownThePortalState)_gameManager.GetGameStates(typeof(TakeDownThePortalState)).First();
                _portalState.shouldActivate = true;
                    
                _gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(TakeDownThePortalState.SetMageCharacterRpc),
                    new NetworkSerializableObject[] { new(_character.ownerClientId.Value) },
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            }
            
            _gameManager.characterManager.AskForUpdateAllCharactersRpc();
        }
    }
}