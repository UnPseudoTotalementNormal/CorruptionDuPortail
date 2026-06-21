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

        // Domain reload is disabled in this project, so a SO's runtime state survives Play sessions — reset to
        // the neutral Board mode on (re)load so a stale value can't leak into the next run.
        private void OnEnable() => Current = CameraMode.Board;

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
    }
}
