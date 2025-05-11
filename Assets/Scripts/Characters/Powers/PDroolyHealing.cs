using System;
using System.Collections.Generic;
using System.Linq;
using Extensions;
using FocusSystem;
using GameLogic;
using Network;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    [Serializable]
    public class PDroolyHealing : Power
    {
        [NonSerialized] private Character clickedCharacter;
        [NonSerialized] private List<Character> alreadyHealedCharacters = new();
        
        [NonSerialized] public ulong[] healedCharacters = Enumerable.Repeat(GameValues.FAKE_CLIENT_ID, GameValues.MAX_PLAYERS).ToArray();
        
        public override void NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            base.NetworkSerialize(serializer);
            serializer.SerializeValue(ref healedCharacters);
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (alreadyHealedCharacters.Any(_c => _c.ownerClientId == _clickedCard.characterInfo.ownerClientId))
            {
                return;
            }
            
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            var _senderId = NetworkManager.Singleton.LocalClientId;
            GameManager.instance.DoPowerStaticMethodRpc(this.GetType().FullName, nameof(TryHealServerRpc),
                new[] {  
                    new NetworkSerializableObject(_senderId),
                    new NetworkSerializableObject(clickedCharacter.ownerClientId),
                    new NetworkSerializableObject(_character.role) }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            OnUsed();
        }
        
        private static void TryHealServerRpc(ulong _sender, ulong _healingCharacterId, Role _compareRole)
        {
            PDroolyHealing _power = (PDroolyHealing)GameManager.instance.GetCharacter(_sender).role.powers.First(_p => _p.GetType() == typeof(PDroolyHealing));
            if (_power.healedCharacters.Contains(_healingCharacterId))
            {
                Debug.Log("ALREADY HEALED");
                return;
            }
            
            var _choosedCharacter = GameManager.instance.GetCharacter(_healingCharacterId, false);
            if (_compareRole.IsTheSameRole(_choosedCharacter.role))
            {
                _power.healedCharacters[_power.healedCharacters.CountUsed(GameValues.FAKE_CLIENT_ID)] = _healingCharacterId;
                _choosedCharacter.HealPlayer();
                GameManager.instance.AskForUpdateAllCharactersRpc();
                GameManager.instance.DoPowerStaticMethodRpc(typeof(PDroolyHealing).FullName, nameof(OnHealRpc),
                    new[] { new NetworkSerializableObject(_choosedCharacter.ownerClientId) }, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.single,new[] {_sender} ));
            }
            else
            {
                //fail heal
            }
        }

        private static void OnHealRpc(ulong _healedCharacterId)
        {
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _healedCharacterId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
        }
        
        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }

            if (healedCharacters.CountUsed(GameValues.FAKE_CLIENT_ID) == GameManager.instance.GetCharacters(false).Count)
            {
                return false;
            }
            
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);

            alreadyHealedCharacters = GameManager.instance.GetCharacters(false)
                .Where(_c => healedCharacters.Contains(_c.ownerClientId)).ToList();
            
            Debug.Log(alreadyHealedCharacters.Count);

            foreach (var _alreadyHealedCharacter in alreadyHealedCharacters)
            {
                Card _card = BoardManager.instance.visibleCards.FirstOrDefault(_c => _c.characterInfo.ownerClientId == _alreadyHealedCharacter.ownerClientId);
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

        public override object Clone()
        {
            var _clonedPower = (PDroolyHealing)this.MemberwiseClone();
            _clonedPower.healedCharacters = (ulong[])this.healedCharacters.Clone();
            _clonedPower.alreadyHealedCharacters = alreadyHealedCharacters.ToList();
            return _clonedPower;
        }
    }
}