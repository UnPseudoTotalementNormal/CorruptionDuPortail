using DG.Tweening;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX
{
    /// <summary>
    /// A small lamp that lights one flat target and nothing around it (board task T11): a framing projector, the spot set
    /// on the target's normal with a cookie computed at startup from the target's outline (<see cref="LampFraming"/>), a
    /// soft pool rather than a lit rectangle. Moving or resizing the target keeps it framed. Subclasses give the target
    /// and say when the lamp is on (<see cref="SetLit"/>); it fades in and out. Presentation only.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public abstract class FramedLamp : MonoBehaviour
    {
        [SerializeField] private Light _light;
        [Tooltip("Distance from the target's face to the light. Further = flatter light, same framing.")]
        [SerializeField] private float _distance = 10f;
        [Tooltip("Brightness at the target's centre (lux).")]
        [SerializeField] private float _illuminance = 1.2f;
        [SerializeField] protected LampFraming.Profile _profile = LampFraming.Profile.Default;
        [SerializeField] private int _cookieResolution = 1024;
        [SerializeField] private float _fadeInDuration = 1.5f;
        [SerializeField] private float _fadeOutDuration = 1f;

        private Texture2D _cookie;
        private float _litIntensity;
        private bool _isLit;

        /// <summary>The target's framing, for a light <paramref name="_distance"/> away whose pool spills <paramref name="_spill"/>.</summary>
        protected abstract LampFraming.Framing FrameTarget(float _distance, float _spill);

        protected virtual void Awake()
        {
            Assert.IsNotNull(_light, $"{GetType().Name}._light is not wired — wire it in GameScene (the composition root).");
            Frame();
            _light.intensity = 0f;
            _light.enabled = false;
        }

        protected virtual void OnDestroy()
        {
            if (_light != null)
            {
                _light.DOKill();
            }
            if (_cookie != null)
            {
                Destroy(_cookie);
            }
        }

        /// <summary>Places and shapes the light on the target: position, cone, cookie and intensity.</summary>
        private void Frame()
        {
            LampFraming.Framing _framing = FrameTarget(_distance, _profile.spill);
            transform.SetPositionAndRotation(_framing.Position, _framing.Rotation);
            _light.type = LightType.Spot;
            _light.spotAngle = _framing.SpotAngle;
            _light.innerSpotAngle = _framing.SpotAngle;
            _cookie = LampFraming.BuildCookie(_framing, _profile, _cookieResolution, out float _centreValue);
            _light.cookie = _cookie;
            _litIntensity = LampFraming.Intensity(_illuminance, _distance, _centreValue);
        }

        /// <summary>Fades the lamp in (<paramref name="_lit"/>) or out; nothing if it already is.</summary>
        protected void SetLit(bool _lit)
        {
            if (_lit == _isLit)
            {
                return;
            }
            _isLit = _lit;
            _light.DOKill();
            if (_lit)
            {
                _light.enabled = true;
                _light.DOIntensity(_litIntensity, _fadeInDuration).SetEase(Ease.InOutSine);
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
            _profile = DefaultProfile;
        }

        /// <summary>The look a freshly added lamp starts with.</summary>
        protected virtual LampFraming.Profile DefaultProfile => LampFraming.Profile.Default;
    }
}
