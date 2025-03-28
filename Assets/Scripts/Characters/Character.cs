using System;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using Characters.WinningConditions;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Characters/Character")]
public class Character : ScriptableObject
{
    [Header("Character Settings")]
    public string characterName;
    public Sprite characterArtwork;
    public CharacterType characterType;
    public FactionType factionType;
    [UnityEngine.Range(1, 3)] public int characterDifficulty;
    
    public List<Power> powers;
    public List<WinningCondition> winningConditions;
        
    [Header("Variables")]
    public ulong ownerClientId;
    public bool isChained;
    
    
    public bool IsTheSameCharacter(Character otherCharacter)
    {
        return characterName == otherCharacter.characterName;
    }
}
