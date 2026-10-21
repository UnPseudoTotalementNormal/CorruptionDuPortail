using Avatars;
using UnityEngine;
using UnityEngine.Assertions;

namespace Presentation
{
    /// <summary>
    /// Pitch-black surroundings in the seated first-person view (board task T11, 2026-07-13: "le fond réellement noir
    /// tout autour lorsqu'on regarde en POV"). While the seated node is the live camera, the room (walls, ceiling, floors)
    /// is hidden, the camera clears to black and <see cref="_seatedFloor"/> takes the floor's place: the same wood, unlit
    /// (shader "Corruption/Seated Floor"), well visible under the table and the cats then fading to black in a circle
    /// centred on the table. The table floats in the void yet stays grounded wherever the player turns their head.
    /// Presentation only, local: the other views keep the room.
    /// </summary>
    public class SeatedSurroundings : MonoBehaviour
    {
        [SerializeField] private CameraModeChannel _cameraMode;
        [SerializeField] private Camera _camera;
        [Tooltip("The room around the table (walls, ceiling, floors): hidden in the seated first-person view.")]
        [SerializeField] private Renderer[] _room;
        [Tooltip("The seated view's own floor (wood fading to black around the table): shown in the seated " +
                 "first-person view only.")]
        [SerializeField] private GameObject _seatedFloor;

        private bool _isBlack;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackground;

        /// <summary>Off = the room stays visible in first person too (autoplay before / after captures).</summary>
        public bool Enabled { get; set; } = true;

        private void Awake()
        {
            Assert.IsNotNull(_cameraMode, "SeatedSurroundings._cameraMode not wired");
            Assert.IsNotNull(_camera, "SeatedSurroundings._camera not wired");
        }

        private void OnEnable()
        {
            if (_cameraMode != null)
            {
                _cameraMode.OnSeatedFirstPersonLiveChanged += OnSeatedLiveChanged;
            }
            Apply();
        }

        private void OnDisable()
        {
            if (_cameraMode != null)
            {
                _cameraMode.OnSeatedFirstPersonLiveChanged -= OnSeatedLiveChanged;
            }
            SetBlack(false);
        }

        private void OnSeatedLiveChanged(bool _live) => Apply();

        /// <summary>Re-evaluate after <see cref="Enabled"/> changed.</summary>
        public void Apply() => SetBlack(Enabled && _cameraMode != null && _cameraMode.SeatedFirstPersonLive);

        private void SetBlack(bool _black)
        {
            if (_black == _isBlack)
            {
                return;
            }
            _isBlack = _black;
            foreach (Renderer _renderer in _room)
            {
                if (_renderer != null)
                {
                    _renderer.enabled = !_black;
                }
            }
            if (_seatedFloor != null)
            {
                _seatedFloor.SetActive(_black);
            }
            if (_camera == null)
            {
                return;
            }
            if (_black)
            {
                _savedClearFlags = _camera.clearFlags;
                _savedBackground = _camera.backgroundColor;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
            }
            else
            {
                _camera.clearFlags = _savedClearFlags;
                _camera.backgroundColor = _savedBackground;
            }
        }
    }
}
