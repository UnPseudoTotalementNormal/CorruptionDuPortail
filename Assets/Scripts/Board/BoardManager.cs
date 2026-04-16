#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Board;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using GameLogic;
using TransformComposition;
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
    public event Action<Card> onCardSpawned;
    public event Action<Card> onCardDestroyed;
    
    public const float CARD_SPACING = 7;
    public const float CARD_LINE_SPACING = 9;
    
    public bool hasAllCardsShown => visibleCards.Count == GameManager.instance.characterManager.GetCharacters().Count(_c => !_c.isFake);
    
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
                _card.SetInfo(_character);
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
            
            _card.visualComponents.compositor.GetLayer("Transform").DOLocalMoveX(_localTargetPosition.x, 0.5f);
            _card.visualComponents.compositor.GetLayer("Transform").DOLocalMoveY(_localTargetPosition.y, 0.5f);
            _card.visualComponents.compositor.GetLayer("Transform").DOLocalMoveZ(_localTargetPosition.z, 0.5f);
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

    public async UniTask ShowAllPlayerCards(bool _forceRefresh = false, bool _stopOtherAnims = true)
    {
        if (hasAllCardsShown && !_forceRefresh)
        {
            return;
        }
        
        CancellationTokenSource _cancelToken = new();
        OnStartingNewAnim(_cancelToken, _stopOtherAnims);
        
        await HideAllCards(false);
        _cancelToken.Token.ThrowIfCancellationRequested();
        
        foreach (var _character in GameManager.instance.characterManager.GetCharacters().Where(_c => !_c.isFake))
        {
            Card _card = AddNewCard(_character);
            _card.visualComponents.compositor.GetLayer("Flip").localEulerAngles = new Vector3(0, 0, -180);
            _ = _card.ShowPseudoWithRevealedInfo(true);
        }

        Card ownedCard = visibleCards.SingleOrDefault(c =>
            c.characterInfo.ownerClientId.Value == CharacterManager.instance.GetLocalClientId());
        if (ownedCard)
        {
            visibleCards.ChangeIndex(visibleCards.IndexOf(ownedCard), 0);
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
            _card.SetCanShowBackInfo(false);
            _ = _card.ShowBackSide();
        }

        await UniTask.Delay(TimeSpan.FromSeconds(visibleCards[0].visualComponents.rotateTime), cancellationToken: _cancelToken.Token);
        
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
        onCardDestroyed?.Invoke(_card);
        visibleCards.Remove(_card);
        Destroy(_card.gameObject);
    }
    
    public Card AddNewCard(Character _characterInfo = null, bool _assignCardToBoard = true)
    {
        Card _card = Instantiate(cardPrefab, transform);
        _card.transform.localPosition = new Vector3(0, 0, 0);

        if (_assignCardToBoard)
        {
            visibleCards.Add(_card);
        }

        if (_characterInfo)
        {
            _card.SetInfo(_characterInfo);
        }

        if (_assignCardToBoard)
        {
            _card.onCardClicked += OnCardClicked;
            _card.onCardHovered += OnCardHovered;
            _card.onCardUnhovered += OnCardUnhovered;
            onCardSpawned?.Invoke(_card);
        }
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
}

public enum BoardAnims
{
    ShowAllPlayerCards,
    HideAllCards,
}
