namespace Avatars
{
    /// <summary>
    /// Story 13.3 (Epic 13 — Player Embodiment). The three camera presentations the
    /// <see cref="AvatarCameraArbiter"/> arbitrates between, keyed by game-state TYPE
    /// (see <see cref="AvatarCameraModePolicy"/>):
    ///
    /// • <see cref="FreeRoam"/>  — the Lobby first-person walk-around (Story 13.2's
    ///   <see cref="AvatarFollowCamera"/> is active; movement input is enabled).
    /// • <see cref="Board"/>     — every in-loop fixed state: the existing
    ///   <see cref="Board.BoardCameraSystem.BoardCameraManager"/> presentation is in
    ///   charge (forceBoardCamera + arrow neighbour-nav), exactly as before this epic.
    /// • <see cref="Embodied"/>  — the Vote. In 13.3 this is a ROUTING SLOT only:
    ///   movement is locked and board arrow-nav is cut, but the concrete seated camera
    ///   / seat-snap / clamped look is Story 13.4. Until 13.4 lands, Embodied falls back
    ///   to the unchanged board-camera presentation of <c>VoteState</c> (its current
    ///   forceBoardCamera) — see <see cref="AvatarCameraArbiter"/>.
    /// </summary>
    public enum CameraMode
    {
        FreeRoam,
        Board,
        Embodied,
    }
}
