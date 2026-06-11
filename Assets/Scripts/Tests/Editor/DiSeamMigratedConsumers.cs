using System;
using Characters;
using Characters.Powers;
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
