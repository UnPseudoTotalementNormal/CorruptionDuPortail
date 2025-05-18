using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

public class BoardManager : NetworkBehaviour
{
    public static BoardManager instance;
    public Card cardPrefab;
    
    public Transform spawnCardPosition;
    
    public List<Card> visibleCards = new();
    
    public List<CancellationTokenSource> cancelTokens = new();

    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;
    
    public const float CARD_SPACING = 7;
    
    
    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        GameManager.instance.onCharactersListUpdated += OnCharacterListUpdated;
    }

    private void OnCharacterListUpdated(List<Character> _characters)
    {
        UpdateCards(_characters);
    }

    private void UpdateCards(List<Character> _characters)
    {
        foreach (var _character in _characters)
        {
            Card _card = visibleCards.FirstOrDefault(_char => _char.characterInfo.ownerClientId == _character.ownerClientId);
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
    
    public async UniTask ShowAllPlayerCards(bool _stopOtherAnims = true)
    {
        CancellationTokenSource _cancelToken = new();
        OnStartingNewAnim(_cancelToken, _stopOtherAnims);
        
        await HideAllCards(false);
        _cancelToken.Token.ThrowIfCancellationRequested();
        
        foreach (var _character in GameManager.instance.GetCharacters())
        {
            Card _card = AddNewCard();
            _card.SetInfo(_character);
            _ = _card.ShowPseudoWithRevealedInfo();
            _card.transform.eulerAngles = new Vector3(0, 0, 180);

            _card.transform.DOLocalMove(new Vector3(spawnCardPosition.localPosition.x + (visibleCards.Count - 1) * CARD_SPACING, 0, 0), 0.5f);
            _ = _card.ShowFrontSide();
        }
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
}

public enum BoardAnims
{
    ShowAllPlayerCards,
    HideAllCards,
}
