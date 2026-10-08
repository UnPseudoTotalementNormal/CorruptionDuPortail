#region

using System.Linq;
using Extensions;
using TMPro;
using UnityEngine;

#endregion

namespace Board.UI.PowerBar
{
    /// <summary>
    /// Board T2 / T1: a one-shot copied power (<c>Power.isStolenCopy</c>: Ugës's Marque copies, Luma's fake-card copy, a
    /// stolen Réincarnation's grants) is coated in slime on the table so the player reads it as "used once, then gone".
    /// Client-only cosmetic: a glossy translucent coat appended as the last material of each model renderer (a renderer
    /// with more materials than submeshes redraws its last submesh with the extra one) + drips emitted from the model.
    /// The look lives on the two assets (placeholder, design-owned), not here.
    /// </summary>
    public static class SingleUseSlimeMark
    {
        public const string DRIPS_OBJECT_NAME = "SingleUseSlimeDrips";

        /// <summary>
        /// Marks a built power visual. Idempotent (never stacks a second coat or drip emitter); a null asset skips its
        /// own part only.
        /// </summary>
        public static void Apply(Transform _visual, Material _coat, GameObject _dripPrefab)
        {
            if (!_visual)
            {
                return;
            }

            Transform _existingDrips = _visual.Find(DRIPS_OBJECT_NAME);
            Renderer _meshSource = null;
            foreach (Renderer _renderer in _visual.GetComponentsInChildren<Renderer>())
            {
                if (!IsModelSurface(_renderer, _existingDrips))
                {
                    continue;
                }
                if (!_meshSource && SharedMeshOf(_renderer))
                {
                    _meshSource = _renderer;
                }
                if (_coat)
                {
                    AppendCoat(_renderer, _coat);
                }
            }

            if (_dripPrefab && _meshSource && !_existingDrips)
            {
                AddDrips(_visual, _dripPrefab, _meshSource);
            }
        }

        // Only the model's own visible meshes: no particle / line renderers, no 3D text (TMP owns its materials),
        // nothing under an emitter we added.
        private static bool IsModelSurface(Renderer _renderer, Transform _existingDrips)
        {
            if (!(_renderer is MeshRenderer) && !(_renderer is SkinnedMeshRenderer))
            {
                return false;
            }
            if (_renderer.GetComponent<TMP_Text>())
            {
                return false;
            }
            return !_existingDrips || !_renderer.transform.IsChildOf(_existingDrips);
        }

        private static Mesh SharedMeshOf(Renderer _renderer)
        {
            if (_renderer is SkinnedMeshRenderer _skinned)
            {
                return _skinned.sharedMesh;
            }
            MeshFilter _filter = _renderer.GetComponent<MeshFilter>();
            return _filter ? _filter.sharedMesh : null;
        }

        private static void AppendCoat(Renderer _renderer, Material _coat)
        {
            Material[] _materials = _renderer.sharedMaterials;
            if (_materials.Contains(_coat))
            {
                return;
            }
            // sharedMaterials: the coat is one shared asset, no per-object material instance to leak.
            _renderer.sharedMaterials = _materials.Append(_coat).ToArray();
        }

        private static void AddDrips(Transform _visual, GameObject _dripPrefab, Renderer _meshSource)
        {
            GameObject _drips = Object.Instantiate(_dripPrefab, _visual);
            _drips.name = DRIPS_OBJECT_NAME;
            _drips.transform.localPosition = Vector3.zero;
            _drips.transform.localRotation = Quaternion.identity;
            _drips.SetLayerRecursively(_visual.gameObject.layer);

            ParticleSystem _particles = _drips.GetComponent<ParticleSystem>();
            if (!_particles)
            {
                return;
            }
            AimShapeAt(_particles, _meshSource);
            _particles.Play(true);
        }

        /// <summary>
        /// The marked object is leaving (spent copy shrinking out): stop new drops and let the falling ones finish on
        /// their own instead of vanishing with it. The detached emitter destroys itself once empty.
        /// </summary>
        public static void ReleaseDrips(Transform _visual)
        {
            Transform _drips = _visual ? _visual.Find(DRIPS_OBJECT_NAME) : null;
            if (!_drips || !_drips.TryGetComponent(out ParticleSystem _particles))
            {
                return;
            }
            ParticleSystem.MainModule _main = _particles.main;
            _main.stopAction = ParticleSystemStopAction.Destroy;
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _drips.SetParent(null, true);
        }

        // Drops leave the model's own surface when its mesh is CPU-readable. An imported model usually is not (Read/Write
        // off: the shape module would emit nothing in a player build), so the drops then fall from its bounding box.
        private static void AimShapeAt(ParticleSystem _particles, Renderer _meshSource)
        {
            ParticleSystem.ShapeModule _shape = _particles.shape;
            Mesh _mesh = SharedMeshOf(_meshSource);
            if (_mesh.isReadable)
            {
                if (_meshSource is SkinnedMeshRenderer _skinned)
                {
                    _shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    _shape.skinnedMeshRenderer = _skinned;
                }
                else
                {
                    _shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    _shape.meshRenderer = (MeshRenderer)_meshSource;
                }
                return;
            }

            // Bounds expressed in the emitter's space (both move and scale together, so this holds after the bar
            // re-scales its objects).
            Transform _emitter = _particles.transform;
            Bounds _bounds = _meshSource.bounds;
            Vector3 _size = _emitter.InverseTransformVector(_bounds.size);
            _shape.shapeType = ParticleSystemShapeType.Box;
            _shape.position = _emitter.InverseTransformPoint(_bounds.center);
            _shape.rotation = Vector3.zero;
            _shape.scale = new Vector3(Mathf.Abs(_size.x), Mathf.Abs(_size.y), Mathf.Abs(_size.z));
        }
    }
}
