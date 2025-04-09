using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class BoardManager : NetworkBehaviour
{
    public static BoardManager instance;
    
    public List<Card> visibleCards = new();

    private void Awake()
    {
        instance = this;
    }
    
    public void HideAllCards()
    {
        foreach (var _card in visibleCards)
        {
            Destroy(_card.gameObject);
        }
    }
}
