#region

using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

public class TakeDownThePortalTextTitle : MonoBehaviour
{
    [SerializeField] private TMP_Text textTitle;

    private void Start()
    {
        GetComponentInParent<StateUI>().owningGameState.onStateStartClient += OnStateStartClient;
    }

    private void OnStateStartClient()
    {
        var _takeDownThePortalState = (TakeDownThePortalState)GetComponentInParent<StateUI>().owningGameState;
        if (_takeDownThePortalState == null || !_takeDownThePortalState.shouldActivate)
        {
            return;
        }
        // Story 7.4: prefab-resident UI leaf (StateUI prefab) — a [SerializeField] can't ref the scene
        // CharacterManager, so route via the façade. Behaviour-identical; proper injection: Epic 12.
        textTitle.text = CharacterManager.instance.GetCharacter(_takeDownThePortalState.mageCharacterOwnerId).GetOwnerPseudo() + " doit abattre le portail.";
    }
}
