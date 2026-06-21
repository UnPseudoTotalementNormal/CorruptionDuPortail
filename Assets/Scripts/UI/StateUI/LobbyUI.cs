// Quick-dev lobby-menu-on-tablet (2026-06-20): the Start-Game logic moved to the host-agnostic
// LobbyStartButton (resolves LobbyState via CompositionRoot, modular contract). LobbyUI stays the lobby
// HUD's StateUI host (show/hide CanvasGroup lifecycle on lobby start/end) with no logic of its own.
public class LobbyUI : StateUI
{
}
