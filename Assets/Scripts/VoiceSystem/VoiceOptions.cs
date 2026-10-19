#region

using System;
using UnityEngine;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// Local, per-player voice settings (T22, mockups vocal v1: C1 + D1): the microphone mode (hold V, or always open)
    /// and the volume of the other players' voices. Set in the pause menu's audio panel, saved in PlayerPrefs, never
    /// networked. A ScriptableObject channel (mirror of <see cref="Presentation.SeatedViewOptions"/>): the audio
    /// panel and the <see cref="VoiceChatManager"/> reference the same asset, no singleton.
    /// </summary>
    [CreateAssetMenu(menuName = "Corruption/Voice Options", fileName = "VoiceOptions")]
    public class VoiceOptions : ScriptableObject
    {
        private const string OpenMicKey = "Voice.OpenMic";
        private const string VolumeKey = "Voice.Volume";

        /// <summary>The microphone stays open (Steam cuts the silences) instead of opening while V is held.</summary>
        public bool OpenMic { get; private set; }

        /// <summary>Volume of the other players' voices, 0..1 (on top of the master volume).</summary>
        public float Volume { get; private set; } = 1f;

        public event Action OnChanged;

        // Domain reload is disabled in this project: reload the saved values whenever the asset is (re)enabled.
        private void OnEnable()
        {
            OpenMic = PlayerPrefs.GetInt(OpenMicKey, 0) == 1;
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        }

        /// <summary>Set the microphone mode. <paramref name="_persist"/> false = this session only (tests, autoplay:
        /// PlayerPrefs are shared by every process on the machine).</summary>
        public void SetOpenMic(bool _value, bool _persist = true)
        {
            if (_persist)
            {
                PlayerPrefs.SetInt(OpenMicKey, _value ? 1 : 0);
            }
            if (_value == OpenMic)
            {
                return;
            }
            OpenMic = _value;
            OnChanged?.Invoke();
        }

        /// <summary>Set the voices' volume (see <see cref="SetOpenMic"/> for <paramref name="_persist"/>).</summary>
        public void SetVolume(float _value, bool _persist = true)
        {
            _value = Mathf.Clamp01(_value);
            if (_persist)
            {
                PlayerPrefs.SetFloat(VolumeKey, _value);
            }
            if (Mathf.Approximately(_value, Volume))
            {
                return;
            }
            Volume = _value;
            OnChanged?.Invoke();
        }
    }
}
