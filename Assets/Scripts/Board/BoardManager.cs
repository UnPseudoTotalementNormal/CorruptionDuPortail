using System;
using Unity.Netcode;
using UnityEngine;

public class BoardManager : NetworkBehaviour
{
    public static BoardManager instance;

    private void Awake()
    {
        instance = this;
    }
}
