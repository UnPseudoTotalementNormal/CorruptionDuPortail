using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Story 13.1 (Epic 13 — Player Embodiment & Physical Presence). Data-driven appearance hook
    /// (FR8 / DO6). Today it carries the SINGLE shared body material every avatar uses. The seam is
    /// shaped so per-player customization can be added later WITHOUT re-architecting: a future
    /// NetworkVariable&lt;int&gt; appearanceId (or a per-player profile) can select among multiple
    /// entries while <see cref="PlayerAvatar"/>.ApplyAppearance's call site stays unchanged.
    ///
    /// The SO reference lives on the avatar PREFAB, so every client's replica reads the same data with
    /// zero network traffic (the network-correct seam). SOs are READ-ONLY at runtime
    /// (project-context.md §ScriptableObject) — never mutate this asset; if per-player runtime state is
    /// ever needed, clone via Instantiate(so). Customization itself is NOT implemented in 13.1.
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarAppearanceData", menuName = "Avatars/Avatar Appearance Data")]
    public class AvatarAppearanceData : ScriptableObject
    {
        [Tooltip("The shared body material applied to the avatar model's renderers today. Later, multiple " +
                 "appearance entries can be selected per player without changing the ApplyAppearance call site.")]
        [SerializeField] private Material _bodyMaterial;

        [Tooltip("Optional shared body model prefab. Unused in 13.1 (the model is a child of the avatar " +
                 "prefab); reserved so a future data-driven appearance can swap the mesh from data.")]
        [SerializeField] private GameObject _modelPrefab;

        public Material BodyMaterial => _bodyMaterial;
        public GameObject ModelPrefab => _modelPrefab;
    }
}
