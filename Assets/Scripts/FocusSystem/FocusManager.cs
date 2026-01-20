#region

using System;
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
        [SerializeField] private ParticleSystem _focusParticlePrefab;
        
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
        
        public void SetFocusOnType(FocusType _focusType, Func<ulong, TargetUtils.TargetType, bool> _checkValidFunc)
        {
            UnfocusAll();
            
            switch (_focusType)
            {
                case FocusType.Roles:
                    foreach (var _characterBarObject in GameManager.instance.charactersBar.charactersBarObjects)
                    {
                        if (_checkValidFunc(_characterBarObject.playerCharacter.ownerClientId.Value, TargetUtils.TargetType.Role))
                        {
                            FocusObject(_characterBarObject.gameObject);
                        }
                    }
                    break;
                case FocusType.Cards:
                    foreach (var _card in BoardManager.instance.visibleCards)
                    {
                        if (_checkValidFunc(_card.characterInfo.ownerClientId.Value, TargetUtils.TargetType.Character))
                        {
                            FocusObject(_card.gameObject);
                        }
                    }
                    break;
                case FocusType.Powers:
                    break;
            }
        }

        public void SetFocusOnType(FocusType _focusType, Func<ulong, bool> _checkValidFunc, bool _stopOtherFocus = true)
        {
            if (_stopOtherFocus)
            {
                UnfocusAll();
            }
            
            switch (_focusType)
            {
                case FocusType.Roles:
                    foreach (var _characterBarObject in GameManager.instance.charactersBar.charactersBarObjects)
                    {
                        if (_checkValidFunc(_characterBarObject.playerCharacter.ownerClientId.Value))
                        {
                            FocusObject(_characterBarObject.gameObject);
                        }
                    }
                    break;
                case FocusType.Cards:
                    foreach (var _card in BoardManager.instance.visibleCards)
                    {
                        if (_checkValidFunc(_card.characterInfo.ownerClientId.Value))
                        {
                            FocusObject(_card.gameObject);
                        }
                    }
                    break;
                case FocusType.Powers:
                    break;
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
                        if (_targetRoles.Contains(_characterBarObject.playerCharacter.ownerClientId.Value))
                        {
                            FocusObject(_characterBarObject.gameObject);
                        }
                    }
                    break;
                case FocusType.Cards:
                    List<ulong> _targetChars = TargetUtils.GetTargetsForCharacters(_includeFlags);
                    foreach (var _card in BoardManager.instance.visibleCards)
                    {
                        if (_targetChars.Contains(_card.characterInfo.ownerClientId.Value))
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
            
            //focus particles
            if (_focusParticlePrefab == null)
            {
                return;
            }

            ParticleSystem _focusParticles = Instantiate(_focusParticlePrefab);
            _newFocusObject.focusParticles = _focusParticles;
            
            var _transformFollower = _focusParticles.gameObject.AddComponent<TransformFollower>();
            _transformFollower.transformToFollow = _gameObject.transform;
            _transformFollower.offset = Vector3.up * 0.1f;

            var _particleUpdater = _focusParticles.gameObject.GetComponent<FocusParticleUpdater>();
            _particleUpdater.targetObject = _gameObject;

            var _shape = _focusParticles.shape;
            _shape.shapeType = ParticleSystemShapeType.BoxEdge;

            _focusParticles.Play();
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
            
            if (_orderObject.focusParticles)
            {
                _orderObject.focusParticles.Stop();
                Destroy(_orderObject.focusParticles.gameObject, 5f);
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
        public ParticleSystem focusParticles;
        
        public FocusObject(GameObject _gameObject)
        {
            gameObject = _gameObject;
        }
    }
}