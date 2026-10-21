using System.Collections.Generic;
using Avatars;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Presentation
{
    /// <summary>
    /// A light per cat (board task T11, 2026-07-16: each cat under its own light from above, shadows on the faces).
    /// While the seated first-person view is live, every other player's seated cat gets a copy of <see cref="_template"/>
    /// hung above it, tilted toward the table by <see cref="_elevation"/> (60° chosen on renders, 2026-10-16: the face
    /// reads, the light still comes from above) and fading in / out with the view. The cone is wide and soft so it holds
    /// the whole cat, ears included. Presentation only, local; the lights follow the seats
    /// <see cref="AvatarSeatingPresenter"/> places each frame (runs after it).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class CatSpotlights : MonoBehaviour
    {
        [SerializeField] private CameraModeChannel _cameraMode;
        [Tooltip("Disabled spot light copied once per seated cat (its colour, intensity, angle, range and shadows).")]
        [SerializeField] private Light _template;
        [Tooltip("Angle of the light above the horizontal, on the table side of the cat: 90 = straight above, lower = " +
                 "more light on the face.")]
        [Range(0f, 90f)]
        [SerializeField] private float _elevation = 60f;
        [Tooltip("Distance from the cat's head to the light (world units). Far enough for the cone to hold the ears.")]
        [SerializeField] private float _distance = 30f;
        [Tooltip("The light aims this far below the cat's eye pivot (world units), at the middle of the head.")]
        [SerializeField] private float _aimBelowEye = 0.3f;
        [Tooltip("Seconds to fade the lights in when the seated view starts, and out when it ends.")]
        [SerializeField] private float _fadeSeconds = 0.4f;

        [Tooltip("T23 (mockups jour-pov v1 H2): the light of the cat the player points at is this many times stronger.")]
        [SerializeField] private float _hoverBoost = 1.8f;
        [Tooltip("Seconds for a pointed cat's light to swell / settle.")]
        [SerializeField] private float _hoverBoostSeconds = 0.15f;

        private readonly List<Light> _lights = new();
        private readonly List<PlayerAvatar> _avatars = new();
        private readonly Dictionary<PlayerAvatar, float> _boostLevels = new();
        private float _level;

        /// <summary>The cat whose light swells (the one the player points at, or whose card), null = none.</summary>
        public PlayerAvatar Boosted { get; set; }

        /// <summary>Autoplay before / after captures: false = keep the lights off even in the seated view.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Multiplies every cat's light, 1 = as tuned. The vote candle (T19) lowers it as the room goes dark.</summary>
        public float Dim { get; set; } = 1f;

        /// <summary>Multiplies every cat's light too: the room going dark around a chained cat (T19), 1 = as tuned.</summary>
        public float Darken { get; set; } = 1f;

        /// <summary>A cat lifted off its seat (T19): its light stays off, the chaining's own light follows it. Null = none.</summary>
        public PlayerAvatar Lifted { get; set; }

        private void Awake()
        {
            Assert.IsNotNull(_cameraMode, "CatSpotlights._cameraMode not wired");
            Assert.IsNotNull(_template, "CatSpotlights._template not wired");
            if (_template != null)
            {
                _template.enabled = false;
            }
        }

        private void OnDisable()
        {
            _level = 0f;
            ShowLights(0);
        }

        private void LateUpdate()
        {
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            bool _wanted = Enabled && _cameraMode != null && _cameraMode.SeatedFirstPersonLive && _manager != null;
            float _step = _fadeSeconds > 0f ? Time.unscaledDeltaTime / _fadeSeconds : 1f;
            _level = Mathf.MoveTowards(_level, _wanted ? 1f : 0f, _step);
            if (_level <= 0f || _manager == null || _template == null)
            {
                ShowLights(0);
                return;
            }

            // Every seated cat but the player's own (the camera sits in its head).
            _avatars.Clear();
            IReadOnlyList<PlayerAvatar> _live = _manager.GetAvatars();
            for (int _i = 0; _i < _live.Count; _i++)
            {
                if (_live[_i] != null && !_live[_i].IsOwner && _live[_i] != Lifted)
                {
                    _avatars.Add(_live[_i]);
                }
            }

            while (_lights.Count < _avatars.Count)
            {
                Light _light = Instantiate(_template, transform);
                _light.name = $"CatSpotlight {_lights.Count}";
                _lights.Add(_light);
            }
            for (int _i = 0; _i < _avatars.Count; _i++)
            {
                PlayerAvatar _avatar = _avatars[_i];
                Vector3 _head = _avatar.EyePivot != null ? _avatar.EyePivot.position : _avatar.transform.position;
                Pose _pose = CatSpotlightPose.Compute(_head + Vector3.down * _aimBelowEye, _avatar.transform.forward,
                    _elevation, _distance);
                Light _light = _lights[_i];
                CopySettings(_light);
                _light.intensity *= 1f + (_hoverBoost - 1f) * BoostLevel(_avatar);
                _light.transform.SetPositionAndRotation(_pose.position, _pose.rotation);
            }
            ShowLights(_avatars.Count);
        }

        // Eases the pointed cat's light up and the others back down.
        private float BoostLevel(PlayerAvatar _avatar)
        {
            _boostLevels.TryGetValue(_avatar, out float _boost);
            float _step = _hoverBoostSeconds > 0f ? Time.unscaledDeltaTime / _hoverBoostSeconds : 1f;
            _boost = Mathf.MoveTowards(_boost, _avatar == Boosted ? 1f : 0f, _step);
            _boostLevels[_avatar] = _boost;
            return _boost;
        }

        // Keep each copy in step with the template (tuned live in the editor), times the fade.
        private void CopySettings(Light _light)
        {
            _light.color = _template.color;
            _light.intensity = _template.intensity * _level * Dim * Darken;
            _light.range = _template.range;
            _light.spotAngle = _template.spotAngle;
            _light.innerSpotAngle = _template.innerSpotAngle;
            _light.shadows = _template.shadows;
            _light.shadowStrength = _template.shadowStrength;
        }

        private void ShowLights(int _count)
        {
            for (int _i = 0; _i < _lights.Count; _i++)
            {
                if (_lights[_i] != null)
                {
                    _lights[_i].enabled = _i < _count;
                }
            }
        }
    }
}
