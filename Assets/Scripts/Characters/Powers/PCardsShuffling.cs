using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PCardsShuffling : Power, ICardsShufflingGuess, IDiscoveredAdd
    {
        public NetworkList<ulong> discoveredClientIds = new(); // List of client id role discovered or if discovered that it's not used
        private ulong currentRoleGuessClientId;

        // Powers-POCO v2: the guess-resolution logic lives in CardsShufflingDecision (pure). The guess
        // report (read) + the discovered-list write are power-local, exposed via ICardsShufflingGuess /
        // IDiscoveredAdd and reached through SelfState. The multi-step selection flow + fake-card branch stay
        // here. Behaviour-identical to the old inline GuessRoleRpc.
        private readonly CardsShufflingDecision _decision = new();
        private ulong _lastGuessClickedId;

        // Seed seam (story d'archi, catalogue 246-252): the fake-card copy pick goes through an INJECTABLE random
        // provider instead of a hard-wired new UnityRandomProvider(). Prod default = UnityRandomProvider; tests seed
        // it via reflection. Server-only path (GrantCopyFromFakeRole), so no client-side draw to diverge.
        private IRandomProvider _randomProvider = new UnityRandomProvider();

        bool ICardsShufflingGuess.IsCorrect =>
            characterManager.GetCharacter(_lastGuessClickedId).role.roleID
            == characterManager.GetCharacter(currentRoleGuessClientId).role.roleID;

        string ICardsShufflingGuess.ClickedPseudo =>
            lobbyPlayerInfoHolder.GetPlayerInfo(_lastGuessClickedId).playerName.ToString();

        string ICardsShufflingGuess.GuessRoleName =>
            characterManager.GetCharacter(currentRoleGuessClientId).role.roleName.ToString();

        IReadOnlyList<string> ICardsShufflingGuess.TargetedRoleNames =>
            roleTargetSystem.GetAllTargetingDataForTargeter(currentRoleGuessClientId)
                .Select(_td => characterManager.GetCharacter(_td.targetId).role.roleName.ToString())
                .ToList();

        void IDiscoveredAdd.DiscoveredAdd(int _slot) => discoveredClientIds.Add((ulong)_slot);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => !discoveredClientIds.Contains(ctx.targetId));
            // Design (2026-07-15): Luma ne pioche que des rôles élus (faction chosen).
            targetValidator.AddRule(ctx =>
            {
                Character _c = characterManager.GetCharacter(ctx.targetId, false);
                return _c != null && _c.role != null && _c.role.factionType == FactionType.chosen;
            });
        }
        
        private void OnRolePicked(Role _role)
        {
            ulong _clientIdClicked = _role.ownerClientId;
            if (!CheckIsTargetValid(_clientIdClicked, TargetUtils.TargetType.Role))
            {
                return;
            }

            currentRoleGuessClientId = _clientIdClicked;
            
            OnCharacterBarObjectClickedRpc(_clientIdClicked);
        }

        [Rpc(SendTo.Server)]
        private void OnCharacterBarObjectClickedRpc(ulong _clientIdClicked, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _clientIdClicked)) return; // NET-09: server-side use authorization
            // Re-validation autoritaire serveur : les règles d'OnRolePicked ont tourné côté client seulement,
            // mais cette branche ACCORDE désormais un pouvoir — un client trafiqué ne doit pas contourner les
            // règles (cible valide, non déjà découverte, faction élue). Rejette aussi les RPC en double (la 2e
            // arrive après discoveredClientIds.Add → règle !Contains) et les rôles null (règle faction).
            if (!ServerRoleTargetValid(_clientIdClicked))
            {
                // Never a silent return: the owner's picker is waiting for the next step.
                ServerCancelAuthorizedEffect();
                Debug.LogWarning($"[POWER] '{powerName}' of {ownerClientId.Value}: role of {_clientIdClicked} refused by the server");
                RoleClickRefusedRpc(characterManager.GetSafeRpcTarget(ownerClientId.Value));
                return;
            }

            Character _character = characterManager.GetCharacter(_clientIdClicked);
            if (_character.isFake)
            {
                // Rôle élu absent de la partie : Luma copie temporairement (one-shot) un de ses pouvoirs actifs.
                GrantCopyFromFakeRole(_character);
                discoveredClientIds.Add(_clientIdClicked);
                OnUsed();
                return; // carte fausse : la copie remplace l'ancien cul-de-sac, rien d'autre à faire
            }
            
            currentRoleGuessClientId = _character.ownerClientId.Value;
            AskForGuessRoleRpc(characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }
        
        // Server re-validation from the OWNER's point of view. The client validator goes through TargetUtils, which is
        // viewer-relative (it reads the LOCAL player's identity and knowledge): run on the server it judged the host's
        // view, so a client's click on the host's own role card was refused and the power hung. Same rules, the
        // owner as "self".
        private bool ServerRoleTargetValid(ulong _target)
        {
            Character _targetCharacter = characterManager.GetCharacter(_target, false);
            if (_targetCharacter == null || _targetCharacter.role == null)
            {
                return false;
            }
            if (discoveredClientIds.Contains(_target))
            {
                return false; // already discovered (also rejects a duplicated RPC)
            }
            if (_targetCharacter.role.factionType != FactionType.chosen)
            {
                return false;
            }
            return targetIncludeFlags.HasFlag(TargetIncludeFlags.Self) || _target != ownerClientId.Value;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void RoleClickRefusedRpc(RpcParams _rpcParams)
        {
            selectionFlowService.CancelSelection();
            Cancel();
        }

        private void OnGuessCharacterPicked(Character _character)
        {
            GuessRoleRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void GuessRoleRpc(ulong _clickedId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _clickedId)) return; // NET-09: server-side use authorization
            _lastGuessClickedId = _clickedId;
            // State feeds BOTH the context (the decision READS ctx.State<ICardsShufflingGuess>()) and the
            // runtime (DiscoveredAdd WRITES the discovered list) — both resolve to this carrier via SelfState.
            var _selfState = SelfState;
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_clickedId, state: _selfState), _selfState);
            // The use is consumed by the owner's OnUsed (OnGuessCharacterPicked): a second consume here counted it
            // twice on the host and was rejected ("consume with no use left") for a client.
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void AskForGuessRoleRpc(RpcParams _rpcParams)
        {
            Character _guessCharacter = characterManager.GetCharacter(currentRoleGuessClientId);

            selectionFlowService.StartCharacterSelection(null, OnGuessCharacterPicked,
                new SelectionFlowOptions
                {
                    focusType        = FocusType.Cards,
                    stepDescriptions = new[] { guessCharacterPickerDescription },
                    pinnedRole       = _guessCharacter.role
                });
        }

        [SerializeField] private string rolePickerDescription;
        [SerializeField] private string guessCharacterPickerDescription;

        // {0} = nom du rôle copié, {1} = nom du pouvoir copié.
        [SerializeField] private string copyObtainedMessage =
            "Le rôle {0} n'est pas en jeu : vous copiez temporairement son pouvoir « {1} » (usage unique).";
        [SerializeField] private string noActivePowerToCopyMessage =
            "Le rôle sélectionné n'est pas en jeu, mais n'a aucun pouvoir actif à copier.";

        // Server-only. Rôle élu piochée absent (fausse carte) : tire UN pouvoir actif au hasard et en donne une
        // copie one-shot à Luma. Réutilise le kernel pur StolenPowerSelector (pickCount 1) + GivePowerToCharacter
        // + la config one-shot d'Ugues. En fixant ownerIsChosen:true / ownerIsUgues:false, PowerCandidate.IsEligible
        // se réduit à (!isPassive && !isStolenCopy) = « pouvoir actif copiable ».
        private void GrantCopyFromFakeRole(Character _fakeCharacter)
        {
            List<Power> _powers = _fakeCharacter.role.powers;
            var _candidates = new List<PowerCandidate>(_powers.Count);
            foreach (Power _p in _powers)
            {
                bool _isPassive = _p == null || _p.BaseIsPassive;
                bool _isCopied = _p != null && _p.isCopiedPower.Value;
                _candidates.Add(new PowerCandidate(true, false, _isPassive, _isCopied));
            }

            List<int> _picks = StolenPowerSelector.SelectStealable(_candidates, 1, _randomProvider);
            string _message;
            if (_picks.Count == 0)
            {
                Debug.Log("[LUMA] Mélange des cartes : fausse carte élue sans pouvoir actif copiable.");
                _message = noActivePowerToCopyMessage;
            }
            else
            {
                Power _template = _powers[_picks[0]];
                // onReady = shared one-shot config (Power.ConfigureAsOneShotStolenCopy): spent copies despawn. A Mélange
                // stolen by Ugës: the copy it gives joins his Marque's per-night budget.
                characterManager.GivePowerToCharacter(ownerClientId.Value, _template,
                    InheritMarqueBudget(Power.ConfigureAsOneShotStolenCopy));
                _message = string.Format(copyObtainedMessage,
                    _fakeCharacter.role.roleName.ToString(), _template.powerName.ToString());
            }

            ChatMessage _chat = new ChatMessage
            {
                message = _message,
                senderClientId = ChatManager.SERVER_CLIENT_ID,
                chatId = (int)ChatWindowIDs.Server
            };
            chatManager.ReceiveChatMessageRpc(_chat, characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { rolePickerDescription } });
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }
    }
}
