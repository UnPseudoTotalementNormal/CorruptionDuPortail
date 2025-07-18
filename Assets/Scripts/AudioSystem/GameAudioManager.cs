using System.Collections.Generic;
using System.Linq;
using Extensions;
using FMOD.Studio;
using FMODUnity;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.Assertions;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace AudioSystem
{
    public class GameAudioManager : NetworkBehaviour
    {
        public static GameAudioManager instance;
        
        private Dictionary<string, EventInstance> eventInstances = new();
        
        private List<EventInstance> activeMusicInstances = new();

        private void Awake()
        {
            instance = this;
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void StopMusicRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            StopMusic(_eventPath.ToString());
        }

        public void StopMusic(string _eventPath)
        {
            foreach (var _m in activeMusicInstances.ToList())
            {
                _m.getDescription(out EventDescription _desc);
                _desc.getPath(out string _path);
                
                if (_path == _eventPath)
                {
                    _m.stop(STOP_MODE.ALLOWFADEOUT);
                    _m.release();
                    activeMusicInstances.Remove(_m);
                }
            }
            
            if (activeMusicInstances.Count > 0)
            {
                activeMusicInstances[^1].setPaused(false);
            }
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayMusicRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            PlayMusic(_eventPath.ToString());
        }
        
        public void PlayMusic(string _eventPath)
        {
            if (string.IsNullOrEmpty(_eventPath))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath);
            EventInstance _newMusicInstance = RuntimeManager.CreateInstance(_eventReference);

            Assert.IsTrue(_newMusicInstance.isValid(), "the new music instance is not valid with path: " + _eventPath);
            
            if (activeMusicInstances.Count != 0)
            {
                activeMusicInstances[^1].setPaused(true);
            }
            
            _newMusicInstance.start();
            activeMusicInstances.Add(_newMusicInstance);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayOneShotRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            PlayOneShot(_eventPath.ToString());
        }
        
        public void PlayOneShot(string _eventPath)
        {
            if (string.IsNullOrEmpty(_eventPath))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath);
            _eventReference.TryPlayOneShot();
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayEventInstanceRpc(FixedString128Bytes _eventPath, FixedString64Bytes _instanceKey, RpcParams _rpcParams = default)
        {
            PlayEventInstance(_eventPath.ToString(), _instanceKey.ToString());
        }
        
        public void PlayEventInstance(string _eventPath, string _instanceKey)
        {
            if (string.IsNullOrEmpty(_eventPath))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath);
            EventInstance _instance = RuntimeManager.CreateInstance(_eventReference);
            _instance.start();

            if (eventInstances.ContainsKey(_instanceKey))
            {
                StopEventInstance(_instanceKey);
            }
                
            eventInstances.Add(_instanceKey, _instance);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void StopEventInstanceRpc(FixedString64Bytes _instanceKey, RpcParams _rpcParams = default)
        {
            StopEventInstance(_instanceKey.ToString());
        }

        public void StopEventInstance(string _instanceKey)
        {
            if (string.IsNullOrEmpty(_instanceKey))
            {
                return;
            }
            
            if (eventInstances.TryGetValue(_instanceKey, out EventInstance _instance))
            {
                if (_instance.isValid())
                {
                    _instance.stop(STOP_MODE.ALLOWFADEOUT);
                    _instance.release();
                }
                eventInstances.Remove(_instanceKey);
            }
        }
    }
}
