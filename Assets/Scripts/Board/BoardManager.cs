#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

public class BoardManager : NetworkBehaviour
{
    public static BoardManager instance;
    public Card cardPrefab;
    
    public Transform spawnCardPosition;
    public Transform maxCardPosition; //cards will overflow past this point
    
    public List<Card> visibleCards = new();
    
    public List<CancellationTokenSource> cancelTokens = new();

    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;
    
    public const float CARD_SPACING = 7;
    public const float CARD_LINE_SPACING = 9;
    
    
    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        GameManager.instance.characterManager.onCharactersListUpdated += OnCharacterListUpdated;
    }

    private void OnCharacterListUpdated(List<Character> _characters)
    {
        UpdateCards(_characters);
    }

    private void UpdateCards(List<Character> _characters)
    {
        foreach (var _character in _characters)
        {
            Card _card = visibleCards.FirstOrDefault(_char => _char.characterInfo.ownerClientId.Value == _character.ownerClientId.Value);
            if (_card)
            {
                _card.characterInfo = _character;
            }
        }
    }

    private void OnStartingNewAnim(CancellationTokenSource _cancelToken, bool _stopOtherAnims = true)
    {
        if (_stopOtherAnims)
        {
            foreach (var _token in cancelTokens.ToList())
            {
                _token.Cancel();
            }
            cancelTokens.Clear();
        }
        
        cancelTokens.Add(_cancelToken);
    }
    
    #region Animations
    
    public void PlaceAllCardsToPosition()
    {
        for (var _i = 0; _i < visibleCards.Count; _i++)
        {
            var _card = visibleCards[_i];
            //_card.transform.DOLocalMove(new Vector3(spawnCardPosition.localPosition.x + _i * CARD_SPACING, 0, 0), 0.5f);
            Vector3 _localTargetPosition = GetCardPlacedPosition(_i);
            
            _card.transform.DOLocalMoveX(_localTargetPosition.x, 0.5f);
            _card.transform.DOLocalMoveZ(_localTargetPosition.z, 0.5f);
        }
    }

    private Vector3 GetCardPlacedPosition(int _cardIndex)
    {
        var _position = new Vector3(spawnCardPosition.localPosition.x, spawnCardPosition.localPosition.y, spawnCardPosition.localPosition.z);
        while (_cardIndex > 0)
        {
            _position += new Vector3(CARD_SPACING, 0, 0);
            if (_position.x >= maxCardPosition.localPosition.x)
            {
                _position = new Vector3(spawnCardPosition.localPosition.x, _position.y, _position.z - CARD_LINE_SPACING);
            }
            
            _cardIndex -= 1;
        }

        return _position;
    }

    public async UniTask ShowAllPlayerCards(bool _stopOtherAnims = true)
    {
        CancellationTokenSource _cancelToken = new();
        OnStartingNewAnim(_cancelToken, _stopOtherAnims);
        
        await HideAllCards(false);
        _cancelToken.Token.ThrowIfCancellationRequested();
        
        foreach (var _character in GameManager.instance.characterManager.GetCharacters().Where(_c => !_c.isFake))
        {
            Card _card = AddNewCard();
            _card.SetInfo(_character);
            _ = _card.ShowPseudoWithRevealedInfo();
            _card.cardPivotTransform.eulerAngles = new Vector3(0, 0, -180);
            
            _ = _card.ShowFrontSide();
        }
        PlaceAllCardsToPosition();
    }
    
    public async UniTask HideAllCards(bool _stopOtherAnims = true)
    {
        CancellationTokenSource _cancelToken = new();
        OnStartingNewAnim(_cancelToken, _stopOtherAnims);
        
        if (visibleCards.Count == 0)
        {
            return;
        }
        
        foreach (var _card in visibleCards)
        {
            _ = _card.ShowBackSide();
        }

        await UniTask.Delay(TimeSpan.FromSeconds(visibleCards[0].rotateTime), cancellationToken: _cancelToken.Token);
        
        foreach (var _card in visibleCards)
        {
            _card.transform.DOLocalMove(new Vector3(0, _card.transform.localPosition.y, 0), 0.5f).SetEase(Ease.OutQuint);
        }

        await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _cancelToken.Token);
        
        foreach (var _card in visibleCards.ToList())
        {
            DestroyCard(_card);
        }
    }

    #endregion
    
    public void DestroyCard(Card _card)
    {
        _card.onCardClicked -= onCardClicked;
        _card.onCardHovered -= onCardHovered;
        _card.onCardUnhovered -= onCardUnhovered;
        visibleCards.Remove(_card);
        Destroy(_card.gameObject);
    }
    
    public Card AddNewCard()
    {
        Card _card = Instantiate(cardPrefab, transform);
        _card.transform.localPosition = new Vector3(0, 0, 0);
        
        visibleCards.Add(_card);

        _card.onCardClicked += OnCardClicked;
        _card.onCardHovered += OnCardHovered;
        _card.onCardUnhovered += OnCardUnhovered;
        return _card;
    }
    
    private void OnCardClicked(Card _card)
    {
        onCardClicked?.Invoke(_card);
    }
    
    private void OnCardHovered(Card _card)
    {
        onCardHovered?.Invoke(_card);
    }
    
    private void OnCardUnhovered(Card _card)
    {
        onCardUnhovered?.Invoke(_card);
    }

    [Rpc(SendTo.Everyone)]
    public void UpdateCardChainStatusRpc(ulong _clientId, bool _instant = false)
    {
        var _card = visibleCards.Find(_c => _c.characterInfo.ownerClientId.Value == _clientId);
        _card?.UpdateChainOverlay(_instant);
    }
}

public enum BoardAnims
{
    ShowAllPlayerCards,
    HideAllCards,
}
