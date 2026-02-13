using System;
using System.Collections.Generic;
using System.Linq;
using Board;
using Characters;
using DG.Tweening;
using GameLogic;
using GameLogic.Validation;
using TransformComposition;
using Unity.VisualScripting;
using UnityEngine;
using static Characters.Powers.Target.TargetUtils;

namespace UI.BoardUI
{
    public class CardPickerManager : MonoBehaviour
    {
        public static CardPickerManager instance;
        
        public event Action<Character> onCharacterSelected;
        public event Action<Role> onRoleSelected;
        
        private List<Action<Role>> subscribedRoleCallbacks = new();
        private List<Action<Character>> subscribedCharacterCallbacks = new();

        [Header("Role picker settings")] 
        [SerializeField] private float rolePickerCardSpacingAngle = 5; // The angle which will be added to each card to make an arc of cards.
        [SerializeField] private float rolePickerCardSpacing = 1;
        [SerializeField] private float rolePickerCardHeightOffset = -0.1f;
        [SerializeField] private Transform rolePickerCenter;

        private void Awake()
        {
            instance = this;
        }
        
        [ContextMenu("ShowCharacterPicker")]
        public void TestRolePicker()
        {
            // Validator that allows all roles
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(ctx => ctx.targetType == TargetType.Role);
            ShowRolePicker(_validator, role => { Debug.Log("Selected role: " + role.roleName); });
        }
        
        public void ShowRolePicker(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _callback)
        {
            onRoleSelected += _callback;
            subscribedRoleCallbacks.Add(_callback);
            
            List<Character> _distinctRoles = CharacterManager.instance.GetCharacters()
                .DistinctBy(c => c.role.roleID)
                .ToList();
            List<Character> _validRoles = _distinctRoles.Where(_role =>
                _validator.Evaluate((_role.ownerClientId.Value, TargetType.Role))).ToList();
            List<Card> _cards = new();
            
            float totalAngle = (_validRoles.Count - 1) * rolePickerCardSpacingAngle;
            float startAngle = -totalAngle / 2f;
            
            for (var i = 0; i < _validRoles.Count; i++)
            {
                Character _validRole = _validRoles[i];
                _cards.Add(BoardManager.instance.AddNewCard(_validRole, false));
                TransformLayer transformLayer = _cards[i].GetTransformCompositor().GetLayer("RolePicker");
                
                float _angle = startAngle + (i * rolePickerCardSpacingAngle);
                
                float angleRad = _angle * Mathf.Deg2Rad;
                
                float radius = rolePickerCardSpacing * _validRoles.Count * 0.5f;
                float xPos = Mathf.Sin(angleRad) * radius;
                float zPos = (Mathf.Cos(angleRad) * radius) - radius;
                
                Vector3 targetPosition = rolePickerCenter.localPosition + new Vector3(xPos, rolePickerCardHeightOffset * i, zPos);
                
                transformLayer.DOLocalRotate(new Vector3(0, _angle, 0), 0.5f).SetEase(Ease.OutQuint);
                transformLayer.DOLocalMove(targetPosition, 0.5f).SetEase(Ease.OutQuint);
                _cards[i].onCardClicked += (clicked) =>
                {
                    onRoleSelected?.Invoke(clicked.roleInfo);
                };
            }
        }

        public void ShowCharacterPicker(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _callback)
        {
            onCharacterSelected += _callback;
            subscribedCharacterCallbacks.Add(_callback);
            // Get all cards that satisfy the validator
            List<Card> _cards = BoardManager.instance.visibleCards;
            List<Card> _validCards = _cards.Where(_card =>
                _validator.Evaluate((_card.characterInfo.ownerClientId.Value, TargetType.Character))).ToList();
            foreach (Card _validCard in _validCards)
            {
                _validCard.GetTransformCompositor().GetLayer("CharacterPicker").DOLocalMoveY(1.5f, 0.5f).SetEase(Ease.OutQuint);
            }
        }

        public void CancelPicker()
        {
            foreach (Action<Role> _subscribedCallback in subscribedRoleCallbacks.ToList())
            {
                onRoleSelected -= _subscribedCallback;
                subscribedRoleCallbacks.Remove(_subscribedCallback);
            }

            foreach (Action<Character> _subscribedCallback in subscribedCharacterCallbacks.ToList())
            {
                onCharacterSelected -= _subscribedCallback;
                subscribedCharacterCallbacks.Remove(_subscribedCallback);
            }
            
            //Picker reset
            List<Card> _cards = BoardManager.instance.visibleCards;
            foreach (Card _card in _cards)
            {
                _card.GetTransformCompositor().GetLayer("CharacterPicker").DOLocalMoveY(0, 0.5f).SetEase(Ease.OutQuint);
            }
        }
    }
}
