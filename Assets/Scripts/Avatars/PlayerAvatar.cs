using Unity.Netcode;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Story 13.1 (Epic 13 — Player Embodiment &amp; Physical Presence). The per-player networked avatar
    /// body. Spawned server-side per REAL client by <see cref="AvatarManager"/>, replicated to every
    /// client, and persists for the whole match. Carries its owner identity and applies the data-driven
    /// appearance hook. NO movement / camera / embodiment / voice here — those are stories 13.2–13.6.
    ///
    /// Lane C (NGO replica): any manager dependency would be resolved ONCE in OnNetworkSpawn via
    /// CompositionRoot.For(NetworkManager) (mirrors Character.cs:49). 13.1 needs none — appearance is read
    /// from the avatar's own prefab-baked [SerializeField] SO (identical on every replica, zero network
    /// traffic) — so OnNetworkSpawn only applies appearance. Tracking/registration is done BY the
    /// AvatarManager via its authoritative replicated list (mirrors CharacterManager adding to
    /// networkedCharacters in AddNewCharacter — the Character does not add itself), which avoids the
    /// unguaranteed cross-object OnNetworkSpawn order.
    ///
    /// Position rides <see cref="NetworkTransform"/> (NFR3) — no custom NetworkVariable&lt;Vector3&gt;.
    /// </summary>
    public class PlayerAvatar : NetworkBehaviour
    {
        // Mirrors Character.ownerClientId (Character.cs:18): each avatar carries its identity to clients.
        // Server-authoritative (default NetworkVariable write perm); set once by AvatarManager at spawn.
        public NetworkVariable<ulong> ownerClientId = new(GameValues.FAKE_CLIENT_ID);

        // Data-driven appearance seam (FR8 / DO6). Wired on the prefab; read-only at runtime.
        [SerializeField] private AvatarAppearanceData _defaultAppearance;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyAppearance();
        }

        /// <summary>
        /// Reads the data-driven appearance hook and applies the single shared body material to the
        /// avatar model's renderers. Null-tolerant: a missing SO/material leaves the prefab's default
        /// look untouched (no crash). THIS call site is the stable seam — when per-player customization
        /// lands later, only the SOURCE of the appearance changes, not this method's shape.
        /// </summary>
        private void ApplyAppearance()
        {
            if (_defaultAppearance == null || _defaultAppearance.BodyMaterial == null)
            {
                return;
            }

            // sharedMaterial (not material): assign the shared asset to avoid per-renderer material
            // instantiation/leaks and to match the "single shared model/material" intent.
            foreach (var _renderer in GetComponentsInChildren<Renderer>())
            {
                _renderer.sharedMaterial = _defaultAppearance.BodyMaterial;
            }
        }
    }
}
