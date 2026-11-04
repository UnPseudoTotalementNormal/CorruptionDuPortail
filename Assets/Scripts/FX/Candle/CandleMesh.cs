using System.Collections.Generic;
using UnityEngine;

namespace FX.Candle
{
    /// <summary>
    /// Meshes of the vote candle (board task T19, the artist's model since T33): how its wax melts, and the quads of the
    /// flame and its halo.
    /// </summary>
    public static class CandleMesh
    {
        /// <summary>
        /// The artist's wax for a melt amount (objets-alex v1 K2): drawn out from its foot by <paramref name="_stretch"/> so
        /// there is wax to burn, then sunk by <paramref name="_drop"/> into the holder's cup, so its melted rim and drips stay
        /// as drawn; what would go under the foot stays flat on it, hidden in the cup. y only, up.
        /// </summary>
        public static void MeltWax(Vector3[] _source, Vector3[] _target, float _foot, float _stretch, float _drop)
        {
            for (int _i = 0; _i < _source.Length; _i++)
            {
                Vector3 _v = _source[_i];
                _v.y = Mathf.Max(_foot + (_v.y - _foot) * _stretch - _drop, _foot);
                _target[_i] = _v;
            }
        }

        /// <summary>A quad x in [-0.5, 0.5], y in [<paramref name="_bottom"/>, <paramref name="_bottom"/> + 1], facing -z.</summary>
        public static void BuildQuad(Mesh _mesh, float _bottom)
        {
            _mesh.Clear();
            _mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, _bottom, 0f), new Vector3(0.5f, _bottom, 0f),
                new Vector3(-0.5f, _bottom + 1f, 0f), new Vector3(0.5f, _bottom + 1f, 0f),
            });
            _mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            _mesh.RecalculateNormals();
            // The flame and its halo are turned to the camera in the shader: keep them from being culled.
            _mesh.bounds = new Bounds(new Vector3(0f, _bottom + 0.5f, 0f), Vector3.one * 2f);
        }
    }
}
