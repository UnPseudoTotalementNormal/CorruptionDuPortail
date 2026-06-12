#region

using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Netcode;
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
        // Story 12.3: prefab-resident UI leaf (StateUI prefab) — a [SerializeField] can't ref the scene
        // CharacterManager, so it resolves through the sanctioned CompositionRoot.For(Singleton) instead of the
        // CharacterManager God-Object façade. Behaviour-identical (one production NM).
        textTitle.text = CompositionRoot.For(NetworkManager.Singleton).CharacterQuery.GetCharacter(_takeDownThePortalState.mageCharacterOwnerId).GetOwnerPseudo() + " doit abattre le portail.";
    }
}
