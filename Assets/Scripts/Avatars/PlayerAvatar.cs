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

        // Story 13.2: the eye/look pivot (a child at head height). Body yaw lives on the avatar root
        // (networked via NetworkTransform); local view pitch is applied to this pivot by the owner's
        // AvatarMovementController, and the first-person camera copies this pivot's world pose.
        [SerializeField] private Transform _eyePivot;
        public Transform EyePivot => _eyePivot;

        // Seated-ring gaze: the owner's seated head look, RELATIVE to seat facing (deg) — yaw (left/right) +
        // pitch (up/down). Owner-writable so each player publishes WHERE they look during the embodied Vote;
        // every client renders it on the avatar's HEAD (EyePivot) on top of the LOCALLY-computed seat facing.
        // Because the per-client ring rotation is a rigid isometry and the look is relative (rotates with the
        // frame), "A looks at B" stays consistent on every client. Presentation only (NFR3) — no game state;
        // fills the gap where the seated body was static during the Vote (movement locked, only the local
        // camera turned). Owner-write is a deliberate, human-ratified exception to the "no owner-write" rule
        // (cosmetic head direction only — no authority, faking it only mis-points your own head).
        // GENERALIZED (spec rigged-head-look): this channel now ALSO carries the FREE-ROAM head-look (head
        // yaw offset relative to body + pitch), published by AvatarMovementController. Modes are
        // arbiter-exclusive (only EmbodiedCamera OR MovementController publishes at a time) so there is never
        // a second writer. AvatarHeadLook reads these on every client to aim the rigged HeadBone.
        public NetworkVariable<float> SeatedYaw = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> SeatedPitch = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Owner-only publish of the seated head look (yaw + pitch, relative to seat facing). No-op on non-owners.</summary>
        public void PublishSeatedLook(float _yawDeg, float _pitchDeg)
        {
            if (IsOwner)
            {
                SeatedYaw.Value = _yawDeg;
                SeatedPitch.Value = _pitchDeg;
            }
        }

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
