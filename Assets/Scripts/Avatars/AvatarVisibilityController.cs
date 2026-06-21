using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// The SINGLE owner of avatar body visibility (LOCAL, presentation-only — NOT a NetworkBehaviour).
    /// Driven by the <see cref="AvatarCameraArbiter"/> with the resolved <see cref="CameraMode"/>.
    ///
    /// POLICY ("you only see each other during the day"):
    ///  • <c>Board</c> (everything that is not the lobby nor the seated Vote) → EVERY avatar hidden.
    ///  • <c>FreeRoam</c> (lobby) / <c>Embodied</c> (seated Vote) → every avatar visible EXCEPT the LOCAL
    ///    one (it is in first-person, so its own body would clip the camera).
    ///
    /// This centralizes ALL renderer toggling that previously lived split across
    /// <see cref="AvatarFollowCamera"/> and <see cref="AvatarEmbodiedCamera"/> (each hid the local body on
    /// activate/deactivate). With one owner, a Board "hide all" can't fight a camera's local show/hide on the
    /// same renderers across mode transitions. Renderers are cached per avatar and only written when the
    /// target state changes (cheap, idempotent); late-spawning avatars are picked up the next LateUpdate.
    /// </summary>
    public class AvatarVisibilityController : MonoBehaviour
    {
        private CameraMode _mode = CameraMode.Board;
        // Cached renderer arrays per avatar (GetComponentsInChildren allocates — do it once, not per frame).
        private readonly Dictionary<PlayerAvatar, Renderer[]> _renderers = new();

        /// <summary>Arbiter contract: the resolved camera mode (mirror AvatarSeatingPresenter.SetActive).</summary>
        public void SetMode(CameraMode _newMode) => _mode = _newMode;

        /// <summary>
        /// Pure visibility policy (EditMode-testable): is an avatar's body shown in <paramref name="_mode"/>?
        /// Board hides everyone; otherwise everyone is shown except the local (owner) first-person body.
        /// </summary>
        public static bool ResolveVisible(CameraMode _mode, bool _isOwner)
        {
            if (_mode == CameraMode.Board)
            {
                return false;
            }
            return !_isOwner;
        }

        private void LateUpdate()
        {
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null)
            {
                return;
            }

            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                if (_avatar == null)
                {
                    continue;
                }
                Apply(_avatar, ResolveVisible(_mode, _avatar.IsOwner));
            }
        }

        private void Apply(PlayerAvatar _avatar, bool _visible)
        {
            if (!_renderers.TryGetValue(_avatar, out Renderer[] _rs) || _rs == null)
            {
                // Include inactive so a model toggled off elsewhere is still tracked.
                _rs = _avatar.GetComponentsInChildren<Renderer>(true);
                _renderers[_avatar] = _rs;
            }
            foreach (Renderer _r in _rs)
            {
                if (_r != null && _r.enabled != _visible)
                {
                    _r.enabled = _visible;
                }
            }
        }
    }
}
