#region

using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using TMPro;
using UI.CardUI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Container for all UI components used by ICardDisplay implementations.
    /// This class holds the Unity UI references that will be injected into ICardDisplay implementations.
    /// </summary>
    [System.Serializable]
    public class CardVisualComponents : MonoBehaviour
    {
        [Header("Core References")]
        [field: SerializeField] public Transform cardEffectsParent { get; private set; }
        
        [Header("Both Side References")] 
        [field: SerializeField] public MeIconCard meIconCard { get; private set; }

        [Header("Front Side References")]
        [field: SerializeField] public TMP_Text cardPlayerPseudo { get; private set; }
        [field: SerializeField] public TMP_Text cardRoleText { get; private set; }
        [field: SerializeField] public Image cardImage { get; private set; }
        [field: SerializeField] public Image factionLogoImage { get; private set; }
        [field: SerializeField] public Image factionLogoBackgroundImage { get; private set; }
        [field: SerializeField] public Image unknownFogOverlay { get; private set; }
        [field: SerializeField] public CanvasGroup chainedOverlay { get; private set; }
        [field: SerializeField] public List<CanvasGroup> objectsToShowOnFrontSidePlacementOnly { get; private set; } = new();

        [Header("Back Side References")]
        [field: SerializeField] public TMP_Text bsCardPlayerPseudo { get; private set; }
        [field: SerializeField] public Image bsFactionLogoImage { get; private set; }
        [field: SerializeField] public List<CanvasGroup> objectsToShowOnBackSidePlacementOnly { get; private set; } = new();

        [Header("Assets")]
        [field: SerializeField] public Sprite unknownCardSprite { get; private set; }
        [field: SerializeField] public SerializedDictionary<FactionType, Sprite> factionLogo { get; private set; }
        [field: SerializeField] public SerializedDictionary<FactionType, Sprite> factionLogoBackground { get; private set; }
    }
}

