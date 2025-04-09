using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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
    
    
    private void Awake()
    {
        instance = this;
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
        
        foreach (var _character in GameManager.instance.characters)
        {
            Card _card = AddNewCard();
            _card.SetInfo(_character);
            _card.ShowPseudoOnly();
            _card.transform.eulerAngles = new Vector3(0, 0, 180);

            _card.transform.DOLocalMove(new Vector3(spawnCardPosition.localPosition.x + (visibleCards.Count - 1) * 7, 0, 0), 0.5f);
            _card.ShowFrontSide();
            
            if (_character.isChained || _character.ownerClientId == NetworkManager.LocalClientId)
            {
                _card.ShowPseudoWithRole();
            }
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
            _card.ShowBackSide();
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
        visibleCards.Remove(_card);
        Destroy(_card.gameObject);
    }
    
    public Card AddNewCard()
    {
        Card _card = Instantiate(cardPrefab, transform);
        _card.transform.localPosition = new Vector3(0, 0, 0);
        
        visibleCards.Add(_card);
        return _card;
    }
}

public enum BoardAnims
{
    ShowAllPlayerCards,
    HideAllCards,
}
