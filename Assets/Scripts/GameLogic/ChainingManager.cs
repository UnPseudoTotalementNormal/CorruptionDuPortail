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
        public NetworkList<ulong> chainingPlayers = new();
        public Power takeDownThePortalPowerDataObject;
        
        [Rpc(SendTo.Server)]
        public void ChainCharacterRpc(ulong _characterId)
        {
            var _gameManager = GameManager.instance;
            var _character = _gameManager.characterManager.GetCharacter(_characterId);
            
            _character.isChained.Value = true;
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