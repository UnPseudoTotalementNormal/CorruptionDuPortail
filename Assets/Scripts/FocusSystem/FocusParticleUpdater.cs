using UnityEngine;

namespace FocusSystem
{
    public class FocusParticleUpdater : MonoBehaviour
    {
        public GameObject targetObject;
        private ParticleSystem ps;
        private Canvas[] allCanvas;

        private void Start()
        {
            ps = GetComponent<ParticleSystem>();
            if (targetObject != null)
            {
                allCanvas = targetObject.GetComponentsInChildren<Canvas>();
            }
        }

        private void Update()
        {
            if (targetObject == null || ps == null || allCanvas == null)
            {
                return;
            }
            
            UpdateParticleShape();
        }

        private void UpdateParticleShape()
        {
            Vector3 _worldSize = Vector3.zero;

            foreach (var _canvas in allCanvas)
            {
                RectTransform _rt = _canvas.GetComponent<RectTransform>();
                if (_rt != null)
                {
                    Vector3[] _corners = new Vector3[4];
                    _rt.GetWorldCorners(_corners);

                    float _width = Vector3.Distance(_corners[0], _corners[3]);
                    float _height = Vector3.Distance(_corners[0], _corners[1]);

                    Vector3 _w = new Vector3(_width, _height, 0.1f);
                    _worldSize = Vector3.Max(_worldSize, _w);
                }
            }

            var _shape = ps.shape;
            _shape.scale = new Vector3(_worldSize.x, 0.01f, _worldSize.y);
        }
    }
}

