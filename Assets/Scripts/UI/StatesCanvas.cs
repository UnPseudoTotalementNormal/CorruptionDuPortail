using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace UI
{
    public class StatesCanvas : NetworkBehaviour
    {
        public static StatesCanvas Instance { get; private set; }
        
        private void Awake()
        {
            Instance = this;
        }
    }
}