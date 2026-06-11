using System;
using Board;
using Characters;
using Characters.Powers;
using Characters.Powers.PowerComponents;
using Characters.Powers.PowerObjects;
using GameLogic;

namespace Tests.Editor
{
    /// <summary>
    /// Story 6.2 / Epic 6 (D0) — the ONE shared curated registry behind both DI-seam guards
    /// (refactor-architecture-despaghetti.md §5). A consumer rerouted off the Service Locator is
    /// appended here ONCE (Epic 7+ stories), and both guards pick it up:
    /// <list type="bullet">
    /// <item><see cref="DiSeamNoLocatorGuardTests"/> (guard #1) — checks the TYPE: no locator
    /// lookup left in the consumer's source.</item>
    /// <item><see cref="SceneWiringGuardTests"/> (guard #2) — checks the BINDING: every injected
    /// [SerializeField] dependency is actually wired in GameScene / a prefab.</item>
    /// </list>
    /// </summary>
    public static class DiSeamMigratedConsumers
    {
        /// <summary>
        /// Consumers proven to no longer use the locator
        /// (<c>GameManager.instance</c> / <c>CharacterManager.instance</c>).
        /// </summary>
        public static readonly Type[] All =
        {
            typeof(LightManager),
            typeof(PTruthChains), // 6.3 lane C worked example — resolves via CompositionRoot in OnNetworkSpawn.
            // Story 7.1 — powers migrated off the GameManager/CharacterManager locator onto the
            // Power.characterManager base field (lane C, resolved in Power.OnNetworkSpawn). These are
            // prefab-present, so both guards cover them. Mixed powers that still hold a GameManager.For
            // hop for OTHER members (gameInfoRevealer) are deferred to 7.3 (they would trip guard #1).
            typeof(Power),
            typeof(PAutoCorruption),
            typeof(PClandestineObservation),
            typeof(PEyeOfTheVoid),
            typeof(PInfiniteMessage),
            typeof(PLegacy),
            typeof(PReincarnation),
            typeof(PVisionOfTheImpossible),
            // Story 7.2 — non-power locator consumers migrated onto an injected CharacterManager:
            // Character (lane C, OnNetworkSpawn) + BoardManager (lane A, scene-wired [SerializeField]).
            typeof(Character),
            typeof(BoardManager),
        };

        /// <summary>
        /// Story 7.1 — clean migrated types that guard #1 source-scans but guard #2 must NOT (they
        /// have no scene/prefab instance of their own: a plain C# class, or lane-C
        /// <see cref="PowerComponent"/>s whose prefab presence is not guaranteed). They carry no
        /// serialized injected field, so guard #2 would add no coverage anyway — it would only
        /// false-fail on the "instance not found" staleness assert. Guard #1 reads
        /// <see cref="All"/> ∪ this; guard #2 reads <see cref="All"/> ∪ <see cref="SceneWiredOnly"/>.
        /// </summary>
        public static readonly Type[] NoLocatorOnly =
        {
            typeof(PowerComponent),
            typeof(PCPowerUnlockWhenChain),
            typeof(PCReparentOnChain),
            typeof(PersonalBeaconObject),
            // Story 7.2 note: Card was migrated onto a lane-B CharacterManager (pushed by
            // BoardManager.AddNewCard) but still holds a GameManager.instance.gameInfoRevealer hop,
            // so it stays OFF the registry until 7.3 (guard #1 cannot tell the two hops apart) —
            // mirrors the mixed powers of 7.1.
        };

        /// <summary>
        /// Manager types delivered through injection. A serialized field counts as an INJECTED
        /// dependency (and gets wiring-checked by guard #2) iff its field type is, or derives
        /// from, a member of this set — presentation refs (Light, Color, …) stay out
        /// automatically, zero per-field maintenance. Grows as the track injects more manager
        /// types (CharacterManager, GameInfoRevealer, …).
        /// </summary>
        public static readonly Type[] InjectedManagerTypes =
        {
            typeof(GameManager),
            typeof(CharacterManager), // 6.3 — CompositionRoot.characterManager wiring is guard-checked.
        };

        /// <summary>
        /// Scene-wired types that are NOT no-locator consumers and so must stay OUT of
        /// <see cref="All"/> (guard #1 would flag them). The <see cref="CompositionRoot"/> is the
        /// one surviving static — it legitimately calls <c>GameManager.For(</c> /
        /// <c>CharacterManager.For(</c> — but its own lane A <c>[SerializeField]</c> manager refs
        /// must still be wired, so SceneWiringGuard (guard #2) covers it via this set (story 6.3
        /// AC4 caution). Guard #1 never reads this list.
        /// </summary>
        public static readonly Type[] SceneWiredOnly =
        {
            typeof(CompositionRoot),
        };
    }
}
