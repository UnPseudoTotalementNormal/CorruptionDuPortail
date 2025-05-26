using FMODUnity;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Settings
{
    public class AudioPanelSettings : MonoBehaviour
    {
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider musicVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        
        private void Start()
        {
            masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 0.5f);
            musicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume", 0.5f);
            sfxVolumeSlider.value = PlayerPrefs.GetFloat("SfxVolume", 0.5f);
            SaveMasterVolume(masterVolumeSlider.value);
            SaveMusicVolume(musicVolumeSlider.value);
            SaveSfxVolume(sfxVolumeSlider.value);
            
            masterVolumeSlider.onValueChanged.AddListener(SaveMasterVolume);
            musicVolumeSlider.onValueChanged.AddListener(SaveMusicVolume);
            sfxVolumeSlider.onValueChanged.AddListener(SaveSfxVolume);
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
        
        private float DbToLinear(float _db)
        {
            return Mathf.Pow(10f, _db / 20f);
        }
    }
}
