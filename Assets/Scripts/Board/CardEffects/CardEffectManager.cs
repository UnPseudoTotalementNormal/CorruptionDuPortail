using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using GameLogic;
using UnityEngine;

namespace Board
{
    public class CardEffectManager : MonoBehaviour
    {
        public static CardEffectManager instance;
        
        [SerializeField] private SerializedDictionary<CardEffectID, CardEffectSettings> cardEffects = new();
        
        private Dictionary<ulong, List<CardEffectID>> cardEffectsByCardId = new();
        
        private HashSet<CardEffectInfo> currentCardEffects = new();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                instance = this;
            }
        }

        public void Start()
        {
            BoardManager.instance.onCardSpawned += OnCardSpawned;
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
        
        public void AddCardEffect(CardEffectID _cardEffectID, Card _card)
        {
            AddCardEffect(_cardEffectID, _card.characterInfo.ownerClientId.Value);
        }

        public void AddCardEffect(CardEffectID _cardEffectID, ulong _targetId)
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
            Card _card = BoardManager.instance.visibleCards.Find(_c => _c.characterInfo.ownerClientId.Value == _targetId);
            if (_card == null)
            {
                return;
            }
            
            CardEffectComponent _cardEffectComponent = Instantiate(cardEffects[_cardEffectID].cardEffectPrefab, _card.cardEffectsParent);
            _cardEffectComponent.Initialize(_card);
            
            CardEffectInfo _cardEffectInfo = new CardEffectInfo(_cardEffectID, _card, _cardEffectComponent);
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

        public CardEffectInfo(CardEffectID _cardEffectID, Card _targetCard, CardEffectComponent _cardEffectComponent)
        {
            cardEffectID = _cardEffectID;
            targetCard = _targetCard;
            cardEffectComponent = _cardEffectComponent;
        }
    }
}