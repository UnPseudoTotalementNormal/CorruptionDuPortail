#region

using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;

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
        textTitle.text = GameManager.instance.characterManager.GetCharacter(_takeDownThePortalState.mageCharacterOwnerId).GetOwnerPseudo() + " doit abattre le portail.";
    }
}
