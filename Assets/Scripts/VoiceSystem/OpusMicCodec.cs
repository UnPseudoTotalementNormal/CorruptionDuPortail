#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Concentus.Enums;
using Concentus.Structs;
using FMOD;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// A microphone of this PC picked in the options (mockups micro v1 C4): FMOD records it, Opus (Concentus, pure C#)
    /// compresses it in 20 ms frames, a few frames per packet. Steam cannot be told which microphone to use, so a player
    /// whose Steam listens to the wrong one picks the right one here. Decoding needs neither Steam nor a microphone.
    /// Packet: one byte = frame count, then per frame one byte = its length and its bytes.
    /// </summary>
    public sealed class OpusMicCodec : IVoiceCodec, IDisposable
    {
        public const int OpusRate = 48000;
        private const int FrameSamples = OpusRate / 50; // 20 ms
        private const int Bitrate = 24000;
        private const int MaxFrameBytes = 250;
        private const int MaxFramesPerPacket = 8;
        // A sample above this (int16) is sound: a live microphone always hisses a little, a dead one gives exact zeros.
        private const int SoundThreshold = 3;

        private readonly Func<string> _deviceName;
        private OpusEncoder _encoder;
        private OpusDecoder _decoder;
        private readonly List<short> _pending = new();
        private readonly short[] _frame = new short[FrameSamples];
        private readonly byte[] _frameBytes = new byte[MaxFrameBytes];
        private readonly short[] _decoded = new short[FrameSamples];
        private readonly List<short> _decodedAll = new();

        private Sound _sound;
        private int _driver = -1;
        private int _driverRate;
        private int _driverChannels;
        private uint _soundSamples;
        private uint _readPosition;
        private double _resamplePhase;
        private bool _recording;

        /// <param name="_deviceName">The microphone's name; a name no longer plugged in falls back to the default one.</param>
        /// <param name="_outputRate">Rate of the PCM <see cref="Decode"/> hands out (the playback's).</param>
        public OpusMicCodec(Func<string> _deviceName, int _outputRate = OpusRate)
        {
            this._deviceName = _deviceName;
            SampleRate = _outputRate;
        }

        /// <summary>FMOD sees at least one microphone.</summary>
        public bool IsAvailable => Devices().Count > 0;

        /// <summary>Rate of the PCM <see cref="Decode"/> hands out.</summary>
        public int SampleRate { get; set; }

        /// <summary>The device actually opened (after a fallback), null when closed.</summary>
        public string OpenedDevice { get; private set; }

        /// <summary><see cref="Time.unscaledTime"/> of the last sample that was not silence (opening counts as sound).</summary>
        public float LastSoundAt { get; private set; }

        public bool Recording
        {
            get => _recording;
            set
            {
                if (value == _recording)
                {
                    return;
                }
                if (value)
                {
                    Open();
                }
                else
                {
                    Close();
                }
            }
        }

        // ---- The microphones of this PC ----

        /// <summary>FMOD's record drivers that are plugged in, loopbacks (what the PC plays) left out. The system's
        /// default microphone comes first.</summary>
        public static List<string> DeviceNames()
        {
            var _names = new List<string>();
            foreach ((int _, string name, int _, int _) in Devices())
            {
                _names.Add(name);
            }
            return _names;
        }

        private static List<(int id, string name, int rate, int channels)> Devices()
        {
            var _list = new List<(int, string, int, int)>();
            try
            {
                FMOD.System _core = RuntimeManager.CoreSystem;
                if (_core.getRecordNumDrivers(out int _count, out _) != RESULT.OK)
                {
                    return _list;
                }
                for (int i = 0; i < _count; i++)
                {
                    if (_core.getRecordDriverInfo(i, out string _name, 256, out _, out int _rate, out _, out int _channels,
                            out DRIVER_STATE _state) != RESULT.OK)
                    {
                        continue;
                    }
                    if ((_state & DRIVER_STATE.CONNECTED) == 0 || _name.Contains("[loopback]"))
                    {
                        continue;
                    }
                    _list.Add((i, _name, _rate, Math.Max(1, _channels)));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VOICE] FMOD record drivers unavailable: {e.Message}");
            }
            return _list;
        }

        private void Open()
        {
            List<(int id, string name, int rate, int channels)> _devices = Devices();
            if (_devices.Count == 0)
            {
                Diagnostics.Logs.GameLog.Warn("voice", "voice.input.none");
                return;
            }
            string _wanted = _deviceName?.Invoke();
            int _pick = _devices.FindIndex(d => d.name == _wanted);
            if (_pick < 0)
            {
                _pick = 0; // FMOD lists the system's default microphone first
                if (!string.IsNullOrEmpty(_wanted))
                {
                    // the microphone picked in the options is no longer plugged in
                    Diagnostics.Logs.GameLog.Warn("voice", "voice.input.fallback", "wanted", _wanted, "opened", _devices[0].name);
                }
            }
            (int id, string name, int rate, int channels) = _devices[_pick];
            try
            {
                FMOD.System _core = RuntimeManager.CoreSystem;
                var _info = new CREATESOUNDEXINFO
                {
                    cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)),
                    numchannels = channels,
                    format = SOUND_FORMAT.PCM16,
                    defaultfrequency = rate,
                    length = (uint)(rate * channels * sizeof(short)), // one second
                };
                RESULT _result = _core.createSound("", MODE.OPENUSER | MODE.LOOP_NORMAL | MODE._2D, ref _info, out _sound);
                if (_result == RESULT.OK)
                {
                    _result = _core.recordStart(id, _sound, true);
                }
                if (_result != RESULT.OK)
                {
                    Diagnostics.Logs.GameLog.Error("voice", "voice.input.open", "device", name, "result", _result.ToString());
                    ReleaseSound();
                    return;
                }
                _driver = id;
                _driverRate = rate;
                _driverChannels = channels;
                _soundSamples = (uint)rate;
                _readPosition = 0;
                _resamplePhase = 0;
                _pending.Clear();
                _encoder = OpusEncoder.Create(OpusRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
                _encoder.Bitrate = Bitrate;
                _recording = true;
                OpenedDevice = name;
                LastSoundAt = Time.unscaledTime;
                Diagnostics.Logs.GameLog.Info("voice", "voice.input.open", "device", name, "rate", rate, "channels", channels);
            }
            catch (Exception e)
            {
                Diagnostics.Logs.GameLog.Error("voice", "voice.input.open", "device", name, "result", e.Message);
                ReleaseSound();
            }
        }

        private void Close()
        {
            if (_driver >= 0)
            {
                try
                {
                    RuntimeManager.CoreSystem.recordStop(_driver);
                }
                catch (Exception)
                {
                    // FMOD already gone (quitting)
                }
            }
            ReleaseSound();
            _driver = -1;
            _recording = false;
            OpenedDevice = null;
            _pending.Clear();
        }

        private void ReleaseSound()
        {
            if (_sound.hasHandle())
            {
                _sound.release();
                _sound.clearHandle();
            }
        }

        public void Dispose() => Close();

        // ---- Capture ----

        public int ReadCaptured(MemoryStream into)
        {
            if (!_recording)
            {
                return 0;
            }
            PullFromDevice();
            int _written = EncodePending(into);
            // A long hitch left more than a packet holds: the oldest voice is dropped, the delay stays short.
            if (_pending.Count > FrameSamples * MaxFramesPerPacket)
            {
                _pending.RemoveRange(0, _pending.Count - FrameSamples * MaxFramesPerPacket);
            }
            return _written;
        }

        // Copies what FMOD recorded since the last read, mono, resampled to 48 kHz.
        private void PullFromDevice()
        {
            if (RuntimeManager.CoreSystem.getRecordPosition(_driver, out uint _position) != RESULT.OK)
            {
                return;
            }
            uint _available = (_position + _soundSamples - _readPosition) % _soundSamples;
            if (_available == 0)
            {
                return;
            }
            int _bytesPerSample = _driverChannels * sizeof(short);
            if (_sound.@lock(_readPosition * (uint)_bytesPerSample, _available * (uint)_bytesPerSample, out IntPtr _p1,
                    out IntPtr _p2, out uint _l1, out uint _l2) != RESULT.OK)
            {
                return;
            }
            var _raw = new short[(_l1 + _l2) / sizeof(short)];
            Marshal.Copy(_p1, _raw, 0, (int)(_l1 / sizeof(short)));
            if (_p2 != IntPtr.Zero && _l2 > 0)
            {
                Marshal.Copy(_p2, _raw, (int)(_l1 / sizeof(short)), (int)(_l2 / sizeof(short)));
            }
            _sound.unlock(_p1, _p2, _l1, _l2);
            _readPosition = _position;
            Feed(_raw, _driverChannels, _driverRate);
        }

        /// <summary>Adds raw interleaved PCM16 of the device (any rate, any channel count) to the voice to encode:
        /// mono, resampled to 48 kHz. Public for the tests.</summary>
        public void Feed(short[] _raw, int _channels, int _rate)
        {
            _driverChannels = _channels;
            int _count = _raw.Length / _channels;
            if (_count < 2)
            {
                return;
            }
            double _step = (double)_rate / OpusRate;
            // linear resampling, the phase carried from one read to the next
            for (; _resamplePhase < _count - 1; _resamplePhase += _step)
            {
                int i = (int)_resamplePhase;
                double t = _resamplePhase - i;
                double a = Mono(_raw, i), b = Mono(_raw, i + 1);
                _pending.Add((short)(a + (b - a) * t));
            }
            _resamplePhase -= _count - 1;
            bool _heard = false;
            for (int i = 0; i < _raw.Length && !_heard; i++)
            {
                _heard = Math.Abs((int)_raw[i]) > SoundThreshold;
            }
            if (_heard)
            {
                LastSoundAt = Time.unscaledTime;
            }
        }

        /// <summary>Tests: encodes what <see cref="Feed"/> got without a device.</summary>
        internal void StartEncoderForTests()
        {
            _encoder = OpusEncoder.Create(OpusRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = Bitrate;
            _recording = true;
            _driver = -1;
        }

        internal int EncodePending(MemoryStream into)
        {
            int _frames = Math.Min(_pending.Count / FrameSamples, MaxFramesPerPacket);
            if (_frames == 0)
            {
                return 0;
            }
            long _start = into.Position;
            into.WriteByte((byte)_frames);
            for (int f = 0; f < _frames; f++)
            {
                _pending.CopyTo(f * FrameSamples, _frame, 0, FrameSamples);
                int _bytes = _encoder.Encode(_frame, 0, FrameSamples, _frameBytes, 0, MaxFrameBytes);
                into.WriteByte((byte)_bytes);
                into.Write(_frameBytes, 0, _bytes);
            }
            _pending.RemoveRange(0, _frames * FrameSamples);
            return (int)(into.Position - _start);
        }

        private double Mono(short[] _raw, int i)
        {
            if (_driverChannels == 1)
            {
                return _raw[i];
            }
            double _sum = 0;
            for (int c = 0; c < _driverChannels; c++)
            {
                _sum += _raw[i * _driverChannels + c];
            }
            return _sum / _driverChannels;
        }

        // ---- Decoding (any player, with or without a microphone) ----

        public int Decode(byte[] compressed, int length, MemoryStream pcm)
        {
            pcm.SetLength(0);
            if (length < 1)
            {
                return 0;
            }
            _decoder ??= OpusDecoder.Create(OpusRate, 1);
            _decodedAll.Clear();
            int p = 0;
            int _frames = compressed[p++];
            try
            {
                for (int f = 0; f < _frames && p < length; f++)
                {
                    int _bytes = compressed[p++];
                    if (p + _bytes > length)
                    {
                        break;
                    }
                    int _samples = _decoder.Decode(compressed, p, _bytes, _decoded, 0, FrameSamples, false);
                    p += _bytes;
                    for (int i = 0; i < _samples; i++)
                    {
                        _decodedAll.Add(_decoded[i]);
                    }
                }
            }
            catch (Exception)
            {
                return 0; // a corrupt packet: VoiceChatManager logs the failed decode
            }
            WriteResampled(pcm);
            return (int)pcm.Length;
        }

        private void WriteResampled(MemoryStream pcm)
        {
            if (_decodedAll.Count == 0)
            {
                return;
            }
            double _step = (double)OpusRate / SampleRate;
            for (double s = 0; s < _decodedAll.Count; s += _step)
            {
                int i = (int)s;
                double t = s - i;
                double a = _decodedAll[i], b = i + 1 < _decodedAll.Count ? _decodedAll[i + 1] : a;
                short v = (short)(a + (b - a) * t);
                pcm.WriteByte((byte)(v & 0xFF));
                pcm.WriteByte((byte)((v >> 8) & 0xFF));
            }
        }
    }
}
