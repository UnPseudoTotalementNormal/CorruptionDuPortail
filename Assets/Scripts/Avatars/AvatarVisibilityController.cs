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
        // Shader property on "Corruption/Cat Dither Lit": 1 = fully visible, 0 = fully clipped (screen-door
        // dither). Driven per RENDERER via a MaterialPropertyBlock — the avatars share one material, so writing
        // the material itself would fade everyone at once (and instancing it would leak).
        private static readonly int CatFadeAmountId = Shader.PropertyToID("_CatFadeAmount");

        [Header("Dither fade (instead of popping in/out)")]
        [Tooltip("Cross-fade bodies with a screen-door dither instead of snapping the renderers on/off.")]
        [SerializeField] private bool _ditherFade = true;
        [Tooltip("MODULARITY KNOB. Off = fade the EMOTE self-view only (the owner's own body — the only body " +
                 "whose visibility the emote override drives). On = fade EVERY show/hide, including the " +
                 "night/day reveal of the other avatars. Flip this to generalise the effect later.")]
        [SerializeField] private bool _fadeAllTransitions = false;
        [Tooltip("Seconds for a full 0->1 fade.")]
        [SerializeField] private float _fadeSeconds = 0.25f;

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
        // Current dither-fade amount per avatar (0..1), eased toward the target each frame.
        private readonly Dictionary<PlayerAvatar, float> _fade = new();
        // One reusable block — allocating a MaterialPropertyBlock per renderer per frame would garbage.
        private MaterialPropertyBlock _propertyBlock;

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
                ApplyFaded(_avatar, _visible);
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
                _fade.Remove(_deadKeys[_i]);
            }
        }

        /// <summary>
        /// Ease one avatar's body toward <paramref name="_visible"/>. With the dither fade on, the body dissolves
        /// in/out over <see cref="_fadeSeconds"/> through the shader's <c>_CatFadeAmount</c>; the renderers stay
        /// enabled while any pixel survives and are only switched off once fully clipped (a hidden body then
        /// costs no draw call). Without it, the original instant toggle.
        ///
        /// SCOPE (modular): only the OWNER's body fades by default. Its visibility is driven ENTIRELY by the emote
        /// override — <see cref="ResolveVisible"/> is always false for the owner — so "fade the owner" is exactly
        /// "fade the emote self-view". <see cref="_fadeAllTransitions"/> widens it to every avatar.
        /// </summary>
        private void ApplyFaded(PlayerAvatar _avatar, bool _visible)
        {
            float _target = _visible ? 1f : 0f;
            bool _shouldFade = _ditherFade && (_fadeAllTransitions || _avatar.IsOwner);

            // First sight of an avatar starts AT the target, so a freshly spawned body does not fade in.
            if (!_fade.TryGetValue(_avatar, out float _current))
            {
                _current = _target;
            }

            _current = _shouldFade && _fadeSeconds > 0f
                ? Mathf.MoveTowards(_current, _target, Time.deltaTime / _fadeSeconds)
                : _target;
            _fade[_avatar] = _current;

            Apply(_avatar, _current > 0f, _current);
        }

        private void Apply(PlayerAvatar _avatar, bool _visible, float _fadeAmount)
        {
            if (!_renderers.TryGetValue(_avatar, out Renderer[] _rs) || _rs == null)
            {
                // Include inactive so a model toggled off elsewhere is still tracked.
                _rs = _avatar.GetComponentsInChildren<Renderer>(true);
                _renderers[_avatar] = _rs;
            }

            _propertyBlock ??= new MaterialPropertyBlock();

            foreach (Renderer _r in _rs)
            {
                if (_r == null)
                {
                    continue;
                }
                if (_r.enabled != _visible)
                {
                    _r.enabled = _visible;
                }
                if (!_visible)
                {
                    continue;
                }

                // Per-RENDERER override: the avatars share one material, so this is what lets them dissolve
                // independently without instancing (which would leak a material per body).
                _r.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(CatFadeAmountId, _fadeAmount);
                _r.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
