namespace Avatars
{
    /// <summary>
    /// Pure presentation policy for the player <see cref="AvatarNameplate"/> — no Unity types, no state, so it
    /// is trivially unit-testable (EditMode). Encodes the two design rules (Poyo, 2026-07-13): a nameplate is
    /// shown for OTHER players only, and ONLY while the local camera is first-person.
    /// </summary>
    public static class NameplatePolicy
    {
        /// <summary>
        /// Whether a given avatar's nameplate should be visible right now.
        /// <paramref name="isLocalAvatar"/> = this is the local player's OWN avatar (never show your own —
        /// you don't see your own head in first-person). <paramref name="isFirstPerson"/> = the local camera
        /// is in a first-person mode (FreeRoam walk-around or the seated Embodied vote with the first-person
        /// node live). Both conditions gate the plate: show ⇔ not-mine AND first-person.
        /// </summary>
        public static bool ShouldShow(bool isLocalAvatar, bool isFirstPerson) => !isLocalAvatar && isFirstPerson;

        /// <summary>
        /// Normalize a resolved pseudo for display: trims surrounding whitespace; a null/blank pseudo (name not
        /// resolved yet, or an empty <c>PlayerInfo</c>) collapses to an empty string so the plate shows nothing
        /// rather than stray whitespace.
        /// </summary>
        public static string ResolveLabel(string pseudo) =>
            string.IsNullOrWhiteSpace(pseudo) ? string.Empty : pseudo.Trim();
    }
}
