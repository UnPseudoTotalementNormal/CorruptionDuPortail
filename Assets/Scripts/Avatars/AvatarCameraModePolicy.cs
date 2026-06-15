using GameLogic;
using GameLogic.GameStates;

namespace Avatars
{
    /// <summary>
    /// Story 13.3 (Epic 13 — Player Embodiment). The PURE state → camera-mode mapping
    /// (FR5). Single decision point so a future state reorder/rename has exactly one
    /// place to break — and so the mapping is EditMode-testable with zero network setup
    /// (<c>AvatarCameraModePolicyTests</c> instantiates states via
    /// <c>ScriptableObject.CreateInstance&lt;T&gt;()</c> and asserts each row).
    ///
    /// TYPE-KEYED, not index-keyed, ON PURPOSE: matching on the C# state TYPE
    /// (<c>is LobbyState</c> / <c>is VoteState</c>) means a state REORDER in the
    /// GameManager dictionary is a no-op here, and a state RENAME breaks COMPILATION
    /// rather than silently dropping a player into the wrong camera. Indexing on the
    /// dictionary position would do neither.
    ///
    /// Lives in the <b>Game</b> asmdef, NOT Domain: it references <see cref="GameState"/>,
    /// an engine <c>ScriptableObject</c> type forbidden by Domain's <c>noEngineReferences</c>
    /// purity guard (same reason <see cref="IGameStateQuery"/> lives in Game, not Domain).
    ///
    /// The <see cref="CameraMode.Embodied"/> result only ROUTES the Vote to the embodied
    /// slot + locks movement; the concrete seat/camera realization is Story 13.4 (13.3
    /// leaves the arbiter untouched when 13.4 fills it in).
    /// </summary>
    public static class AvatarCameraModePolicy
    {
        /// <summary>
        /// Resolves the camera mode for a game state. <c>LobbyState → FreeRoam</c>,
        /// <c>VoteState → Embodied</c>; <c>null</c> AND every other state type → <c>Board</c>
        /// (the safe default that preserves the untouched in-loop presentation, NFR1).
        /// </summary>
        public static CameraMode ResolveMode(GameState _state)
        {
            return _state switch
            {
                LobbyState => CameraMode.FreeRoam,
                VoteState => CameraMode.Embodied,
                // null and every other in-loop state keep the existing board presentation.
                _ => CameraMode.Board,
            };
        }
    }
}
