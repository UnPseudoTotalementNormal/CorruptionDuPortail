#region

using CorruptionDuPortail.Domain;
using Network;
using Network.Player;
using TMPro;
using UnityEngine;

#endregion

public class PseudoInputField : MonoBehaviour
{
    private void Awake()
    {
        GetComponentInChildren<TMP_InputField>().onSubmit.AddListener(OnSubmitPseudo);
        GetComponentInChildren<TMP_InputField>().onDeselect.AddListener(OnSubmitPseudo);
    }

    private void OnSubmitPseudo(string pseudo)
    {
        if (string.IsNullOrWhiteSpace(pseudo))
        {
            Debug.LogWarning("Pseudo is empty");
            return;
        }

        var _info = LocalPlayerInfoHolder.playerInfo;
        // NET-02: typed by the player, so no UGS discriminator to strip; sanitized + UTF-8-safe truncation.
        _info.playerName = PlayerNameSanitizer.Sanitize(pseudo, false, _info.playerName.ToString());
        LocalPlayerInfoHolder.playerInfo = _info;

        // NET-02: a rename while connected must replicate (it used to stay local-only).
        LobbyPlayerInfoHolder _holder = LobbyPlayerInfoHolder.instance;
        if (_holder != null && _holder.IsSpawned)
        {
            _holder.UpdateLocalPlayerInfo();
        }
    }
}
