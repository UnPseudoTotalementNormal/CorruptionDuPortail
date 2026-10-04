#region

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using FocusSystem;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine.Assertions;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PVisionOfTheImpossible : Power, IVisionGuesses
    {
        public int charactersToSelect = 2;
        public int rolesToSelect = 2;

        [NonSerialized] private List<Character> clickedCharacters = new();
        [NonSerialized] private List<Role> clickedRoles = new();

        // Powers-POCO v2 in-place wiring (Phase 3): server logic lives in VisionOfTheImpossibleDecision
        // (pure, EditMode-tested, first-match-then-stop). The multi-guess is reduced to a VisionGuess list
        // exposed via IVisionGuesses (read by the decision through SelfState); the selection flow + the
        // server RPC plumbing stay adapter-side. The engine reads (role/pseudo) that feed the reduction stay
        // power-local here.
        // PLAYTEST-REQUIRED before merge (held-5 — see spec-powers-poco-v2-architecture.md).
        private readonly VisionOfTheImpossibleDecision _decision = new();

        // Snapshot of the picked (character id, guessed roles) forwarded by the server RPC, reduced on
        // demand into the VisionGuess list the decision consumes. Populated on the server before Decide.
        private ulong[] _pendingGuessedCharacterIds = Array.Empty<ulong>();
        private Role[] _pendingGuessedRoles = Array.Empty<Role>();

        IReadOnlyList<VisionGuess> IVisionGuesses.Guesses
        {
            get
            {
                var _guesses = new List<VisionGuess>(_pendingGuessedCharacterIds.Length);
                foreach (var _guessedCharacterId in _pendingGuessedCharacterIds)
                {
                    var _guessedCharacter = characterManager.GetCharacter(_guessedCharacterId, false);
                    bool _matches = _pendingGuessedRoles.Any(_r => _r.IsTheSameRole(_guessedCharacter.role));
                    _guesses.Add(new VisionGuess((int)_guessedCharacterId, _matches, _guessedCharacter.GetOwnerPseudo()));
                }
                return _guesses;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx =>
            {
                // Pour les personnages: ne pas inclure ceux déjà cliqués
                if (ctx.targetType == TargetUtils.TargetType.Character)
                {
                    return !clickedCharacters.Any(c => c.ownerClientId.Value == ctx.targetId);
                }
                // Pour les rôles: ne pas inclure ceux déjà cliqués
                else
                {
                    var character = characterManager.GetCharacter(ctx.targetId, false);
                    return character == null || !clickedRoles.Any(_r => _r.IsTheSameRole(character.role));
                }
            });
        }

        private void OnCharacterPicked(Character _clickedCharacter)
        {
            if (!CheckIsTargetValid(_clickedCharacter.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            clickedCharacters.Add(_clickedCharacter);
            if (clickedCharacters.Count >= charactersToSelect)
            {
                StartRoleSelection();
                return;
            }
            StartCharacterSelection();
        }

        [SerializeField] private string characterPickerDescription;
        [SerializeField] private string rolePickerDescription;

        private void StartCharacterSelection()
        {
            selectionFlowService.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions
                {
                    focusType        = FocusType.Cards,
                    stepDescriptions = new[] { characterPickerDescription },
                });
        }

        private void OnRolePicked(Role _roleClicked)
        {
            if (!CheckIsTargetValid(_roleClicked.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            clickedRoles.Add(_roleClicked);
            if (clickedRoles.Count >= rolesToSelect)
            {
                focusManager.UnfocusAll();

                OnUsed();

                OnVisionGuessServerRpc(
                    clickedCharacters.Select(_c => _c.ownerClientId.Value).ToArray(),
                    clickedRoles.ToArray());
                return;
            }

            StartRoleSelection();
        }

        private void StartRoleSelection()
        {
            selectionFlowService.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { rolePickerDescription } });
        }

        [Rpc(SendTo.Server)]
        private void OnVisionGuessServerRpc(ulong[] _guessedCharacterIds, Role[] _guessedRoles, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _guessedCharacterIds)) return; // NET-09: server-side use authorization
            Assert.IsTrue(NetworkManager.IsServer, "OnVisionGuessServerRpc should only be called on server");
            _pendingGuessedCharacterIds = _guessedCharacterIds;
            _pendingGuessedRoles = _guessedRoles;
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, state: SelfState), SelfState);
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }

            return true;
        }

        public override void StartUse()
        {
            base.StartUse();

            clickedCharacters.Clear();
            clickedRoles.Clear();

            StartCharacterSelection();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
            focusManager.UnfocusAll();
        }
    }
}
