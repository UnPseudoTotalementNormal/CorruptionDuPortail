#region

using Network;
using TMPro;
using UnityEngine;

#endregion

public class GameCodeText : MonoBehaviour
{
    private void Start()
    {
        GetComponent<TMP_Text>().text = GameCode.gameCode;
    }
}
