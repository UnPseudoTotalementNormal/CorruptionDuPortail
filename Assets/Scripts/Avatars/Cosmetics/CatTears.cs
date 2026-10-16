using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// The tears of the "Crying" animation (the artist's particle system: tear cards shot from an emitter on the head,
    /// falling under gravity). Built at runtime on the cat's "Tear Emitter" part, it emits while the Animator is in a
    /// state tagged "Crying" (the emote replicates through the NetworkAnimator, so every peer sees the tears).
    /// </summary>
    [RequireComponent(typeof(CatAppearance))]
    public class CatTears : MonoBehaviour
    {
        private const string CryingTag = "Crying";
        private const string EmitterPart = "Tear Emitter";
        // Height of the cat in the artist's file (Blender units): the particle settings below are in those units.
        private const float SourceCatHeight = 4.37f;
        // Particle size of the file (a scale on the tear card) and the card's size there (Blender units).
        private const float TearSize = 0.05f;
        private const float SourceTearCardSize = 2f;

        [SerializeField] private Animator animator;

        [Tooltip("The tear card (Cat_Cosmetics.fbx > Tear Particle).")]
        [SerializeField] private Mesh tearMesh;

        [SerializeField] private Material tearMaterial;

        [Tooltip("Tears per second (the file: 1000 over 200 frames at 24 fps).")]
        [SerializeField] private float rate = 120f;

        private ParticleSystem _tears;

        private void Start()
        {
            var appearance = GetComponent<CatAppearance>();
            GameObject emitter = appearance.Part(EmitterPart);
            if (emitter == null || tearMesh == null || animator == null) return;

            float scale = CatHeight() / SourceCatHeight;
            var go = new GameObject("Tears");
            go.transform.SetParent(emitter.transform.parent, false);
            go.transform.localPosition = emitter.transform.localPosition;
            go.transform.localRotation = emitter.transform.localRotation;
            go.transform.localScale = emitter.transform.localScale;
            _tears = go.AddComponent<ParticleSystem>();
            _tears.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _tears.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 50f / 24f;
            main.startSpeed = 5f * scale; // the file: 5 units/s along the emitter's normals (a mesh shape emits along them)
            // A mesh particle's size multiplies the mesh: aim at the file's tear size on this cat, whatever the import
            // scale of the card (the FBX meshes come in at 1/100 under a x100 node).
            float meshSize = Mathf.Max(tearMesh.bounds.size.x, Mathf.Max(tearMesh.bounds.size.y, tearMesh.bounds.size.z));
            float size = TearSize * SourceTearCardSize * scale / Mathf.Max(meshSize, 1e-5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * size, size); // size random 1
            // The emitter node's x100 import scale must shape the emission only, not the tears' size and speed.
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.gravityModifier = scale;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            ParticleSystem.EmissionModule emission = _tears.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = _tears.shape;
            MeshFilter filter = emitter.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable)
            {
                shape.shapeType = ParticleSystemShapeType.Mesh;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                shape.mesh = filter.sharedMesh;
                shape.normalOffset = 0f;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.3f * scale;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = tearMesh;
            renderer.sharedMaterial = tearMaterial;
            renderer.alignment = ParticleSystemRenderSpace.Velocity;
        }

        private void Update()
        {
            if (_tears == null) return;
            bool crying = animator.GetCurrentAnimatorStateInfo(0).IsTag(CryingTag);
            if (crying && !_tears.isEmitting) _tears.Play();
            else if (!crying && _tears.isEmitting) _tears.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private float CatHeight()
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any ? Mathf.Max(bounds.size.y, 0.01f) : SourceCatHeight;
        }
    }
}
