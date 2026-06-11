using System;
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
        };
    }
}
