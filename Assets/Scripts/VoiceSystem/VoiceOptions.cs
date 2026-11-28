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
        private const string InputKey = "Voice.Input";
        private const string OwnVolumeKey = "Voice.OwnVolume";

        /// <summary>« Ma voix » at this value sends the voice as it is; the slider goes a little above (+3 dB).</summary>
        public const float OwnVolumeUnity = 0.7f;

        /// <summary>The microphone stays open (Steam cuts the silences) instead of opening while V is held.</summary>
        public bool OpenMic { get; private set; }

        /// <summary>Volume of the other players' voices, 0..1 (on top of the master volume).</summary>
        public float Volume { get; private set; } = 1f;

        /// <summary>The microphone picked in the options (mockups micro v1 C4): empty = Steam's own, else the name of a
        /// microphone of this PC, opened by the game (<see cref="OpusMicCodec"/>).</summary>
        public string InputDevice { get; private set; } = "";

        /// <summary>« Ma voix »: how loud the others hear this player, 0..1 (<see cref="OwnVolumeUnity"/> = unchanged).</summary>
        public float OwnVolume { get; private set; } = OwnVolumeUnity;

        /// <summary>The gain the listeners apply to this player's voice (from <see cref="OwnVolume"/>).</summary>
        public float OwnGain => OwnVolume / OwnVolumeUnity;

        /// <summary>The Vocal menu listens to the microphone (its test, or watching for a dead one): the push-to-talk key stays shut meanwhile.
        /// Never saved.</summary>
        public bool TestRunning { get; private set; }

        public event Action OnChanged;

        // Domain reload is disabled in this project: reload the saved values whenever the asset is (re)enabled.
        private void OnEnable()
        {
            OpenMic = PlayerPrefs.GetInt(OpenMicKey, 0) == 1;
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            InputDevice = PlayerPrefs.GetString(InputKey, "");
            OwnVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(OwnVolumeKey, OwnVolumeUnity));
            TestRunning = false;
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
    
        /// <summary>Pick the microphone: empty = Steam's (see <see cref="SetOpenMic"/> for <paramref name="_persist"/>).</summary>
        public void SetInputDevice(string _value, bool _persist = true)
        {
            _value ??= "";
            if (_persist)
            {
                PlayerPrefs.SetString(InputKey, _value);
            }
            if (_value == InputDevice)
            {
                return;
            }
            InputDevice = _value;
            OnChanged?.Invoke();
        }

        /// <summary>Set « Ma voix » (see <see cref="SetOpenMic"/> for <paramref name="_persist"/>).</summary>
        public void SetOwnVolume(float _value, bool _persist = true)
        {
            _value = Mathf.Clamp01(_value);
            if (_persist)
            {
                PlayerPrefs.SetFloat(OwnVolumeKey, _value);
            }
            if (Mathf.Approximately(_value, OwnVolume))
            {
                return;
            }
            OwnVolume = _value;
            OnChanged?.Invoke();
        }

        public void SetTestRunning(bool _value)
        {
            if (_value == TestRunning)
            {
                return;
            }
            TestRunning = _value;
            OnChanged?.Invoke();
        }
    }
}
