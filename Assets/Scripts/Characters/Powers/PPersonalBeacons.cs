using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers.PowerObjects;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using Extensions;
using GameLogic;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    public class PPersonalBeacons : Power
    {
        List<PersonalBeaconObject> personalBeacons = new();

        // Powers-POCO v2 in-place wiring (Phase 3): the passive robot reveal lives in PersonalBeaconsDecision
        // (pure, EditMode-tested) — reveal every Robot's forceCorruptOnRoleRevealed to the owner. It runs in
        // OnGameStartedServer (the base characterManager + ownerClientId are resolved there); the old code did
        // it in Awake, where neither was ready — a latent NRE this wiring also fixes. The beacon-object spawn
        // + the corrupted-beacon local chat cue stay power-local plumbing below.
        // PLAYTEST-REQUIRED before merge: engine instantiation of beacon objects (held-5 — see
        // spec-powers-poco-v2-architecture.md).
        private readonly PersonalBeaconsDecision _decision = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => personalBeacons.All(_p => _p.targetClientId != ctx.targetId));
        }

        private void Awake()
        {
            if (NetworkManager.IsServer)
            {
                onPowerReparented += OnPowerReparented;
            }
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, roster: Roster));
        }

        private void OnPowerReparented()
        {
            CreateBeaconRpc(ownerClientId.Value, true);
        }

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
        }

        private void OnCharacterPicked(Character _characterClicked)
        {
            if (!CheckIsTargetValid(_characterClicked.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }

            OnCharacterClickedRpc(_characterClicked.ownerClientId.Value);

            OnUsed();
        }
        
        [Rpc(SendTo.Server)]
        private void OnCharacterClickedRpc(ulong _characterClickedId)
        {
            roleTargetSystem.NewTargeting(ownerClientId.Value, _characterClickedId);
            
            CreateBeaconRpc(_characterClickedId, true);
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }

        [Rpc(SendTo.Everyone)]
        private void CreateBeaconRpc(ulong _targetClientId, bool _isVisibleOnCard)
        {
            PersonalBeaconObject _newBeacon = new(this, _targetClientId, characterManager);
            personalBeacons.Add(_newBeacon);
            
            // Only the owner processes beacon state changes. Simulated players on host must subscribe too.
            if (!characterManager.IsLocalOrSimulated(ownerClientId.Value)) 
            {
                return;
            }
            
            _newBeacon.onCorruptedBeaconChanged += (_newState) => OnCorruptedBeaconChanged(_newBeacon, _newState);
        }

        private void OnCorruptedBeaconChanged(PersonalBeaconObject _newBeacon, bool _newState)
        {
            Character _beaconedCharacter = characterManager.GetCharacter(_newBeacon.targetClientId);
            
            // Do not show local visual/chat cues if the Host is not currently possessing the owner
            if (characterManager.GetLocalClientId() != ownerClientId.Value) return;

            if (_beaconedCharacter.role.roleID == RoleID.Robot)
            {
                chatManager.AddMessageLocal($"Le robot a un nouvel état de corruption: {_newState}",
                    ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
                return;
            }
            
            if (_beaconedCharacter.role.factionType != FactionType.chosen)
            {
                return;
            }
            chatManager.AddMessageLocal($"{_newBeacon.targetClientId.GetPlayerName()} a un nouvel état de corruption: {_newState}", 
                ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
        }
    }
}