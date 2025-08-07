#region

using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Target;
using DG.Tweening;
using GameLogic;
using UnityEngine;

#endregion

namespace FocusSystem
{
    public class FocusManager : MonoBehaviour
    {
        public static FocusManager instance;
        
        [SerializeField] private CanvasGroup _focusCanvasGroup;
        
        public List<FocusObject> currentFocusObjects = new();
        
        private const int FOCUS_ORDER_IN_LAYER = 500;
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void UnfocusAll()
        {
            foreach (var _focusObject in currentFocusObjects.ToList())
            {
                UnfocusObject(_focusObject);
            }
        }
        
        public void SetFocusOnType(FocusType _focusType, TargetIncludeFlags _includeFlags = (TargetIncludeFlags)(-1), bool _stopOtherFocus = true)
        {
            if (_stopOtherFocus)
            {
                UnfocusAll();
            }
            
            switch (_focusType)
            {
                case FocusType.Roles:
                    List<ulong> _targetRoles = TargetUtils.GetTargetsForRoles(_includeFlags);
                    foreach (var _characterBarObject in GameManager.instance.charactersBar.charactersBarObjects)
                    {
                        if (_targetRoles.Contains(_characterBarObject.playerCharacter.ownerClientId))
                        {
                            FocusObject(_characterBarObject.gameObject);
                        }
                    }
                    break;
                case FocusType.Cards:
                    List<ulong> _targetChars = TargetUtils.GetTargetsForCharacters(_includeFlags);
                    foreach (var _card in BoardManager.instance.visibleCards)
                    {
                        if (_targetChars.Contains(_card.characterInfo.ownerClientId))
                        {
                            FocusObject(_card.gameObject);
                        }
                    }
                    break;
                case FocusType.Powers:
                    break;
            }
        }
        
        public void FocusObject(GameObject _gameObject)
        {
            if (currentFocusObjects.Any(_f => _f.gameObject == _gameObject))
            {
                return;
            }

            var _allCanvas = _gameObject.GetComponentsInChildren<Canvas>();
            foreach (var _canvas in _allCanvas)
            {
                _canvas.sortingOrder += FOCUS_ORDER_IN_LAYER;
            }
            
            var _newFocusObject = new FocusObject(_gameObject);
            
            currentFocusObjects.Add(_newFocusObject);

            if (currentFocusObjects.Count >= 1)
            {
                _focusCanvasGroup.DOKill();
                _focusCanvasGroup.DOFade(1, 0.25f);
            }
        }
        
        public bool IsFocused(GameObject _gameObject)
        {
            return currentFocusObjects.Any(_f => _f.gameObject == _gameObject);
        }
        
        public void UnfocusObject(GameObject _gameObject)
        {
            var _focusObject = currentFocusObjects.FirstOrDefault(_f => _f.gameObject == _gameObject);
            if (_focusObject == null)
            {
                return;
            }
            
            UnfocusObject(_focusObject);
        }

        private void UnfocusObject(FocusObject _orderObject)
        {
            GameObject _gameObject = _orderObject.gameObject;
            
            if (_gameObject == null)
            {
                return;
            }

            var _allCanvas = _gameObject.GetComponentsInChildren<Canvas>();

            foreach (var _canvas in _allCanvas)
            {
                _canvas.sortingOrder -= FOCUS_ORDER_IN_LAYER;
            }
            
            currentFocusObjects.Remove(_orderObject);
            
            if (currentFocusObjects.Count == 0)
            {
                _focusCanvasGroup.DOKill();
                _focusCanvasGroup.DOFade(0, 0.25f);
            }
        }
    }
    
    public class FocusObject
    {
        public GameObject gameObject;
        
        public FocusObject(GameObject _gameObject)
        {
            gameObject = _gameObject;
        }
    }
}