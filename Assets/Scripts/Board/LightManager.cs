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

    private void Awake()
    {
        Assert.IsNotNull(gameManager, "LightManager.gameManager is not wired — wire it in the composition root (GameScene/prefab).");
    }

    private void Start()
    {
        gameManager.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
    }

    private void OnGameStateChanged(int _previousValue, int _newValue)
    {
        GameState _gameState = gameManager.GetGameState(_newValue);
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
