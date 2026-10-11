#region

using Network;
using Network.Steam;
using TMPro;
using UnityEngine;

#endregion

public class GameCodeText : MonoBehaviour
{
    private void Start()
    {
        GetComponent<TMP_Text>().text = GameCode.gameCode;
        // Steam build: "Inviter des amis" under the code (no-op in the Relay build).
        SteamInviteButton.AddUnder(transform as RectTransform);
    }
}
