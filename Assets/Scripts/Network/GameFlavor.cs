#region

using CorruptionDuPortail.Domain;

#endregion

namespace Network
{
    /// <summary>
    /// Which online stack this player runs (<see cref="OnlineFlavor"/>). A player build is fixed at compile time:
    /// <see cref="SteamDefine"/> is added by the Steam build (Game.Editor CdpBuild, CI Build.yml). The Editor follows
    /// the "Tools/Corruption du Portail/Build Steam en Play mode" menu toggle, off by default.
    /// </summary>
    public static class GameFlavor
    {
        public const string SteamDefine = "CDP_STEAM";
        public const string EditorPrefKey = "cdp.flavor.steam";

        public static OnlineFlavor Current => IsSteam ? OnlineFlavor.Steam : OnlineFlavor.Relay;

        public static bool IsSteam
        {
            get
            {
#if CDP_STEAM
                return true;
#elif UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#else
                return false;
#endif
            }
        }
    }
}
