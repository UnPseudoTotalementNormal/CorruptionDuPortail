
public static class GameValues
{
    public const int MAX_PLAYERS = 15;
    // Steam build: Spacewar (480), Valve's shared test app, until the game gets its own app id on Steamworks.
    public const uint STEAM_APP_ID = 480;
    public const ulong FAKE_CLIENT_ID = ulong.MaxValue;
    // Rejoin: how long a real player's seat stays reserved after a mid-game disconnect before the leave rule
    // (instant chain) applies. Owner decision 2026-10-05 (feat/player-rejoin).
    public const double REJOIN_GRACE_SECONDS = 120.0;

    public const ulong CHAT_SERVER_CLIENT_ID = FAKE_CLIENT_ID;
}