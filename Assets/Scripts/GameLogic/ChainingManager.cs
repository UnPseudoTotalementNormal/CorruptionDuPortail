using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public class ChainingManager : NetworkBehaviour
    {
        // Story 10.4 (Epic 10 / D4): the chaining powers were rerouted off this global onto an injected
        // chainingManager (Power base field, lane C) and VoteState onto its inherited GameState field
        // (lane-B push). Guard #1 now locks this global. The static backs only the composition-root
        // accessor that serves it (the one sanctioned locator, since the manager is not de-singletonised)
        // + the PlayMode harness assertions (not source-scanned). // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests
        public static ChainingManager instance;
        
        public NetworkList<ulong> chainingPlayers = new();
        public Power takeDownThePortalPowerDataObject;

        // Story 7.4 lane A: scene-wired CharacterManager + GameInfoRevealer, replacing the
        // GameManager.For hub-hops in ChainCharacterRpc. The GameManager.For game-loop reads
        // (GetGameStates / DoStateMethodRpc) stay until Epic 8 — this stays a mixed file (off the
        // guard registry). Both fields are NULL-TOLERANT (no init assert): they are used only in
        // ChainCharacterRpc, and many PlayMode harnesses create a bare ChainingManager via
        // AddComponent (AddCharacterToChainingList path) that never needs them — an eager assert
        // would false-fail those. Production wires both in GameScene (verified); proper wiring
        // coverage lands when this joins the registry in Epic 8.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1/9.2 (Epic 9 / D3): read + command slices of the scene-wired characterManager (D-NFR6
        // internal-narrowing). GetCharacter goes through Query, AskForUpdateAllCharactersRpc through Command.
        private ICharacterQuery Query => characterManager;
        private ICharacterCommand Command => characterManager;
        [SerializeField] private GameInfoRevealer gameInfoRevealer;

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

        // [LEAVE] _showCardReveal: normally FALSE — the ChainingState card animation performs the flip/reveal
        // itself, so the reveal is applied silently here to avoid a double flip. The mid-game leave path bypasses
        // ChainingState (no animation), so it passes TRUE: the reveal then drives GameInfoRevealer's own card
        // flip+reveal side-effect (the leaver's card is discovered), while the chained sprite still comes from
        // Card's isChained.OnValueChanged.
        [Rpc(SendTo.Server)]
        public void ChainCharacterRpc(ulong _characterId, bool _showCardReveal = false)
        {
            var _gameManager = GameManager.For(NetworkManager);
            var _character = Query.GetCharacter(_characterId);

            _character.ChainCharacterServer();
            gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, _showCardReveal);
            
            if (_character.role.powers.Any(_p => _p.IsTheSamePower(takeDownThePortalPowerDataObject)))
            {
                var _portalState = (TakeDownThePortalState)_gameManager.GetGameStates(typeof(TakeDownThePortalState)).First();
                _portalState.shouldActivate = true;
                    
                _gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(TakeDownThePortalState.SetMageCharacterRpc),
                    new NetworkSerializableObject[] { new(_character.ownerClientId.Value) },
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            }
            
            Command.AskForUpdateAllCharactersRpc();
        }
    }
}