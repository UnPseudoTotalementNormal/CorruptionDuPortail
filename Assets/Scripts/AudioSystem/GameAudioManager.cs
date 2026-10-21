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
            EventInstance _newMusicInstance = RuntimeManager.CreateInstance(_eventReference);

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
            _eventReference.TryPlayOneShot();
        }

        /// <summary>A one-shot at <paramref name="_volume"/> (0..1): the vote clock gets louder as the end nears (T19).</summary>
        public void PlayOneShot(EventReference _eventReference, float _volume)
        {
            if (_eventReference.IsNull)
            {
                return;
            }
            EventInstance _instance = RuntimeManager.CreateInstance(_eventReference);
            _instance.setVolume(Mathf.Clamp01(_volume));
            _instance.start();
            _instance.release();
        }
        
        // ---- T19: the sound sucked out before the chained cat's splatch (local presentation, ChainRevealDirector)

        private FMOD.DSP _muffle;
        private bool _hasMuffle;

        /// <summary>
        /// Everything at <paramref name="_volume"/> (0..1) through a low-pass at <paramref name="_cutoffHz"/>: the
        /// master bus, muffled and quieter. <see cref="ClearMuffle"/> takes it off. No-op without FMOD.
        /// </summary>
        public void SetMuffle(float _volume, float _cutoffHz)
        {
            if (!RuntimeManager.IsInitialized)
            {
                return;
            }
            if (RuntimeManager.StudioSystem.getBus("bus:/", out Bus _master) == FMOD.RESULT.OK)
            {
                _master.setVolume(Mathf.Clamp01(_volume));
            }
            if (!_hasMuffle)
            {
                if (RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.MULTIBAND_EQ, out _muffle) != FMOD.RESULT.OK)
                {
                    return;
                }
                _muffle.setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.A_FILTER, (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.LOWPASS_24DB);
                if (RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup _group) == FMOD.RESULT.OK)
                {
                    _group.addDSP(0, _muffle);
                }
                _hasMuffle = true;
            }
            _muffle.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.A_FREQUENCY, Mathf.Clamp(_cutoffHz, 20f, 22000f));
        }

        /// <summary>Takes the muffle off: full volume, no low-pass.</summary>
        public void ClearMuffle()
        {
            if (!RuntimeManager.IsInitialized)
            {
                return;
            }
            if (RuntimeManager.StudioSystem.getBus("bus:/", out Bus _master) == FMOD.RESULT.OK)
            {
                _master.setVolume(1f);
            }
            if (_hasMuffle)
            {
                if (RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup _group) == FMOD.RESULT.OK)
                {
                    _group.removeDSP(_muffle);
                }
                _muffle.release();
                _hasMuffle = false;
            }
        }

        /// <summary>The volume (0..1) of one mixer bus, e.g. <c>bus:/Music</c>. No-op without FMOD or that bus.</summary>
        public void SetBusVolume(string _busPath, float _volume)
        {
            if (!RuntimeManager.IsInitialized)
            {
                return;
            }
            if (RuntimeManager.StudioSystem.getBus(_busPath, out Bus _bus) == FMOD.RESULT.OK)
            {
                _bus.setVolume(Mathf.Clamp01(_volume));
            }
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

        public override void OnDestroy()
        {
            base.OnDestroy();
            ClearMuffle();
            
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
