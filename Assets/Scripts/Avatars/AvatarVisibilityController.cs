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
    ///  • <c>Board</c> (the seated NIGHT phases) → EVERY avatar hidden.
    ///  • <c>FreeRoam</c> (lobby) / <c>Embodied</c> (the seated DAY phases: vote + recap) → every avatar
    ///    visible EXCEPT the LOCAL one (it is in first-person, so its own body would clip the camera).
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
        // Emote self-feedback: while the local player emotes they watch their OWN body via a third-person orbit
        // camera, so the normally-hidden owner body must be revealed. Arbiter-driven; overrides the "hide the
        // local first-person body" rule for the owner only, and only outside Board (night stays fully hidden).
        private bool _localBodyOverride;
        // Cached renderer arrays per avatar (GetComponentsInChildren allocates — do it once, not per frame).
        private readonly Dictionary<PlayerAvatar, Renderer[]> _renderers = new();
        // Reused scratch for evicting despawned avatars from _renderers: a despawned PlayerAvatar key is never
        // revisited by the live-roster loop, so without eviction its cached Renderer[] would leak for the
        // controller's life (one stale entry per disconnect across reconnect churn).
        private readonly List<PlayerAvatar> _deadKeys = new();

        /// <summary>Arbiter contract: the resolved camera mode (mirror AvatarSeatingPresenter.SetActive).</summary>
        public void SetMode(CameraMode _newMode) => _mode = _newMode;

        /// <summary>Arbiter contract: reveal the local (owner) body while emoting so the third-person orbit
        /// camera can show it. Only takes effect outside Board (night hides everyone). Applied next LateUpdate.</summary>
        public void SetLocalBodyVisibleOverride(bool _revealLocalBody) => _localBodyOverride = _revealLocalBody;

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
                bool _visible = ResolveVisible(_mode, _avatar.IsOwner);
                // Emote self-feedback override: reveal the OWNER's own body during an emote (third-person orbit
                // view) — including at night (Board). It is local + owner-only, so the "you only see each other
                // during the day" rule is preserved for OTHERS (non-owners stay hidden); the emoting player just
                // sees their own cat instead of orbiting empty space.
                if (_avatar.IsOwner && _localBodyOverride)
                {
                    _visible = true;
                }
                Apply(_avatar, _visible);
            }

            // Evict entries whose avatar was despawned (Unity-null key) — the live-roster loop above never
            // revisits them, so their cached Renderer[] would otherwise accumulate for the controller's life.
            _deadKeys.Clear();
            foreach (PlayerAvatar _key in _renderers.Keys)
            {
                if (_key == null)
                {
                    _deadKeys.Add(_key);
                }
            }
            for (int _i = 0; _i < _deadKeys.Count; _i++)
            {
                _renderers.Remove(_deadKeys[_i]);
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
