using System;
using DG.Tweening;
using GameLogic;
using UnityEngine;
using UnityEngine.Assertions;

public class LightManager : MonoBehaviour
{
    [SerializeField] private Light mainLight;
    [SerializeField] private Color baseLightColor;
    [SerializeField] private float colorTransitionDuration = 1f;
    [SerializeField] private GameManager gameManager;

    // Story 8.2 (Epic 8 / D2): the code depends on the narrow read slice, not the whole GameManager.
    // Unity can't serialize an interface, so the field stays concrete (lane A) and the property narrows
    // it — D-NFR6's compromise (spec §3 lane A). Reads route through Query.
    private IGameStateQuery Query => gameManager;

    private void Awake()
    {
        Assert.IsNotNull(gameManager, "LightManager.gameManager is not wired — wire it in the composition root (GameScene/prefab).");
    }

    private void Start()
    {
        Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
    }

    private void OnGameStateChanged(int _previousValue, int _newValue)
    {
        GameState _gameState = Query.GetGameState(_newValue);
        if (!_gameState || !_gameState.useLightColorOverride)
        {
            mainLight.DOColor(baseLightColor, colorTransitionDuration);
            return;
        }

        mainLight.DOColor(_gameState.lightColorOverride, colorTransitionDuration);
    }

    private void Reset()
    {
        mainLight = GetComponent<Light>();
        baseLightColor = mainLight.color;
    }
}
