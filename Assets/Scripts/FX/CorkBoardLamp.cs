using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX
{
    /// <summary>
    /// A small lamp on the cork board at night (board task T11, 2026-07-23: "une petite lumière éclaire uniquement le
    /// tableau en liège afin qu'on puisse un minimum suivre l'avancée dans la nuit"), so the roles shown on it can be
    /// followed while the rest of the room stays dark.
    /// A framing projector, not a pool of light: the spot is placed on the board's normal and its cookie is the
    /// board's rectangle (<see cref="CorkBoardLampFraming"/>), so the table and the cards in front never catch it, and
    /// the board's own shadow stops what would go past it. Both are computed from the board at startup: moving or
    /// resizing the board keeps it framed. Fades in when the night (<see cref="AwakeningState"/>) begins and out when
    /// it ends. Presentation only, every player sees it.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class CorkBoardLamp : MonoBehaviour
    {
        [SerializeField] private Light _light;
        [Tooltip("The cork board (a box mesh whose face looks along -forward).")]
        [SerializeField] private MeshFilter _board;
        // Lane A (scene-wired), narrowed to the read slice like LightManager.
        [SerializeField] private GameManager _gameManager;
        [Tooltip("Distance from the board's face to the light. Further = flatter light, same framing.")]
        [SerializeField] private float _distance = 10f;
        [Tooltip("Brightness at the board's centre (lux).")]
        [SerializeField] private float _illuminance = 1.2f;
        [SerializeField] private CorkBoardLampFraming.Profile _profile = CorkBoardLampFraming.Profile.Default;
        [SerializeField] private int _cookieResolution = 1024;
        [SerializeField] private float _fadeInDuration = 1.5f;
        [SerializeField] private float _fadeOutDuration = 1f;

        private Texture2D _cookie;
        private float _nightIntensity;
        private bool _isNight;

        private IGameStateQuery Query => _gameManager;

        private void Awake()
        {
            Assert.IsNotNull(_light, "CorkBoardLamp._light is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_board, "CorkBoardLamp._board is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_gameManager, "CorkBoardLamp._gameManager is not wired — wire it in GameScene (the composition root).");
            Frame();
            _light.intensity = 0f;
            _light.enabled = false;
        }

        private void Start()
        {
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
            if (_light != null)
            {
                _light.DOKill();
            }
            if (_cookie != null)
            {
                Destroy(_cookie);
            }
        }

        /// <summary>Places and shapes the light on the board: position, cone, cookie and intensity.</summary>
        private void Frame()
        {
            CorkBoardLampFraming.Framing _framing =
                CorkBoardLampFraming.Frame(_board.transform, _board.sharedMesh.bounds, _distance);
            transform.SetPositionAndRotation(_framing.Position, _framing.Rotation);
            _light.type = LightType.Spot;
            _light.spotAngle = _framing.SpotAngle;
            _light.innerSpotAngle = _framing.SpotAngle;
            _cookie = CorkBoardLampFraming.BuildCookie(_framing, _profile, _cookieResolution, out float _centreValue);
            _light.cookie = _cookie;
            _nightIntensity = CorkBoardLampFraming.Intensity(_illuminance, _distance, _centreValue);
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            bool _night = Query.GetGameState(_newValue) is AwakeningState;
            if (_night == _isNight)
            {
                return;
            }
            _isNight = _night;
            _light.DOKill();
            if (_night)
            {
                _light.enabled = true;
                _light.DOIntensity(_nightIntensity, _fadeInDuration).SetEase(Ease.InOutSine);
            }
            else
            {
                _light.DOIntensity(0f, _fadeOutDuration).SetEase(Ease.InOutSine)
                    .OnComplete(() => _light.enabled = false);
            }
        }

        private void Reset()
        {
            _light = GetComponent<Light>();
        }
    }
}
