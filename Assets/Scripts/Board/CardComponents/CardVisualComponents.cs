#region

using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using TMPro;
using TransformComposition;
using UI.CardUI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Container for all UI and animation components used by card implementations.
    /// </summary>
    [System.Serializable]
    public class CardVisualComponents : MonoBehaviour
    {

        [Header("Core References")]
        public Card card => GetComponent<Card>();
        [field: SerializeField] public Transform cardEffectsParent { get; private set; }
        
        [Header("Animation Components")]
        [field: SerializeField] public TransformCompositorComponent compositor { get; private set; }
        [field: SerializeField] public float hoverZoom { get; private set; } = 1.15f;
        [field: SerializeField] public float rotateTime { get; private set; } = 1f;

        [Header("First-person hover (look-at camera + computed no-clip lift)")]
        [Tooltip("Gate: the look-at hover only plays while this channel reports Embodied (the seated Vote). " +
                 "Other phases keep the flat hover. Wire the shared CameraModeChannel asset.")]
        [field: SerializeField] public Avatars.CameraModeChannel cameraModeChannel { get; private set; }
        [Tooltip("The readable FACE normal + up, in the card ROOT's local frame at rest (tune so the card " +
                 "ends up facing the player; flip an axis if it faces away).")]
        [field: SerializeField] public Vector3 hoverFaceLocalNormal { get; private set; } = Vector3.up;
        [field: SerializeField] public Vector3 hoverFaceLocalUp { get; private set; } = Vector3.forward;
        [Tooltip("Table top world Y and the float offset above it. The card's size is measured DYNAMICALLY " +
                 "each hover (card face + deployed vote canvas), so there is no hardcoded extent to tune.")]
        [field: SerializeField] public float hoverSurfaceY { get; private set; } = -15.025f;
        [field: SerializeField] public float hoverFloatOffset { get; private set; } = 0.2f;
        [Tooltip("Local trial options: with 'cards stand' on, every card holds the first-person look-at pose for the " +
                 "whole seated Vote, not only while hovered (T16). Wire the shared SeatedViewOptions asset.")]
        [field: SerializeField] public Presentation.SeatedViewOptions seatedViewOptions { get; private set; }

        [Header("Both Side References")] 
        [field: SerializeField] public MeIconCard meIconCard { get; private set; }

        [Header("Front Side References")]
        [field: SerializeField] public TMP_Text cardPlayerPseudo { get; private set; }
        [field: SerializeField] public Image cardPlayerPseudoHolder { get; private set; }
        [field: SerializeField] public TMP_Text cardRoleText { get; private set; }
        [field: SerializeField] public Image cardRoleTextHolder { get; private set; }
        [field: SerializeField] public Image cardImage { get; private set; }
        [Tooltip("Resolves role.rolePortrait -> Sprite (replaces the old Addressables lookup). Wire the PortraitTable asset.")]
        [field: SerializeField] public PortraitTable portraitTable { get; private set; }
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

