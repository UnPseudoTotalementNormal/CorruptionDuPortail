using UnityEngine;
using UnityEngine.UI;

namespace Presentation
{
    /// <summary>
    /// A shadow cast inward along a rect's four edges: its colour at the edge, fading to nothing <see cref="Depth"/> in
    /// (mockups rangees-jour v3 F5: the day cork's frame shades the cork next to it). Vertex colours only, no texture,
    /// so any UI material that reads the vertex colour draws it.
    /// </summary>
    public class InnerShadowGraphic : MaskableGraphic
    {
        [Tooltip("How far in from each edge the shadow reaches, in this rect's units.")]
        [SerializeField] private float _depth = 10f;

        public float Depth
        {
            get => _depth;
            set
            {
                _depth = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper _vh)
        {
            _vh.Clear();
            Rect _r = GetPixelAdjustedRect();
            float _d = Mathf.Clamp(_depth, 0f, Mathf.Min(_r.width, _r.height) / 2f);
            Color32 _edge = color;
            Color32 _clear = _edge;
            _clear.a = 0;
            // Outer corners 0..3, inner corners 4..7, clockwise from the bottom left; one band per edge.
            _vh.AddVert(new Vector3(_r.xMin, _r.yMin), _edge, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMin, _r.yMax), _edge, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMax, _r.yMax), _edge, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMax, _r.yMin), _edge, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMin + _d, _r.yMin + _d), _clear, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMin + _d, _r.yMax - _d), _clear, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMax - _d, _r.yMax - _d), _clear, Vector2.zero);
            _vh.AddVert(new Vector3(_r.xMax - _d, _r.yMin + _d), _clear, Vector2.zero);
            for (int _k = 0; _k < 4; _k++)
            {
                int _n = (_k + 1) % 4;
                _vh.AddTriangle(_k, _n, 4 + _n);
                _vh.AddTriangle(_k, 4 + _n, 4 + _k);
            }
        }
    }
}
