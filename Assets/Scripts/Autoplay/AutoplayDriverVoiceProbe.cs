#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using Avatars;
using Avatars.Cosmetics;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using VoiceSystem;

namespace Autoplay
{
    /// <summary>
    /// Board task T22 (in-game voice + the talking cat's mouth): probe <c>-autoplay-voice-probe host|client</c>. Every
    /// process swaps Steam Voice for <see cref="SyntheticVoiceCodec"/>, so the voice goes through the real path (push
    /// to talk, server routing, unreliable RPC, decoding, levelling, FMOD playback, mouth) without a microphone. The
    /// named side holds « V » in every day phase (2 s talking, 1 s quiet). Every process journals what it hears,
    /// every 0.25 s while a cat talks: <c>voice.probe seat=… level=… open=… shown=…</c>, and saves one clean capture
    /// per day (<c>voice-d&lt;day&gt;</c>) one second into a speech.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private VoiceChatManager voiceProbeManager;
        private float voiceProbeNextJournal;
        private float voiceProbeTalkSince = -1f;
        private int voiceProbeCapturedDay = -1;
        private int voiceProbeCloseUps;
        private float voiceProbeNextCloseUp;

        private void UpdateVoiceProbe(GameState _state)
        {
            if (string.IsNullOrEmpty(options.voiceProbe))
            {
                return;
            }
            if (voiceProbeManager == null)
            {
                voiceProbeManager = FindAnyObjectByType<VoiceChatManager>();
                if (voiceProbeManager == null)
                {
                    return;
                }
                voiceProbeManager.UseCodec(new SyntheticVoiceCodec(), VoicePlayback.Create);
                Journal.Record("voice.probe.codec", $"side={options.voiceProbe} server={networkManager.IsServer}");
            }

            bool _talker = string.Equals(options.voiceProbe, networkManager.IsServer ? "host" : "client",
                StringComparison.OrdinalIgnoreCase);
            bool _day = _state != null && _state is not AwakeningState;
            voiceProbeManager.ForceTalk = _talker && _day && Mathf.Repeat(Time.unscaledTime, 3f) < 2f;

            if (Time.unscaledTime < voiceProbeNextJournal)
            {
                return;
            }
            voiceProbeNextJournal = Time.unscaledTime + 0.25f;
            AvatarManager _avatars = FindAnyObjectByType<AvatarManager>();
            if (_avatars == null)
            {
                return;
            }
            bool _anyTalking = false;
            foreach (PlayerAvatar _avatar in _avatars.GetAvatars())
            {
                if (_avatar == null)
                {
                    continue;
                }
                CatTalk _cat = _avatar.GetComponentInChildren<CatTalk>(true);
                bool _shown = _cat != null && _cat.MouthShown;
                if (_avatar.VoiceLevel <= 0f && !_shown)
                {
                    continue;
                }
                _anyTalking = true;
                bool _visible = _cat != null && _cat.GetComponentInChildren<Renderer>() is Renderer _renderer && _renderer.isVisible;
                Journal.Record("voice.probe",
                    $"seat={_avatar.ownerClientId.Value} level={_avatar.VoiceLevel:0.00} " +
                    $"open={(_cat != null ? _cat.Openness : -1f):0.00} shown={_shown} " +
                    $"cat={(_cat == null ? "missing" : _cat.isActiveAndEnabled ? "on" : "off")} visible={_visible}");
                if (_shown && (_visible || (_state is LobbyState && voiceProbeCloseUps < 4)) && voiceProbeCloseUps < 12 && Time.unscaledTime >= voiceProbeNextCloseUp)
                {
                    voiceProbeCloseUps++;
                    voiceProbeNextCloseUp = Time.unscaledTime + 0.6f;
                    SaveMouthCloseUp(_cat, $"voice-close-{voiceProbeCloseUps}-{_state?.GetType().Name}-seat{_avatar.ownerClientId.Value}-open{_cat.Openness:0.00}");
                }
            }

            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            int _dayIndex = _gameManager != null ? _gameManager.currentDay : 0;
            if (!_anyTalking)
            {
                voiceProbeTalkSince = -1f;
                return;
            }
            if (voiceProbeTalkSince < 0f)
            {
                voiceProbeTalkSince = Time.unscaledTime;
            }
            if (voiceProbeCapturedDay != _dayIndex && Time.unscaledTime - voiceProbeTalkSince > 0.7f)
            {
                voiceProbeCapturedDay = _dayIndex;
                StartCoroutine(SaveVoiceProbe($"voice-d{_dayIndex}"));
            }
        }

        // A camera of the same settings as the game's, a short way in front of the talking mouth, rendered off screen:
        // proves the mouth is drawn on the muzzle in the real build (renderer, material, depth), whatever the view.
        private void SaveMouthCloseUp(CatTalk _cat, string _name)
        {
            Camera _main = Camera.main;
            Transform _mouth = _cat.Mouth;
            if (_main == null || _mouth == null)
            {
                return;
            }
            var _go = new GameObject("VoiceProbeCloseUp");
            var _camera = _go.AddComponent<Camera>();
            _camera.CopyFrom(_main);
            _camera.fieldOfView = 30f;
            // The head's size sets the distance: the whole face fits the shot at any avatar size.
            float _unit = 1f;
            foreach (Renderer _part in _cat.GetComponentsInChildren<Renderer>())
            {
                if (_part.name == "Head")
                {
                    _unit = Mathf.Max(_part.bounds.extents.y, 0.01f) * 0.25f;
                }
            }
            Vector3 _aim = _mouth.position + _mouth.up * _unit;
            _go.transform.position = _aim + _mouth.forward * _unit * 12f;
            _go.transform.LookAt(_aim);
            _camera.nearClipPlane = _unit * 0.5f;
            var _target = new RenderTexture(640, 640, 24);
            _camera.targetTexture = _target;
            _camera.Render();
            RenderTexture _previous = RenderTexture.active;
            RenderTexture.active = _target;
            var _shot = new Texture2D(640, 640, TextureFormat.RGB24, false);
            _shot.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
            _shot.Apply();
            RenderTexture.active = _previous;
            try
            {
                File.WriteAllBytes(Path.Combine(Journal.OutputDirectory, _name + ".jpg"), _shot.EncodeToJPG(85));
                Journal.Record("voice.closeup", $"{_name} mouthLossy={_mouth.lossyScale.x:0.000}");
            }
            catch (Exception _exception)
            {
                Journal.Record("voice.closeup", $"{_name} error={_exception.Message}");
            }
            _camera.targetTexture = null;
            Destroy(_shot);
            Destroy(_target);
            Destroy(_go);
        }

        private IEnumerator SaveVoiceProbe(string _name)
        {
            Journal.Record("voice.capture", _name);
            yield return SaveClean(_name);
        }
    }

    /// <summary>
    /// A stand-in for Steam Voice (autoplay only): each read while recording is a 4-byte packet, each decode 60 ms of
    /// a voiced vowel (140 Hz and its harmonics, four syllables a second) at a normal speaking level.
    /// </summary>
    internal sealed class SyntheticVoiceCodec : IVoiceCodec
    {
        private const int Rate = 24000;
        private const int SamplesPerPacket = Rate * 60 / 1000;
        private ushort _counter;
        private double _phase;
        private double _time;

        public bool IsAvailable => true;
        public int SampleRate => Rate;
        public bool Recording { get; set; }

        public int ReadCaptured(MemoryStream into)
        {
            if (!Recording)
            {
                return 0;
            }
            _counter++;
            into.WriteByte(0xC7);
            into.WriteByte((byte)(_counter & 0xFF));
            into.WriteByte((byte)(_counter >> 8));
            into.WriteByte(0x7F);
            return 4;
        }

        public int Decode(byte[] compressed, int length, MemoryStream pcm)
        {
            pcm.SetLength(0);
            if (length != 4 || compressed[0] != 0xC7)
            {
                return 0;
            }
            for (int i = 0; i < SamplesPerPacket; i++)
            {
                double _syllable = Math.Max(0, Math.Sin(2 * Math.PI * 4 * _time));
                double _s = 0.12 * _syllable * (Math.Sin(_phase) + 0.5 * Math.Sin(2 * _phase) + 0.3 * Math.Sin(3 * _phase));
                short _v = (short)Math.Round(Math.Clamp(_s, -1, 1) * 32767);
                pcm.WriteByte((byte)(_v & 0xFF));
                pcm.WriteByte((byte)((_v >> 8) & 0xFF));
                _phase += 2 * Math.PI * 140 / Rate;
                _time += 1.0 / Rate;
            }
            return SamplesPerPacket * 2;
        }
    }
}
#endif
