using System;
using Network;
using TMPro;
using Unity.Netcode;
using Unity.Services.Relay;
using UnityEngine;

public class GameCodeText : MonoBehaviour
{
    private void Start()
    {
        GetComponent<TMP_Text>().text = GameCode.gameCode;
    }
}
