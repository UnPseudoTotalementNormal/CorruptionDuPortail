using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using Object = UnityEngine.Object;

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
        
        public void SetFocusOnType(FocusType _focusType, bool _stopOtherFocus = true)
        {
            if (_stopOtherFocus)
            {
                UnfocusAll();
            }
            
            switch (_focusType)
            {
                case FocusType.Characters:
                    foreach (var _characterBarObject in GameManager.instance.charactersBar.charactersBarObjects)
                    {
                        FocusObject(_characterBarObject.gameObject);
                    }
                    break;
                case FocusType.Cards:
                    foreach (var _card in BoardManager.instance.visibleCards)
                    {
                        FocusObject(_card.gameObject);
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