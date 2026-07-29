using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// LOCAL, owner-only orchestrator of an emote's SELF-FEEDBACK lifecycle. <see cref="EmoteWheelInput"/> calls
    /// <see cref="Begin"/> on wheel release; this drives the emote on the local avatar
    /// (<see cref="PlayerAvatar.RequestEmote"/>) and raises <see cref="EmoteStarted"/> / <see cref="EmoteStopped"/>
    /// so the <see cref="AvatarCameraArbiter"/> can swap to the third-person orbit camera + reveal the local body,
    /// then restore.
    ///
    /// STOP: a LOOP emote holds until the player ACTS — any key press or any mouse-button press (checked here),
    /// OR a camera / state / tablet change (the arbiter calls <see cref="End"/> on those). Mouse MOVEMENT never
    /// stops it (it orbits). A ONE-SHOT emote auto-ends after its <see cref="EmoteDefinition.oneShotSeconds"/>.
    ///
    /// Input-System-direct (Keyboard/Mouse.current), the same local low-level path the embodied camera / emote
    /// wheel already use. Presentation-only, NOT a NetworkBehaviour.
    /// </summary>
    public class EmotePlaybackController : MonoBehaviour
    {
        private bool _active;
        private EmoteDefinition _activeEmote;
        private int _startFrame;
        private float _oneShotEndTime;
        private PlayerAvatar _localAvatar;

        /// <summary>Whether an emote is currently playing (self-feedback view up).</summary>
        public bool IsEmoting => _active;

        /// <summary>Raised when an emote starts / stops — the arbiter subscribes to drive the orbit camera + reveal.</summary>
        public event System.Action EmoteStarted;
        public event System.Action EmoteStopped;

        /// <summary>
        /// Start playing <paramref name="_emote"/> on the local avatar and enter the self-feedback view. A prior
        /// emote is stopped first. No-op if the emote is null or the local avatar cannot be resolved.
        /// </summary>
        public void Begin(EmoteDefinition _emote)
        {
            if (_emote == null)
            {
                return;
            }
            if (_active)
            {
                End();
            }

            PlayerAvatar _avatar = ResolveLocalAvatar();
            if (_avatar == null)
            {
                return;
            }

            _avatar.RequestEmote(_emote.animatorEmoteId, _emote.loops);
            _active = true;
            _activeEmote = _emote;
            _startFrame = Time.frameCount;
            if (!_emote.loops)
            {
                _oneShotEndTime = Time.time + Mathf.Max(0f, _emote.oneShotSeconds);
            }
            EmoteStarted?.Invoke();
        }

        /// <summary>
        /// Stop the current emote (if any) and leave the self-feedback view. Idempotent. Clears the server
        /// <c>Emoting</c> bool for LOOP emotes (one-shots return on their own via exit-time).
        /// </summary>
        public void End()
        {
            if (!_active)
            {
                return;
            }

            bool _wasLoop = _activeEmote != null && _activeEmote.loops;
            _active = false;
            _activeEmote = null;
            if (_wasLoop)
            {
                ResolveLocalAvatar()?.StopEmote();
            }
            EmoteStopped?.Invoke();
        }

        // Defensive teardown: if this controller is disabled / destroyed mid-emote without an arbiter-driven End()
        // (e.g. the local avatar despawns, a spectator transition), stop cleanly so the Emoting bool is cleared
        // and EmoteStopped fires (the arbiter stands the orbit camera down + re-hides the body).
        private void OnDisable() => End();

        private void Update()
        {
            if (!_active)
            {
                return;
            }

            // Never self-stop on the frame the emote started (the wheel-release input of that frame is not a
            // "next action"). Only fresh presses AFTER the start frame count.
            if (Time.frameCount == _startFrame)
            {
                return;
            }

            Keyboard _keyboard = Keyboard.current;
            Mouse _mouse = Mouse.current;
            bool _anyKey = _keyboard != null && _keyboard.anyKey.wasPressedThisFrame;
            bool _anyMouseButton = _mouse != null &&
                (_mouse.leftButton.wasPressedThisFrame
                 || _mouse.rightButton.wasPressedThisFrame
                 || _mouse.middleButton.wasPressedThisFrame);

            if (EmoteStop.ShouldStop(_anyKey, _anyMouseButton))
            {
                End();
                return;
            }

            // One-shot: auto-return once its clip window elapses (no player action needed).
            if (_activeEmote != null && !_activeEmote.loops && Time.time >= _oneShotEndTime)
            {
                End();
            }
        }

        // Lazily resolve (and cache) the local owned avatar — same pattern as EmoteWheelInput / the cameras.
        // Re-resolves if the cached one was torn down.
        private PlayerAvatar ResolveLocalAvatar()
        {
            if (_localAvatar != null)
            {
                return _localAvatar;
            }

            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null)
            {
                return null;
            }

            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _localAvatar = _avatar;
                    break;
                }
            }
            return _localAvatar;
        }
    }
}
