#region

using System.Collections.Generic;
using Board;
using Characters;
using GameLogic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

public class CardCorruptedText : MonoBehaviour
{
    private Card card;
    private TMP_Text tmpText;

    private Color originalColor;
    public Color corruptedColor = new Color(0.5f, 0.1f, 0.1f, 1);
    private bool isSubscribed = false;

    private void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        card = GetComponentInParent<Card>();
        Assert.IsNotNull(card, "Card component is missing in the parent object");
    }

    private void Start()
    {
        originalColor = tmpText.color;
        if (card.characterInfo != null)
        {
            GameManager.instance.gameInfoRevealer.onCharacterInfoRevealedChanged += UpdateCorruptedText;
            card.characterInfo.isCorrupted.OnValueChanged += OnCorruptedChanged;
            isSubscribed = true;
            UpdateCorruptedText(card.characterInfo.isCorrupted.Value);
        }
    }

    private void OnDestroy()
    {
        if (card.characterInfo != null && isSubscribed)
        {
            GameManager.instance.gameInfoRevealer.onCharacterInfoRevealedChanged -= UpdateCorruptedText;
            card.characterInfo.isCorrupted.OnValueChanged -= OnCorruptedChanged;
            isSubscribed = false;
        }
    }
    
    private void UpdateCorruptedText()
    {
        if (card.characterInfo)
        {
            UpdateCorruptedText(card.characterInfo.isCorrupted.Value);
        }
    }

    private void OnCorruptedChanged(bool _previous, bool _current)
    {
        UpdateCorruptedText(_current);
    }

    private void UpdateCorruptedText(bool _isCorrupted)
    {
        var _character = card.characterInfo;
        if (_isCorrupted && 
            GameManager.instance.gameInfoRevealer.GetCharacterInfo(_character.ownerClientId.Value).isCorruptRevealed > RevealLevel.False)
        {
            tmpText.color = corruptedColor;
        }
        else
        {
            tmpText.color = originalColor;
        }
    }
}
