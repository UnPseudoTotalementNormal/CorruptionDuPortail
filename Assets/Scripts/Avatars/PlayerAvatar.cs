using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using Unity.Netcode;
using Unity.Netcode.Components;
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

        // Emotes: the body's NetworkAnimator (on the Cat_Avatar model child). Emotes are played SERVER-side on
        // it so the momentary trigger + the EmoteId selector replicate to every client (incl. the owner). Wire
        // it on the PlayerAvatar prefab. Null-tolerant — an unwired ref simply makes RequestEmote a no-op.
        [SerializeField] private NetworkAnimator _networkAnimator;

        // Customization: the cat this player picked in the menu (CatLook.Pack: fur, outfit, eyes, mouth). Server-written
        // (default permission): the owner proposes its saved look with SubmitCatLookRpc when its avatar spawns (a
        // rejoin spawns a new avatar, so it is sent again), the server sanitizes it against the catalog. Every replica
        // dresses its cat model from it, late joiners included (full value on spawn).
        public NetworkVariable<uint> CatLookPacked = new(CatLook.Default.Pack());

        // The cat model's dresser (on the Cat_Avatar child). Null-tolerant: an unwired avatar keeps its default cat.
        [SerializeField] private CatAppearance _catAppearance;

        // Animator wiring for emotes: an integer param selects WHICH emote. LOOP emotes ride the "Emoting" bool
        // (held true → the emote clip loops; false → back to Idle); ONE-SHOT emotes fire the momentary "Emote"
        // trigger (play once → exit-time → Idle). Names must match the Cat_Avatar Animator. Non-trigger params
        // (EmoteId, Emoting) are set on the Animator (NetworkAnimator auto-syncs them); the trigger goes through
        // NetworkAnimator.SetTrigger.
        private const string EmoteIdParam = "EmoteId";
        private const string EmoteTriggerParam = "Emote";
        private const string EmotingParam = "Emoting";
        // The artist's situational animations (Cat_Avatar Animator): held bools, set on the server, synced by the
        // NetworkAnimator. Chained: Getting Chained -> Chained Idle (an emote tried while chained plays Chained
        // Struggle). Tablet: Pulls Tablet Out -> Look at Tablet Idle -> Putting Table Away.
        private const string ChainedParam = "Chained";
        private const string TabletParam = "Tablet";

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

        /// <summary>
        /// Owner-only: request playing an emote (by its Animator <c>EmoteId</c>). Routed to the server, which
        /// sets it on the body's NetworkAnimator so every client — including this owner — sees the animation.
        /// <paramref name="_loops"/> chooses the path: LOOP holds the <c>Emoting</c> bool (until
        /// <see cref="StopEmote"/>); ONE-SHOT fires the momentary <c>Emote</c> trigger. No-op on non-owners.
        /// Values come from the wheel's <see cref="EmoteDefinition"/>.
        /// </summary>
        public void RequestEmote(int _emoteId, bool _loops)
        {
            if (!IsOwner)
            {
                return;
            }
            PlayEmoteRpc(_emoteId, _loops);
        }

        /// <summary>
        /// Owner-only: stop a LOOPing emote (clears the <c>Emoting</c> bool on the server so every client
        /// returns to Idle). No-op on non-owners / one-shot emotes (harmless: the bool is already false).
        /// </summary>
        public void StopEmote()
        {
            if (!IsOwner)
            {
                return;
            }
            StopEmoteRpc();
        }

        // Server plays the emote on the NetworkAnimator: set the EmoteId selector (auto-synced param), then EITHER
        // hold the Emoting bool (loop, auto-synced) OR fire the momentary Emote trigger (one-shot, replicated
        // explicitly). Null-tolerant.
        [Rpc(SendTo.Server)]
        private void PlayEmoteRpc(int _emoteId, bool _loops)
        {
            if (_networkAnimator == null || _networkAnimator.Animator == null)
            {
                return;
            }
            _networkAnimator.Animator.SetInteger(EmoteIdParam, _emoteId);
            if (_loops)
            {
                _networkAnimator.Animator.SetBool(EmotingParam, true);
            }
            else
            {
                _networkAnimator.SetTrigger(EmoteTriggerParam);
            }
        }

        /// <summary>Server: this body plays the chained animations while <paramref name="_chained"/> (AvatarManager
        /// follows the seat's character). Null-tolerant.</summary>
        public void SetChainedServer(bool _chained)
        {
            if (!IsServer || _networkAnimator == null || _networkAnimator.Animator == null)
            {
                return;
            }
            if (_networkAnimator.Animator.GetBool(ChainedParam) != _chained)
            {
                _networkAnimator.Animator.SetBool(ChainedParam, _chained);
                Debug.Log($"[CATANIM] seat {ownerClientId.Value} chained={_chained}");
            }
        }

        // T23 (mockups jour-pov v1 V2): the "Accuser" emote of the Cat_Avatar Animator (Accusing, then held in Accused
        // Idle while Emoting stays true), shown by a cat that voted when the vote is not anonymous.
        private const int AccuseEmoteId = 3;
        private bool _accusing;

        /// <summary>Server: this body holds the accusing pose while <paramref name="_accuse"/> (AvatarManager follows
        /// the vote). Ends only the pose it started: an emote the player chose meanwhile is left alone. Null-tolerant.</summary>
        public void SetAccusingServer(bool _accuse)
        {
            if (!IsServer || _accuse == _accusing || _networkAnimator == null || _networkAnimator.Animator == null)
            {
                return;
            }
            _accusing = _accuse;
            Animator _animator = _networkAnimator.Animator;
            if (_accuse)
            {
                _animator.SetInteger(EmoteIdParam, AccuseEmoteId);
                _animator.SetBool(EmotingParam, true);
            }
            else if (_animator.GetInteger(EmoteIdParam) == AccuseEmoteId)
            {
                _animator.SetBool(EmotingParam, false);
            }
            Debug.Log($"[CATANIM] seat {ownerClientId.Value} accusing={_accuse}");
        }

        /// <summary>Owner-only: the player holds their tablet (true) or put it away (false); every peer sees the
        /// cat take it out, look at it, put it away. No-op on non-owners.</summary>
        public void SetTabletOut(bool _out)
        {
            if (IsOwner)
            {
                SetTabletRpc(_out);
            }
        }

        [Rpc(SendTo.Server)]
        private void SetTabletRpc(bool _out, RpcParams _rpcParams = default)
        {
            if (_rpcParams.Receive.SenderClientId != OwnerClientId || _networkAnimator == null || _networkAnimator.Animator == null)
            {
                return;
            }
            _networkAnimator.Animator.SetBool(TabletParam, _out);
            Debug.Log($"[CATANIM] seat {ownerClientId.Value} tablet={_out}");
        }

        // Server stops a looping emote by clearing the auto-synced Emoting bool. Null-tolerant.
        [Rpc(SendTo.Server)]
        private void StopEmoteRpc()
        {
            if (_networkAnimator == null || _networkAnimator.Animator == null)
            {
                return;
            }
            _networkAnimator.Animator.SetBool(EmotingParam, false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyAppearance();

            CatLookPacked.OnValueChanged += OnCatLookChanged;
            ApplyCatLook(CatLookPacked.Value);
            SubmitLook(CatLookStore.Load());
        }

        /// <summary>Owner-only: proposes <paramref name="_look"/> to the server (sent at spawn with the saved look).
        /// No-op on non-owners.</summary>
        public void SubmitLook(CatLook _look)
        {
            if (IsOwner)
            {
                SubmitCatLookRpc(_look.Pack());
            }
        }

        public override void OnNetworkDespawn()
        {
            CatLookPacked.OnValueChanged -= OnCatLookChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>The cat this avatar wears (as replicated).</summary>
        public CatLook CatLook => CatLook.Unpack(CatLookPacked.Value);

        private void OnCatLookChanged(uint _previous, uint _current) => ApplyCatLook(_current);

        private void ApplyCatLook(uint _packed)
        {
            if (_catAppearance != null)
            {
                _catAppearance.Apply(CatLook.Unpack(_packed));
            }
        }

        // Owner -> server: the look saved on the owner's PC. Only the avatar's owner may dress it; an unknown id (newer
        // version, forged value) falls back to the catalog default, so the server never relays something undrawable.
        [Rpc(SendTo.Server)]
        private void SubmitCatLookRpc(uint _packed, RpcParams _rpcParams = default)
        {
            if (_rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }
            CatLook _look = CatLook.Unpack(_packed);
            if (_catAppearance != null && _catAppearance.Catalog != null)
            {
                _look = _catAppearance.Catalog.Sanitize(_look);
            }
            CatLookPacked.Value = _look.Pack();
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
