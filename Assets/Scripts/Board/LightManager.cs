using System;
using DG.Tweening;
using GameLogic;
using UnityEngine;

public class LightManager : MonoBehaviour
{
    [SerializeField] private Light mainLight;
    [SerializeField] private Color baseLightColor;
    [SerializeField] private float colorTransitionDuration = 1f;
    
    private void Start()
    {
        GameManager.instance.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
    }

    private void OnGameStateChanged(int _previousValue, int _newValue)
    {
        GameState _gameState = GameManager.instance.GetGameState(_newValue);
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
