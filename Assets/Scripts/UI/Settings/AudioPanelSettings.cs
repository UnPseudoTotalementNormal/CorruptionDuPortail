using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using VoiceSystem;

namespace UI.Settings
{
    public class AudioPanelSettings : MonoBehaviour
    {
        private const string HoldToTalkLabel = "‹ Maintenir V ›";
        private const string OpenMicLabel = "‹ Toujours ouvert ›";

        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider musicVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        // T22 (mockups vocal v1: C1 + D1): the voices' volume and the microphone mode, kept by VoiceOptions.
        [SerializeField] private VoiceOptions _voiceOptions;
        [SerializeField] private Slider _voiceVolumeSlider;
        [SerializeField] private Button _micModeButton;
        [SerializeField] private TMP_Text _micModeLabel;

        private void Start()
        {
            Assert.IsNotNull(_voiceOptions, "AudioPanelSettings._voiceOptions not wired");
            Assert.IsNotNull(_voiceVolumeSlider, "AudioPanelSettings._voiceVolumeSlider not wired");
            Assert.IsNotNull(_micModeButton, "AudioPanelSettings._micModeButton not wired");
            Assert.IsNotNull(_micModeLabel, "AudioPanelSettings._micModeLabel not wired");

            masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 0.5f);
            musicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume", 0.5f);
            sfxVolumeSlider.value = PlayerPrefs.GetFloat("SfxVolume", 0.5f);
            SaveMasterVolume(masterVolumeSlider.value);
            SaveMusicVolume(musicVolumeSlider.value);
            SaveSfxVolume(sfxVolumeSlider.value);

            masterVolumeSlider.onValueChanged.AddListener(SaveMasterVolume);
            musicVolumeSlider.onValueChanged.AddListener(SaveMusicVolume);
            sfxVolumeSlider.onValueChanged.AddListener(SaveSfxVolume);

            _voiceVolumeSlider.SetValueWithoutNotify(_voiceOptions.Volume);
            _voiceVolumeSlider.onValueChanged.AddListener(SaveVoiceVolume);
            _micModeButton.onClick.AddListener(ToggleMicMode);
            _voiceOptions.OnChanged += RefreshVoice;
            RefreshVoice();
        }

        private void OnDestroy()
        {
            if (_voiceVolumeSlider != null)
            {
                _voiceVolumeSlider.onValueChanged.RemoveListener(SaveVoiceVolume);
            }
            if (_micModeButton != null)
            {
                _micModeButton.onClick.RemoveListener(ToggleMicMode);
            }
            if (_voiceOptions != null)
            {
                _voiceOptions.OnChanged -= RefreshVoice;
            }
        }

        private void SaveMasterVolume(float _volume)
        {
            PlayerPrefs.SetFloat("MasterVolume", _volume);
            RuntimeManager.GetBus("bus:/").setVolume(_volume);
        }

        private void SaveMusicVolume(float _volume)
        {
            PlayerPrefs.SetFloat("MusicVolume", _volume);
            RuntimeManager.GetBus("bus:/Music").setVolume(_volume);
        }

        private void SaveSfxVolume(float _volume)
        {
            PlayerPrefs.SetFloat("SfxVolume", _volume);
            RuntimeManager.GetBus("bus:/Sfx").setVolume(_volume);
        }

        private void SaveVoiceVolume(float _volume) => _voiceOptions.SetVolume(_volume);

        private void ToggleMicMode()
        {
            Diagnostics.Logs.GameLog.Info("ui", "ui.setting", "name", "openMic", "value", !_voiceOptions.OpenMic);
            _voiceOptions.SetOpenMic(!_voiceOptions.OpenMic);
        }

        // The other audio panel (main menu / pause menu) changed a voice option: keep this one in sync.
        private void RefreshVoice()
        {
            _voiceVolumeSlider.SetValueWithoutNotify(_voiceOptions.Volume);
            _micModeLabel.text = _voiceOptions.OpenMic ? OpenMicLabel : HoldToTalkLabel;
        }

        private float DbToLinear(float _db)
        {
            return Mathf.Pow(10f, _db / 20f);
        }
    }
}
