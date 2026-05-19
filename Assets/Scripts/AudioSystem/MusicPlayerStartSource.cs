using System;
using Extensions;
using FMODUnity;
using UnityEngine;

namespace AudioSystem
{
    public class MusicPlayerStartSource : MonoBehaviour
    {
        [SerializeField] private EventReference musicEvent;

        private void Start()
        {
            GameAudioManager.instance.PlayMusic(musicEvent.GetPath());
        }
    }
}