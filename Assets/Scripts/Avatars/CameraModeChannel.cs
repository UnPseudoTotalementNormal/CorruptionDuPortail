using System;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Reusable, decoupled broadcast of the current <see cref="CameraMode"/> (a ScriptableObject event
    /// channel — no singleton). The <see cref="AvatarCameraArbiter"/> is the SOLE writer (it already owns the
    /// state→mode decision); ANY element can reference this asset and read <see cref="Current"/> or subscribe
    /// to <see cref="OnChanged"/> to gate presentation on "are we seated first-person" (or any other mode) —
    /// e.g. the card hover, and future elements, without coupling to the arbiter.
    /// </summary>
    [CreateAssetMenu(menuName = "Corruption/Camera Mode Channel", fileName = "CameraModeChannel")]
    public class CameraModeChannel : ScriptableObject
    {
        public CameraMode Current { get; private set; } = CameraMode.Board;
        public event Action<CameraMode> OnChanged;

        /// <summary>
        /// Whether the seated first-person board-camera node is the LIVE camera (vs a board overview the player
        /// arrowed to). <see cref="Current"/> alone is too coarse: the whole Vote is <c>Embodied</c>, but the
        /// player can navigate to an overhead overview while still in that mode — where the first-person look-at
        /// (which aims a card at <c>Camera.main</c>) would lay the card flat. Consumers gate the look-at on
        /// <c>Current == Embodied &amp;&amp; SeatedFirstPersonLive</c>. Defaults false (no first-person live).
        /// </summary>
        public bool SeatedFirstPersonLive { get; private set; }
        public event Action<bool> OnSeatedFirstPersonLiveChanged;

        // Domain reload is disabled in this project, so a SO's runtime state survives Play sessions — reset to
        // the neutral Board mode (and no live first-person) on (re)load so a stale value can't leak into the
        // next run.
        private void OnEnable()
        {
            Current = CameraMode.Board;
            SeatedFirstPersonLive = false;
        }

        /// <summary>Arbiter-only: publish the resolved mode. No-op + no event if unchanged.</summary>
        public void Set(CameraMode _mode)
        {
            if (_mode == Current)
            {
                return;
            }
            Current = _mode;
            OnChanged?.Invoke(_mode);
        }

        /// <summary>Arbiter-only: publish whether the seated first-person node is the live camera. No-op + no
        /// event if unchanged.</summary>
        public void SetSeatedFirstPersonLive(bool _live)
        {
            if (_live == SeatedFirstPersonLive)
            {
                return;
            }
            SeatedFirstPersonLive = _live;
            OnSeatedFirstPersonLiveChanged?.Invoke(_live);
        }
    }
}
