using System;
using Network.Player;
using TMPro;
using UnityEngine;

public class PseudoInputField : MonoBehaviour
{
    private void Awake()
    {
        GetComponentInChildren<TMP_InputField>().onSubmit.AddListener(OnSubmitPseudo);
        GetComponentInChildren<TMP_InputField>().onDeselect.AddListener(OnSubmitPseudo);
    }

    private void OnSubmitPseudo(string pseudo)
    {
        if (string.IsNullOrEmpty(pseudo))
        {
            Debug.LogError("Pseudo is empty");
            return;
        }

        var _info = LocalPlayerInfoHolder.Instance.playerInfo;
        _info.playerName = pseudo;
        LocalPlayerInfoHolder.Instance.playerInfo = _info;
    }
}
