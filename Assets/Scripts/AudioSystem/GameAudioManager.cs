using System.Collections.Generic;
using System.Linq;
using Extensions;
using FMOD.Studio;
using FMODUnity;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
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
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void StopMusicRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            StopMusic(_eventPath.ToString());
        }

        public void StopMusic(string _eventPath)
        {
            int _before = activeMusicInstances.Count;
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
            
            if (_before == activeMusicInstances.Count)
            {
                Diagnostics.Logs.GameLog.Verbose("audio", "audio.music.stop.unknown", "path", _eventPath, "playing", _before);
            }
            else
            {
                Diagnostics.Logs.GameLog.Info("audio", "audio.music.stop", "path", _eventPath, "left", activeMusicInstances.Count);
            }
            
            if (activeMusicInstances.Count > 0)
            {
                activeMusicInstances[^1].start();
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
            // A path missing from the banks used to throw EventNotFoundException at the caller: reported, then skipped.
            if (!TryCreateInstance(_eventReference, _eventPath, "music", out EventInstance _newMusicInstance))
            {
                return;
            }
            Diagnostics.Logs.GameLog.Info("audio", "audio.music", "path", _eventPath, "previous", activeMusicInstances.Count);

            Assert.IsTrue(_newMusicInstance.isValid(), "the new music instance is not valid with path: " + _eventPath);
            
            if (activeMusicInstances.Count != 0)
            {
                activeMusicInstances[^1].stop(STOP_MODE.ALLOWFADEOUT);
            }
            
            _newMusicInstance.start();
            activeMusicInstances.Add(_newMusicInstance);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayOneShotRpc(FixedString128Bytes _eventPath, RpcParams _rpcParams = default)
        {
            PlayOneShot(_eventPath.ToString());
        }
        
        public void PlayOneShot(EventReference _eventReference)
        {
            PlayOneShot(_eventReference.GetPath());
        }
        public void PlayOneShot(string _eventPath)
        {
            if (string.IsNullOrEmpty(_eventPath))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath);
            if (_eventReference.Guid.IsNull)
            {
                FmodEventReferenceExtensions.ReportMissingEvent(_eventPath, "oneshot");
                return;
            }
            _eventReference.TryPlayOneShot();
        }

        /// <summary>A one-shot at <paramref name="_volume"/> (0..1): the vote clock gets louder as the end nears (T19).</summary>
        public void PlayOneShot(EventReference _eventReference, float _volume)
        {
            if (_eventReference.IsNull)
            {
                return;
            }
            if (!TryCreateInstance(_eventReference, null, "oneshot-volume", out EventInstance _instance))
            {
                return;
            }
            _instance.setVolume(Mathf.Clamp01(_volume));
            _instance.start();
            _instance.release();
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayEventInstanceRpc(FixedString128Bytes _eventPath, FixedString64Bytes _instanceKey, RpcParams _rpcParams = default)
        {
            PlayEventInstance(_eventPath.ToString(), _instanceKey.ToString());
        }
        
        public void PlayEventInstance(EventReference _eventReference, string _instanceKey)
        {
            PlayEventInstance(_eventReference.GetPath(), _instanceKey);
        }
        
        public void PlayEventInstance(string _eventPath, string _instanceKey)
        {
            if (string.IsNullOrEmpty(_eventPath))
            {
                return;
            }
            
            EventReference _eventReference = RuntimeManager.PathToEventReference(_eventPath);
            if (!TryCreateInstance(_eventReference, _eventPath, "instance", out EventInstance _instance))
            {
                return;
            }
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
            else
            {
                Diagnostics.Logs.GameLog.Verbose("audio", "audio.instance.stop.unknown", "key", _instanceKey);
            }
        }

        // CreateInstance throws EventNotFoundException for an event missing from the banks (an unresolved path gives an
        // empty GUID): report it once (audio.event.missing) and let the caller skip the sound instead of throwing.
        private static bool TryCreateInstance(EventReference _eventReference, string _path, string _where, out EventInstance _instance)
        {
            _instance = default;
            if (_eventReference.Guid.IsNull)
            {
                FmodEventReferenceExtensions.ReportMissingEvent(_path ?? "(no guid)", _where);
                return false;
            }
            try
            {
                _instance = RuntimeManager.CreateInstance(_eventReference);
                return true;
            }
            catch (EventNotFoundException)
            {
                FmodEventReferenceExtensions.ReportMissingEvent(_path ?? _eventReference.Guid.ToString(), _where);
                return false;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            
            foreach (var _music in activeMusicInstances)
            {
                if (_music.isValid())
                {
                    _music.stop(STOP_MODE.ALLOWFADEOUT);
                    _music.release();
                }
            }
            activeMusicInstances.Clear();
            
            foreach (var _eventInstance in eventInstances.Values)
            {
                if (_eventInstance.isValid())
                {
                    _eventInstance.stop(STOP_MODE.ALLOWFADEOUT);
                    _eventInstance.release();
                }
            }
            eventInstances.Clear();
        }
    }
}
