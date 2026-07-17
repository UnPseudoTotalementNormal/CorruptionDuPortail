using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using Characters.Powers.Interfaces;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager instance;

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop and the
        // CharacterManager static-locator lookup.
        [SerializeField] private CharacterManager characterManager;
        // Story 8.3 lane A: scene-wired GameManager, narrowed to the loop slices — IGameLoop (onGameStarted /
        // hasGameStarted) + IGameStateQuery (GetGameState / currentGameStateIndex), replacing the static locator.
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;
        private IGameStateQuery Query => gameManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void Start()
        {
            Assert.IsNotNull(characterManager, "PowerManager.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "PowerManager.gameManager is not wired — wire it in GameScene (the composition root).");
            Power.onPowerSpawned += OnPowerSpawned;

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }
            //server only

            Loop.onGameStarted += OnGameStarted;
        }

        private void OnPowerSpawned(Power _newPower)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (Loop.hasGameStarted)
            {
                _newPower.OnGameStartedServer();
            }

            _newPower.onPowerUsed += () => { OnPowerUsedServer(_newPower); };
        }

        private void OnPowerUsedServer(Power _newPower)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (Query.GetGameState(Query.currentGameStateIndex.Value) is AwakeningState _awakeningState)
            {
                _awakeningState.OnPowerUsedServer(_newPower);
            }
        }

        private void OnGameStarted()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnGameStarted should only be called on the server");
            
            // Snapshot before iterating: OnGameStartedServer can reparent powers (e.g. Marque d'Hurluberluges steals
            // powers at game-start → Power.Reparent mutates role.powers), which would invalidate a lazy SelectMany
            // enumerator → "Collection was modified". ToList() captures each power present at game-start exactly once.
            foreach (var _rolePower in characterManager.GetCharacters().SelectMany(_character => _character.role.powers).ToList())
            {
                if (characterManager.GetCharacter(_rolePower.ownerClientId.Value, false).isFake)
                {
                    continue;
                }
                _rolePower.OnGameStartedServer();
            }

            foreach (var _character in characterManager.GetCharacters())
            {
                _character.onCharacterAwakened += () => OnCharacterAwakenedServer(_character);
                foreach (Power _characterPower in _character.role.powers)
                {
                    
                }
            }
        }

        private void OnCharacterAwakenedServer(Character _character)
        {
            
        }
        
        public void ReparentPowerToCharacterServer(Power _power, Character _newOwner)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("ReparentPowerToCharacterServer should only be called on the server");
                return;
            }
            
            if (_power.ownerCharacter == _newOwner)
            {
                Debug.LogWarning($"Power {_power.powerName} is already owned by character {_newOwner.ownerClientId.Value}");
                return;
            }

            if (_power.ownerCharacter != null)
            {
                RemovePowerFromCharacterPowerListRpc(_power.ownerClientId.Value, new(_power));
            }

            _power.GetComponent<NetworkObject>().TrySetParent(_newOwner.GetComponent<NetworkObject>());
            
            _power.ownerClientId.Value = _newOwner.ownerClientId.Value;
            OnPowerReparentedServer(_power);
        }

        public void OnPowerReparentedServer(Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnPowerReparented should only be called on the server");

            _power.OnReparentedServer();
        }
        
        [Rpc(SendTo.Everyone)]
        public void RemovePowerFromCharacterPowerListRpc(ulong _characterId, NetworkBehaviourReference _powerNetworkRef)
        {
            Character _character = characterManager.GetCharacter(_characterId);
            if (_character == null || _character.role == null)
            {
                return;
            }

            // Tolerate an already-despawned reference. This RPC (SendTo.Everyone) races the NetworkObject.Despawn
            // that follows it in RemovePowerFromCharacter: the HOST processes its own copy AFTER the synchronous
            // despawn, so TryGet fails there; a remote client that handled the despawn message first is in the same
            // boat. In that case drop any dead entries so no fake-null husk lingers in role.powers.
            bool _changed;
            if (_powerNetworkRef.TryGet(out Power _power) && _power != null)
            {
                _changed = _character.role.powers.Remove(_power);
            }
            else
            {
                _changed = _character.role.powers.RemoveAll(_p => !_p) > 0;
            }

            // Symmetric with the add path (Character populates role.powers then fires onPowersUpdated): the power
            // bar subscribes to this event, so a removal (spent one-shot copy, reparent) refreshes it. Only fire
            // when the list actually changed to avoid a redundant rebuild.
            if (_changed)
            {
                _character.InvokeOnPowersUpdated();
            }
        }

        private void OnDestroy()
        {
            Power.onPowerSpawned -= OnPowerSpawned;

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}