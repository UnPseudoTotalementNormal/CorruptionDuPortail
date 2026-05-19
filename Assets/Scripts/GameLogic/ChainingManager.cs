using System;
using System.Linq;
using Characters.Powers;
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
            
            if (!chainingPlayers.Contains(_characterId))
            {
                chainingPlayers.Add(_characterId);
            }
        }

        [Rpc(SendTo.Server)]
        public void ChainCharacterRpc(ulong _characterId)
        {
            var _gameManager = GameManager.instance;
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