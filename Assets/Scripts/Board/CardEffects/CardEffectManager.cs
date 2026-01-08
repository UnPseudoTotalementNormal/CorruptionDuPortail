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
                CreateCardEffect(_cardEffectID, _cardSpawned);
            }
        }
        
        public void CreateCardEffect(CardEffectID _cardEffectID, Card _card)
        {
            if (!cardEffects.ContainsKey(_cardEffectID))
            {
                Debug.LogError($"CardEffectManager: CreateCardEffect: No card effect found for ID {_cardEffectID}");
                return;
            }
            
            CardEffectComponent _cardEffectComponent = Instantiate(cardEffects[_cardEffectID].cardEffectPrefab, _card.transform);
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