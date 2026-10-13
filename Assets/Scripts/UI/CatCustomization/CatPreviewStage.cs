using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace UI.CatCustomization
{
    /// <summary>
    /// The player's cat in the main menu, alive (Idle animation) and dressed from the saved look, filmed into a
    /// transparent RenderTexture that the menu (uGUI RawImage) and the customization screen (UI Toolkit) both show.
    /// Built at runtime far below the menu: the game's Cat_Avatar model with its idle motion, its gameplay / network
    /// components stripped (no NetworkManager in the menu), one camera and one light of its own.
    /// </summary>
    public class CatPreviewStage : MonoBehaviour
    {
        [Tooltip("The game's cat model (Cat_Avatar prefab): the same body as in game, with its CatAppearance.")]
        [SerializeField] private GameObject catModelPrefab;

        [Tooltip("Where the stage is built, out of every menu camera's sight.")]
        [SerializeField] private Vector3 stagePosition = new(0f, -1000f, 0f);

        [SerializeField] private Vector2Int textureSize = new(900, 1100);

        [Tooltip("Vertical field of view of the stage camera.")]
        [SerializeField] private float fieldOfView = 24f;

        [Tooltip("Initial yaw of the cat (degrees): a three-quarter view reads the outfit better than a flat front.")]
        [SerializeField] private float startYaw = -18f;

        private Transform _turntable;
        private CatAppearance _appearance;
        private Camera _camera;
        private RenderTexture _texture;
        private float _yaw;

        public RenderTexture Texture
        {
            get
            {
                Build();
                return _texture;
            }
        }

        public CatLook Look => _appearance != null ? _appearance.Current : CatLook.Default;

        private void Awake()
        {
            Build();
            CatLookStore.Changed += Apply;
        }

        private void OnDestroy()
        {
            CatLookStore.Changed -= Apply;
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        public void Apply(CatLook look)
        {
            Build();
            if (_appearance != null)
            {
                _appearance.Apply(look);
            }
        }

        /// <summary>Turns the cat on itself (drag on the customization screen).</summary>
        public void AddYaw(float degrees)
        {
            _yaw += degrees;
            if (_turntable != null)
            {
                _turntable.localRotation = Quaternion.Euler(0f, 180f + _yaw, 0f);
            }
        }

        public void ResetYaw()
        {
            _yaw = startYaw;
            AddYaw(0f);
        }

        private void Build()
        {
            if (_texture != null || catModelPrefab == null)
            {
                return;
            }

            var root = new GameObject("CatPreviewStage (runtime)");
            root.transform.SetParent(transform, false);
            root.transform.position = stagePosition;

            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(root.transform, false);

            // Built inactive so the stripped components never run their Awake / OnEnable.
            var holder = new GameObject("Model");
            holder.SetActive(false);
            holder.transform.SetParent(_turntable, false);
            GameObject cat = Instantiate(catModelPrefab, holder.transform);
            // Kept: the dresser and the procedural idle (breathing, ears, whiskers, bow; no network, it falls back to
            // the model as its root). Everything else needs the game (NetworkAnimator, head look, hops).
            foreach (MonoBehaviour behaviour in cat.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(behaviour is CatAppearance) && !(behaviour is Avatars.AvatarIdleMotion))
                {
                    DestroyImmediate(behaviour);
                }
            }
            holder.SetActive(true);
            _appearance = cat.GetComponentInChildren<CatAppearance>(true);
            if (_appearance == null)
            {
                Debug.LogError("[CatPreviewStage] The cat model has no CatAppearance: the menu cat cannot be dressed.", this);
            }

            // Frame the whole body from its bounds (the model's pivot is at its feet).
            Bounds bounds = BoundsOf(cat);
            float height = Mathf.Max(bounds.size.y, 0.01f);
            float distance = height * 0.62f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

            _texture = new RenderTexture(textureSize.x, textureSize.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "CatPreview",
                antiAliasing = 4,
            };
            _texture.Create();

            var cameraGo = new GameObject("Camera");
            cameraGo.transform.SetParent(root.transform, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.fieldOfView = fieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = distance * 4f;
            _camera.targetTexture = _texture;
            // URP adds its camera data with post-processing off: the texture keeps its alpha (the menu shows through).
            Vector3 centre = bounds.center - root.transform.position;
            cameraGo.transform.localPosition = new Vector3(centre.x, centre.y + height * 0.04f, centre.z - distance);
            cameraGo.transform.localRotation = Quaternion.identity;

            var lightGo = new GameObject("Key light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(28f, -32f, 0f);
            Light key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            key.color = new Color(1f, 0.93f, 0.84f);

            ResetYaw();
            Apply(CatLookStore.Load());
        }

        private static Bounds BoundsOf(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position + Vector3.up, Vector3.one * 2f);
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }
    }
}
