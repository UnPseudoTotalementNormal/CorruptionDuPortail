#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.Target;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PDroolyHealing : Power
    {
        [NonSerialized] private Character clickedCharacter;
        [NonSerialized] private List<Character> alreadyHealedCharacters = new();
        
        [NonSerialized] public ulong[] healedCharacters = Enumerable.Repeat(GameValues.FAKE_CLIENT_ID, GameValues.MAX_PLAYERS).ToArray();
        
        public EventReference onHealSuccessfulSound;
        public EventReference onHealFailedSound;
        
        public override void NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            base.NetworkSerialize(serializer);
            serializer.SerializeValue(ref healedCharacters);
            onHealSuccessfulSound.NetworkSerialize(serializer);
            onHealFailedSound.NetworkSerialize(serializer);
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (alreadyHealedCharacters.Any(_c => _c.ownerClientId == _clickedCard.characterInfo.ownerClientId))
            {
                return;
            }
            
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                return;
            }
            
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(_character.ownerClientId.Value))
            {
                return;
            }
            
            var _senderId = NetworkManager.Singleton.LocalClientId;
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(TryHealServerRpcc),
                new[] {  
                    new NetworkSerializableObject(_senderId),
                    new NetworkSerializableObject(clickedCharacter.ownerClientId.Value),
                    new NetworkSerializableObject(_character.role) }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            OnUsed();
        }
        
        private void TryHealServerRpcc(ulong _sender, ulong _healingCharacterId, Role _compareRole)
        {
            RoleTargetSystem.instance.NewTargeting(_sender, _healingCharacterId);
            PDroolyHealing _power = (PDroolyHealing)GameManager.instance.characterManager.GetCharacter(_sender).role.powers.First(_p => _p.GetType() == typeof(PDroolyHealing));
            if (_power.healedCharacters.Contains(_healingCharacterId))
            {
                Debug.Log("ALREADY HEALED");
                return;
            }
            
            var _choosedCharacter = GameManager.instance.characterManager.GetCharacter(_healingCharacterId, false);
            bool _healSuccess = false;
            if (_compareRole.IsTheSameRole(_choosedCharacter.role))
            {
                _power.healedCharacters[_power.healedCharacters.CountUsed(GameValues.FAKE_CLIENT_ID)] = _healingCharacterId;

                if (_choosedCharacter.isCorrupted.Value)
                {
                    _healSuccess = true;
                    _choosedCharacter.HealPlayer();
                    GameManager.instance.characterManager.AskForUpdateAllCharactersRpc();
                }
                GameManager.instance.DoPowerMethodRpc(_sender, this, nameof(OnHealSuccessfulRpc),
                    new[] { new NetworkSerializableObject(_choosedCharacter.ownerClientId.Value) }, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.single,new[] {_sender} ));
            }
            
            GameAudioManager.instance.PlayOneShotRpc(
                _healSuccess? onHealSuccessfulSound.GetPath() : onHealFailedSound.GetPath(), 
                NetworkManager.Singleton.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
        }

        private void OnHealSuccessfulRpc(ulong _healedCharacterId)
        {
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _healedCharacterId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
        }

        public List<Character> GetIgnoreCharacters()
        {
            return GameManager.instance.characterManager.GetCharacters(false)
                .Where(_c => healedCharacters.Contains(_c.ownerClientId.Value))
                .ToList();
        }
        
        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }

            if (healedCharacters.CountUsed(GameValues.FAKE_CLIENT_ID) == GameManager.instance.characterManager.GetCharacters(false).Count)
            {
                return false;
            }
            
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);

            alreadyHealedCharacters = GameManager.instance.characterManager.GetCharacters(false)
                .Where(_c => healedCharacters.Contains(_c.ownerClientId.Value)).ToList();
            
            Debug.Log(alreadyHealedCharacters.Count);

            foreach (var _alreadyHealedCharacter in alreadyHealedCharacters)
            {
                Card _card = BoardManager.instance.visibleCards.FirstOrDefault(_c => _c.characterInfo.ownerClientId.Value == _alreadyHealedCharacter.ownerClientId.Value);
                if (_card == null)
                {
                    continue;
                }
                FocusManager.instance.UnfocusObject(_card.gameObject);
            }
            
            clickedCharacter = null;
        }

        public override void OnUsed()
        {
            base.OnUsed();
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }
    }
}