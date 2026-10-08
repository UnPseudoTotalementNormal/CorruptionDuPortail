#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Board;
using Characters;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using GameLogic;
using TransformComposition;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

public class BoardManager : NetworkBehaviour
{
    // Story 10.3 (Epic 10 / D4): the gameplay consumers were rerouted off this global onto an injected
    // boardManager field — the game-loop GameStates (lane B, pushed by SetupGameStates from the
    // composition root), GameManager itself (root), FocusManager + CardEffectManager (lane-A scene
    // field), and GameInfoRevealer (lane-C OnNetworkSpawn). Guard #1 now locks this global. The static
    // backs ONLY recorded non-registered survivors: SelectionFlowService (POCO, no Unity lifecycle →
    // Epic 11.2) + CardPickerManager (UI leaf → Epic 12.2), plus the composition-root accessor that
    // serves it (the one sanctioned locator, since the board is not de-singletonised).
    // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests
    public static BoardManager instance;
    public Card cardPrefab;

    // Story 7.2 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
    [SerializeField] private CharacterManager characterManager;
    // Story 7.3 lane A: scene-wired GameInfoRevealer, pushed into each Card it creates.
    [SerializeField] private GameInfoRevealer gameInfoRevealer;
    // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
    private ICharacterQuery CharacterQuery => characterManager;

    // Story 11.2 (Epic 11 / D5): the card grid-wrap placement arithmetic extracted to a pure,
    // EditMode-tested Domain POCO. The adapter reads the scene Transforms' local positions + the
    // spacing constants and wraps the plain-float result back into a Vector3.
    private readonly CardLayout _cardLayout = new();
    
    public Transform spawnCardPosition;
    public Transform maxCardPosition; //cards will overflow past this point
    
    public List<Card> visibleCards = new();
    
    public List<CancellationTokenSource> cancelTokens = new();

    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;
    public event Action<Card> onCardSpawned;
    public event Action<Card> onCardDestroyed;
    
    // Arrangement of the cards (CardLayout): every preset keeps each card, its vote button and its vote count visible
    // in the top view and seated first person, hovered or not, at every table size (autoplay card visibility probe,
    // 2026-10-08). Poyo picks the default.
    [Tooltip("How the cards are laid out on the board (all three keep every card and vote panel visible).")]
    public CardGridPreset cardGridPreset = CardGridPreset.Centred;
    
    public bool hasAllCardsShown => visibleCards.Count == CharacterQuery.GetCharacters().Count(_c => !_c.isFake);
    
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void Start()
    {
        // Story 7.2 lane A: assert at first use (Start, not Awake) so PlayMode harnesses can wire the
        // serialized field by reflection after AddComponent — AddComponent runs Awake synchronously.
        // The authoritative scene-wiring check is SceneWiringGuard (CI); this is the runtime backstop.
        Assert.IsNotNull(characterManager, "BoardManager.characterManager is not wired — wire it in GameScene (the composition root).");
        // gameInfoRevealer is only forwarded to the Cards this board creates (BoardManager never uses
        // it directly), so it is not asserted at runtime — SceneWiringGuard (CI) is its wiring check.
        CharacterQuery.onCharactersListUpdated += OnCharacterListUpdated;
    }

    public override void OnNetworkDespawn()
    {
        // Story 7.2: the injected field is the authoritative ref for the unsubscribe deref
        // (no For() re-resolution through a possibly-changed registry during teardown).
        if (characterManager != null)
        {
            CharacterQuery.onCharactersListUpdated -= OnCharacterListUpdated;
        }

        if (instance == this)
        {
            instance = null;
        }

        base.OnNetworkDespawn();
    }

    private void OnCharacterListUpdated(List<Character> _characters)
    {
        UpdateCards(_characters);
    }

    private void UpdateCards(List<Character> _characters)
    {
        foreach (var _character in _characters)
        {
            Card _card = visibleCards.FirstOrDefault(_char => _char != null && _char.characterInfo != null && _char.characterInfo.ownerClientId.Value == _character.ownerClientId.Value);
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
        int _count = visibleCards.Count;
        CardGrid _grid = _cardLayout.GridFor(_count, cardGridPreset);
        Vector3 _origin = spawnCardPosition.localPosition;
        float _centreX = (_origin.x + maxCardPosition.localPosition.x) / 2f;
        for (var _i = 0; _i < _count; _i++)
        {
            var _card = visibleCards[_i];
            CardPlacement _placement = _cardLayout.Place(_i, _count, _grid, _origin.x, _origin.y, _origin.z, _centreX);
            var _layer = _card.visualComponents.compositor.GetLayer("Transform");
            _layer.DOLocalMoveX(_placement.X, 0.5f);
            _layer.DOLocalMoveY(_placement.Y, 0.5f);
            _layer.DOLocalMoveZ(_placement.Z, 0.5f);
            // Set at once, not tweened: a card still shrinking at the vote start moved its vote button under the
            // voter's click (autoplay, 2026-10-08).
            _layer.localScale = cardPrefab.transform.localScale * _grid.Scale;
        }
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
        
        foreach (var _character in CharacterQuery.GetCharacters().Where(_c => !_c.isFake))
        {
            Card _card = AddNewCard(_character);
            _card.visualComponents.compositor.GetLayer("Flip").localEulerAngles = new Vector3(0, 0, -180);
            _ = _card.ShowPseudoWithRevealedInfo(true);
        }

        Card ownedCard = visibleCards.SingleOrDefault(c =>
            c.characterInfo.ownerClientId.Value == CharacterQuery.GetLocalClientId());
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
        _card.Initialize(CharacterQuery, gameInfoRevealer); // lane B push: the card is prefab-instantiated, cannot serialize a scene ref.
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
