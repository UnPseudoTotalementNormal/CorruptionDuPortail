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
    /// • <see cref="Embodied"/>  — the Vote (Story 13.4): the seated
    ///   <see cref="AvatarEmbodiedCamera"/> is active (placed at the local seat, clamped
    ///   look), the local body is snapped to its seat, movement is locked, and board
    ///   arrow-nav is cut — see <see cref="AvatarCameraArbiter"/>.
    /// </summary>
    public enum CameraMode
    {
        FreeRoam,
        Board,
        Embodied,
    }
}
