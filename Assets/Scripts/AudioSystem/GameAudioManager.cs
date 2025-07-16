using System;
using Extensions;
using FMODUnity;
using Unity.Netcode;
using Unity.Collections;

namespace AudioSystem
{
    public class GameAudioManager : NetworkBehaviour
    {
        public static GameAudioManager instance;

        private void Awake()
        {
            instance = this;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayOneShotRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            if (string.IsNullOrEmpty(_eventPath.ToString()))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath.ToString());
            _eventReference.TryPlayOneShot();
        }
    }
}
