#region

using System;
using System.Runtime.InteropServices;
using CorruptionDuPortail.Domain;
using FMOD;
using FMODUnity;
using Debug = UnityEngine.Debug;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// One remote player's voice played through FMOD (never an AudioSource): a looping one-second PCM16 mono user
    /// sound on the master bus, fed by <see cref="Push"/> and cleared behind the mixer by <see cref="Tick"/>
    /// (<see cref="VoiceRingCursor"/> keeps the accounts). The channel pauses when the speaker stops and starts
    /// again on the next packet, a short prebuffer ahead. Created by <see cref="VoiceChatManager"/> (lane B).
    /// </summary>
    public sealed class VoicePlayback : IDisposable
    {
        private const float PrebufferSeconds = 0.08f;
        private const float MaxLeadSeconds = 0.4f;

        private readonly VoiceRingCursor _cursor;
        private readonly int _ringBytes;
        private readonly byte[] _silence;
        private Sound _sound;
        private Channel _channel;
        private bool _paused = true;

        public ulong Seat { get; }

        /// <summary>Audio is coming out for this player right now.</summary>
        public bool IsSpeaking => !_paused;

        private VoicePlayback(ulong seat, int sampleRate, Sound sound, Channel channel)
        {
            Seat = seat;
            _ringBytes = sampleRate * sizeof(short);
            _cursor = new VoiceRingCursor(_ringBytes, (int)(_ringBytes * PrebufferSeconds), (int)(_ringBytes * MaxLeadSeconds));
            _silence = new byte[_ringBytes];
            _sound = sound;
            _channel = channel;
            Write(0, _silence, _ringBytes); // a user sound starts with undefined memory
        }

        /// <summary>Creates the FMOD sound and its paused channel. Null when FMOD is not running.</summary>
        public static VoicePlayback Create(ulong seat, int sampleRate)
        {
            try
            {
                var _info = new CREATESOUNDEXINFO
                {
                    cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)),
                    numchannels = 1,
                    format = SOUND_FORMAT.PCM16,
                    defaultfrequency = sampleRate,
                    length = (uint)(sampleRate * sizeof(short)),
                };
                FMOD.System _core = RuntimeManager.CoreSystem;
                RESULT _result = _core.createSound("", MODE.OPENUSER | MODE.LOOP_NORMAL | MODE._2D, ref _info, out Sound _sound);
                if (_result != RESULT.OK)
                {
                    Debug.LogError($"[VOICE] FMOD createSound failed for seat {seat}: {_result}");
                    return null;
                }
                _result = _core.playSound(_sound, default, true, out Channel _channel);
                if (_result != RESULT.OK)
                {
                    Debug.LogError($"[VOICE] FMOD playSound failed for seat {seat}: {_result}");
                    _sound.release();
                    return null;
                }
                return new VoicePlayback(seat, sampleRate, _sound, _channel);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VOICE] FMOD unavailable, seat {seat} stays silent: {e.Message}");
                return null;
            }
        }

        /// <summary>Queues decoded PCM16 mono samples (bytes 0..<paramref name="count"/>).</summary>
        public void Push(byte[] pcm, int count)
        {
            if (count <= 0)
            {
                return;
            }
            Tick();
            if (!_cursor.TryPrepareWrite(count, out int _offset))
            {
                return; // a late burst: dropping it keeps the delay short
            }
            Write(_offset, pcm, count);
            _cursor.Commit(count);
            if (_paused)
            {
                _channel.setPaused(false);
                _paused = false;
            }
        }

        /// <summary>Volume of this voice, 0..1 (the master bus still applies).</summary>
        public void SetVolume(float volume) => _channel.setVolume(volume);

        /// <summary>Every frame: clears what the mixer played and pauses once the speaker went quiet.</summary>
        public void Tick()
        {
            if (_channel.getPosition(out uint _position, TIMEUNIT.PCMBYTES) != RESULT.OK)
            {
                return;
            }
            _cursor.Advance((int)_position, out int _clearOffset, out int _clearLength);
            if (_clearLength > 0)
            {
                Write(_clearOffset, _silence, _clearLength);
            }
            if (!_paused && _cursor.Starved)
            {
                _channel.setPaused(true);
                _paused = true;
            }
        }

        private void Write(int offset, byte[] source, int count)
        {
            if (_sound.@lock((uint)offset, (uint)count, out IntPtr _first, out IntPtr _second, out uint _firstLength,
                    out uint _secondLength) != RESULT.OK)
            {
                return;
            }
            Marshal.Copy(source, 0, _first, (int)_firstLength);
            if (_second != IntPtr.Zero && _secondLength > 0)
            {
                Marshal.Copy(source, (int)_firstLength, _second, (int)_secondLength);
            }
            _sound.unlock(_first, _second, _firstLength, _secondLength);
        }

        public void Dispose()
        {
            if (_channel.hasHandle())
            {
                _channel.stop();
                _channel.clearHandle();
            }
            if (_sound.hasHandle())
            {
                _sound.release();
                _sound.clearHandle();
            }
        }
    }
}
