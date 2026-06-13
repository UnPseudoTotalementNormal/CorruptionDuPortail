using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using GameLogic;
using UnityEngine;
using UnityEngine.Assertions;

namespace Board
{
    public class CardEffectManager : MonoBehaviour
    {
        public static CardEffectManager instance;

        // Story 10.3 lane A: scene-wired BoardManager (still a singleton), replacing the global
        // board-singleton read in the card-spawn subscription + effect creation.
        [SerializeField] private BoardManager boardManager;

        [SerializeField] private SerializedDictionary<CardEffectID, CardEffectSettings> cardEffects = new();
        
        private Dictionary<ulong, List<CardEffectID>> cardEffectsByCardId = new();
        private Dictionary<(CardEffectID, ulong), object> pendingEffectData = new();
        
        private HashSet<CardEffectInfo> currentCardEffects = new();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public void Start()
        {
            Assert.IsNotNull(boardManager, "CardEffectManager.boardManager is not wired — wire it in GameScene (the composition root).");
            boardManager.onCardSpawned += OnCardSpawned;
        }

        private void OnCardSpawned(Card _cardSpawned)
        {
            Character _character = _cardSpawned.characterInfo;

            if (_character == null || !cardEffectsByCardId.TryGetValue(_character.ownerClientId.Value, out List<CardEffectID> _cardEffects))
            {
                return;
            }
            foreach (CardEffectID _cardEffectID in _cardEffects)
            {
                CreateCardEffect(_cardEffectID, _character.ownerClientId.Value);
            }
        }
        
        public void AddCardEffect(CardEffectID _cardEffectID, Card _card, object _effectData = null)
        {
            AddCardEffect(_cardEffectID, _card.characterInfo.ownerClientId.Value, _effectData);
        }

        public void AddCardEffect(CardEffectID _cardEffectID, ulong _targetId, object _effectData = null)
        {
            if (!cardEffects.ContainsKey(_cardEffectID))
            {
                Debug.LogError($"CardEffectManager: AddCardEffect: No card effect found for ID {_cardEffectID}");
                return;
            }
            
            if (!cardEffectsByCardId.ContainsKey(_targetId))
            {
                cardEffectsByCardId[_targetId] = new List<CardEffectID>();
            }
            
            if (cardEffectsByCardId[_targetId].Contains(_cardEffectID))
            {
                Debug.LogWarning($"CardEffectManager: Card effect {_cardEffectID} already exists on target {_targetId}");
                return;
            }
            
            cardEffectsByCardId[_targetId].Add(_cardEffectID);
            
            // Store effect data for later use if provided
            if (_effectData != null)
            {
                pendingEffectData[(_cardEffectID, _targetId)] = _effectData;
            }
            
            CreateCardEffect(_cardEffectID, _targetId);
        }
        
        public void RemoveCardEffect(CardEffectID _cardEffectID, ulong _targetId)
        {
            if (!cardEffectsByCardId.ContainsKey(_targetId))
            {
                return;
            }
            
            cardEffectsByCardId[_targetId].Remove(_cardEffectID);
            
            if (cardEffectsByCardId[_targetId].Count == 0)
            {
                cardEffectsByCardId.Remove(_targetId);
            }
            
            // Destroy visuals
            CardEffectInfo _effectToRemove = null;
            foreach (var _effect in currentCardEffects)
            {
                if (_effect.cardEffectID == _cardEffectID && _effect.targetCard.characterInfo.ownerClientId.Value == _targetId)
                {
                    _effectToRemove = _effect;
                    break;
                }
            }
            
            if (_effectToRemove != null)
            {
                if (_effectToRemove.cardEffectComponent != null)
                {
                    Destroy(_effectToRemove.cardEffectComponent.gameObject);
                }
                currentCardEffects.Remove(_effectToRemove);
            }
        }

        private void CreateCardEffect(CardEffectID _cardEffectID, ulong _targetId)
        {
            Card _card = boardManager.visibleCards.Find(_c => _c.characterInfo.ownerClientId.Value == _targetId);
            if (_card == null)
            {
                return;
            }
            
            // Validate card effect exists in dictionary
            if (!cardEffects.ContainsKey(_cardEffectID))
            {
                Debug.LogError($"CardEffectManager: CreateCardEffect: No card effect found for ID {_cardEffectID}");
                return;
            }
            
            // Validate prefab is not null
            if (cardEffects[_cardEffectID].cardEffectPrefab == null)
            {
                Debug.LogError($"CardEffectManager: CreateCardEffect: Card effect prefab for ID {_cardEffectID} is null. Please assign it in the inspector.");
                return;
            }
            
            // Retrieve effect data if available
            object _effectData = null;
            var _key = (_cardEffectID, _targetId);
            if (pendingEffectData.TryGetValue(_key, out var _data))
            {
                _effectData = _data;
                pendingEffectData.Remove(_key); // Clean up after use
            }
            
            CardEffectComponent _cardEffectComponent = Instantiate(cardEffects[_cardEffectID].cardEffectPrefab, _card.visualComponents.cardEffectsParent);
            _cardEffectComponent.Initialize(_card, _effectData);
            
            CardEffectInfo _cardEffectInfo = new CardEffectInfo(_cardEffectID, _card, _cardEffectComponent, _effectData);
            currentCardEffects.Add(_cardEffectInfo);

            _cardEffectComponent.onObjectDestroyed += () => currentCardEffects.Remove(_cardEffectInfo);
        }

        [Serializable]
        private struct CardEffectSettings
        {
            public CardEffectComponent cardEffectPrefab;
        }
    }

    public class CardEffectInfo
    {
        public CardEffectID cardEffectID;
        public Card targetCard;
        public CardEffectComponent cardEffectComponent;
        public object effectData;

        public CardEffectInfo(CardEffectID _cardEffectID, Card _targetCard, CardEffectComponent _cardEffectComponent, object _effectData = null)
        {
            cardEffectID = _cardEffectID;
            targetCard = _targetCard;
            cardEffectComponent = _cardEffectComponent;
            effectData = _effectData;
        }
    }
}