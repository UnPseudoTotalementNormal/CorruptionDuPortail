#region

using System.Collections.Generic;
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
    
    private void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        card = GetComponentInParent<Card>();
        Assert.IsNotNull(card, "Card component is missing in the parent object");
    }

    private void Start()
    {
        originalColor = tmpText.color;
        GameManager.instance.characterManager.onCharactersListUpdated += OnCharacterListUpdated;
    }

    private void OnCharacterListUpdated(List<Character> _characters)
    {
        var _character = card.characterInfo;
        if (_character.isCorrupted.Value && 
            GameManager.instance.gameInfoRevealer.GetCharacterInfo(_character.ownerClientId.Value).isCorruptRevealed > RevealLevel.False)
        {
            tmpText.color = corruptedColor;
        }
        else
        {
            tmpText.color = originalColor;
        }
    }

    private void OnDestroy()
    {
        GameManager.instance.characterManager.onCharactersListUpdated -= OnCharacterListUpdated;
    }
}
