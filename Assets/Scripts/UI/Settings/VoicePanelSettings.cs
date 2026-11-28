using System.Collections.Generic;
using System.IO;
using CorruptionDuPortail.Domain;
using Diagnostics.Logs;
using Localization;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using VoiceSystem;

namespace UI.Settings
{
    /// <summary>
    /// The Vocal menu's microphone lines (mockups micro v1 C4 + T2, v2 V1): « Ma voix » (how loud the others hear you),
    /// « Entrée » (Steam's microphone first, then the microphones of this PC, a click goes to the next) and « Test »
    /// (you hear yourself live, through the same path as your voice in a game: compression, leveler, « Ma voix »).
    /// « Aucun son de ce micro » shows when a microphone of the PC gives nothing for 2 s, even without speaking (a live
    /// one always hisses a little; playtest 2026-11-28: Hat Kid's Steam listened to the closed laptop's array). Lives
    /// on the Test row: enabled only while the Vocal menu is shown, so leaving it stops the test.
    /// </summary>
    public class VoicePanelSettings : MonoBehaviour
    {
        private const string SteamLabel = "‹ Celle de Steam ›"; // loc
        private const string TestLabel = "Tester le micro"; // loc
        private const string StopLabel = "Arrêter le test"; // loc
        private const string DeadLabel = "Aucun son de ce micro"; // loc
        private const float ReadInterval = 0.06f;
        private const float SilentSeconds = 2f;
        // Steam hands out nothing while it hears no voice: no packet for this long while testing = nothing heard.
        private const float SteamSilentSeconds = 4f;

        [SerializeField] private VoiceOptions _options;
        [SerializeField] private Slider _ownVolumeSlider;
        [SerializeField] private Button _inputButton;
        [SerializeField] private TMP_Text _inputLabel;
        [SerializeField] private Button _testButton;
        [SerializeField] private TMP_Text _testLabel;
        [Tooltip("The ink line that fills with the voice (pivot on the left, scaled in x).")]
        [SerializeField] private RectTransform _levelFill;
        [Tooltip("The test word and its line, hidden while the dead line shows.")]
        [SerializeField] private GameObject _testGroup;
        [SerializeField] private TMP_Text _deadLabel;

        private OpusMicCodec _pcMic;
        private SteamVoiceCodec _steam;
        private IVoiceCodec _testCodec;
        private VoicePlayback _playback;
        private VoiceLeveler _leveler;
        private readonly MemoryStream _packet = new();
        private readonly MemoryStream _pcm = new();
        private float _lastReadAt;
        private float _testStartedAt;
        private float _lastPacketAt;
        private int _testPackets;
        private bool _dead;
        private bool _started;

        private void Start()
        {
            Assert.IsNotNull(_options, "VoicePanelSettings._options not wired");
            Assert.IsNotNull(_ownVolumeSlider, "VoicePanelSettings._ownVolumeSlider not wired");
            Assert.IsNotNull(_inputButton, "VoicePanelSettings._inputButton not wired");
            Assert.IsNotNull(_inputLabel, "VoicePanelSettings._inputLabel not wired");
            Assert.IsNotNull(_testButton, "VoicePanelSettings._testButton not wired");
            Assert.IsNotNull(_testLabel, "VoicePanelSettings._testLabel not wired");
            Assert.IsNotNull(_levelFill, "VoicePanelSettings._levelFill not wired");
            Assert.IsNotNull(_testGroup, "VoicePanelSettings._testGroup not wired");
            Assert.IsNotNull(_deadLabel, "VoicePanelSettings._deadLabel not wired");

            _ownVolumeSlider.SetValueWithoutNotify(_options.OwnVolume);
            _ownVolumeSlider.onValueChanged.AddListener(v => _options.SetOwnVolume(v));
            _inputButton.onClick.AddListener(NextInput);
            _testButton.onClick.AddListener(ToggleTest);
            _deadLabel.SetLoc(DeadLabel);
            _options.OnChanged += Refresh;
            _started = true;
            OnEnable();
        }

        private void OnDestroy()
        {
            if (_options != null)
            {
                _options.OnChanged -= Refresh;
            }
            StopAll();
        }

        private void OnEnable()
        {
            if (!_started)
            {
                return; // Start does it once wired
            }
            Refresh();
            OpenMonitor();
        }

        private void OnDisable() => StopAll();

        private void StopAll()
        {
            StopTest("hidden");
            CloseMonitor();
        }

        // ---- Entrée ----

        private static bool SteamAvailable => SteamClient.IsValid;

        private void NextInput()
        {
            // Steam's first when Steam runs; without it (Relay build) nothing picked = the PC's default microphone.
            List<string> _devices = OpusMicCodec.DeviceNames();
            var _names = new List<string>();
            if (SteamAvailable || _devices.Count == 0)
            {
                _names.Add("");
            }
            _names.AddRange(_devices);
            int _index = string.IsNullOrEmpty(_options.InputDevice) ? 0 : _names.IndexOf(_options.InputDevice);
            string _next = _names[(_index + 1) % _names.Count];
            GameLog.Info("ui", "ui.setting", "name", "voiceInput", "value", _next == "" ? "steam" : _next,
                "devices", _names.Count - 1);
            bool _wasTesting = _testCodec != null;
            StopTest("input-changed");
            CloseMonitor();
            _options.SetInputDevice(_next);
            OpenMonitor();
            if (_wasTesting)
            {
                StartTest();
            }
        }

        private void Refresh()
        {
            if (_inputLabel == null)
            {
                return;
            }
            string _device = _options.InputDevice;
            if (string.IsNullOrEmpty(_device) && !SteamAvailable)
            {
                List<string> _devices = OpusMicCodec.DeviceNames();
                _device = _devices.Count > 0 ? _devices[0] : "";
            }
            if (string.IsNullOrEmpty(_device))
            {
                _inputLabel.SetLoc(SteamLabel);
            }
            else
            {
                _inputLabel.text = $"‹ {_device} ›"; // loc-ok: a device name
            }
            _ownVolumeSlider.SetValueWithoutNotify(_options.OwnVolume);
            _testLabel.SetLoc(_testCodec != null ? StopLabel : TestLabel);
        }

        // A microphone of the PC is listened to while the Vocal menu shows, to say at once when it gives nothing.
        private void OpenMonitor()
        {
            if (!UsesPcMic())
            {
                return;
            }
            _pcMic ??= new OpusMicCodec(() => _options.InputDevice);
            _pcMic.Recording = true;
            HoldMicrophone();
        }

        private void CloseMonitor()
        {
            if (_pcMic != null)
            {
                _pcMic.Recording = false;
            }
            SetDead(false);
            HoldMicrophone();
        }

        // Steam's microphone when picked and Steam runs (Steam build); else a microphone of the PC (the default one
        // when nothing is picked).
        private bool UsesPcMic() => !string.IsNullOrEmpty(_options.InputDevice) || !SteamAvailable;

        // ---- Test ----

        private void ToggleTest()
        {
            if (_testCodec != null)
            {
                StopTest("button");
            }
            else
            {
                StartTest();
            }
        }

        private void StartTest()
        {
            if (UsesPcMic())
            {
                OpenMonitor();
                _testCodec = _pcMic;
            }
            else
            {
                _steam ??= new SteamVoiceCodec();
                _steam.Recording = true;
                _testCodec = _steam;
            }
            HoldMicrophone();
            int _rate = _testCodec is OpusMicCodec ? OpusMicCodec.OpusRate : _testCodec.SampleRate;
            if (_testCodec is OpusMicCodec _pc)
            {
                _pc.SampleRate = _rate;
            }
            _playback = VoicePlayback.Create(ulong.MaxValue, _rate);
            _leveler = new VoiceLeveler(_rate);
            _testStartedAt = Time.unscaledTime;
            _lastPacketAt = float.NegativeInfinity;
            _testPackets = 0;
            GameLog.Info("voice", "voice.test.start", "codec", _testCodec is OpusMicCodec ? "Opus" : "Steam",
                "device", _testCodec is OpusMicCodec _opened ? _opened.OpenedDevice : "steam",
                "gain", System.Math.Round(_options.OwnGain, 2), "playback", _playback != null);
            Refresh();
        }

        private void StopTest(string _why)
        {
            if (_testCodec == null)
            {
                return;
            }
            GameLog.Info("voice", "voice.test.stop", "why", _why, "packets", _testPackets,
                "seconds", System.Math.Round(Time.unscaledTime - _testStartedAt, 1), "silent", _dead);
            if (_testCodec == _steam)
            {
                _steam.Recording = false;
            }
            _testCodec = null;
            _playback?.Dispose();
            _playback = null;
            _leveler = null;
            HoldMicrophone();
            if (_levelFill != null)
            {
                _levelFill.localScale = new Vector3(0f, 1f, 1f);
            }
            Refresh();
        }

        private void Update()
        {
            float _now = Time.unscaledTime;
            if (_now - _lastReadAt >= ReadInterval)
            {
                _lastReadAt = _now;
                ReadMicrophone(_now);
            }
            _playback?.Tick();
            float _level = _playback != null ? _playback.Level : 0f;
            _levelFill.localScale = new Vector3(Mathf.Clamp01(_level), 1f, 1f);
        }

        private void ReadMicrophone(float _now)
        {
            IVoiceCodec _source = _testCodec ?? (_pcMic != null && _pcMic.Recording ? _pcMic : null);
            if (_source == null)
            {
                return;
            }
            _packet.SetLength(0);
            int _read = _source.ReadCaptured(_packet);
            if (_read > 0 && _testCodec != null)
            {
                _testPackets++;
                _lastPacketAt = _now;
                int _decoded = _testCodec.Decode(_packet.ToArray(), _read, _pcm);
                if (_decoded > 0 && _playback != null)
                {
                    _leveler.Process(_pcm.GetBuffer(), _decoded);
                    VoiceGain.Apply(_pcm.GetBuffer(), _decoded, _options.OwnGain);
                    _playback.Push(_pcm.GetBuffer(), _decoded);
                }
            }
            if (_source is OpusMicCodec _pc)
            {
                SetDead(_now - _pc.LastSoundAt > SilentSeconds);
            }
            else
            {
                SetDead(_now - _testStartedAt > SteamSilentSeconds && _now - _lastPacketAt > SteamSilentSeconds);
            }
        }

        // While this menu listens to a microphone the push-to-talk key stays shut: two captures of one device fight.
        private void HoldMicrophone()
        {
            if (_options != null)
            {
                _options.SetTestRunning(_testCodec != null || (_pcMic != null && _pcMic.Recording));
            }
        }

        private void SetDead(bool _value)
        {
            if (_value == _dead)
            {
                return;
            }
            _dead = _value;
            if (_deadLabel != null) _deadLabel.gameObject.SetActive(_value);
            if (_testGroup != null) _testGroup.SetActive(!_value);
            GameLog.Info("voice", "voice.test.silent", "silent", _value, "device",
                UsesPcMic() && _pcMic != null ? _pcMic.OpenedDevice : "steam", "testing", _testCodec != null);
        }
    }
}
