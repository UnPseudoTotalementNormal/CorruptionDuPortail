using System;
using Network.Action;
using Unity.Netcode;
using UnityEngine;

namespace Test
{
    public class NetworkActionTester : NetworkBehaviour
    {
        private NetworkAction testBaseNetworkAction = new("testBaseNetworkAction");
        private NetworkAction<int> intTestNetworkAction = new("intTestNetworkAction");

        private void Awake()
        {
            testBaseNetworkAction += OnTestBaseNetworkActionTriggered;
            intTestNetworkAction += OnIntTestNetworkActionTriggered;
        }
        
        [ContextMenu("TRIGGER TEST ALL ACTIONS")]
        private void TriggerTestAllActions()
        {
            testBaseNetworkAction.Invoke();
            intTestNetworkAction.Invoke(50);
        }

        private void OnIntTestNetworkActionTriggered(int _value)
        {
            Debug.Log($"{nameof(intTestNetworkAction)} triggered with value: {_value}");
        }

        private void OnTestBaseNetworkActionTriggered()
        {
            Debug.Log($"{nameof(testBaseNetworkAction)} triggered");
        }
    }
}