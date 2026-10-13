using System;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// The local player's cat, kept in the PC's preferences (shared by the Relay and Steam builds). The menu's
    /// customization screen writes it; the player's avatar sends it to the host when it spawns.
    /// </summary>
    public static class CatLookStore
    {
        private const string Key = "cdp.catLook";

        /// <summary>Raised after <see cref="Save"/> (the menu's cat follows the screen).</summary>
        public static event Action<CatLook> Changed;

        // Autoplay: several game processes of one PC share its preferences; each one dresses its cat from its own
        // command line instead, kept in memory only.
        private static CatLook? _sessionLook;

        public static CatLook Load()
        {
            if (_sessionLook.HasValue) return _sessionLook.Value;
            return CatLook.TryDecode(PlayerPrefs.GetString(Key, string.Empty), out CatLook look) ? look : CatLook.Default;
        }

        public static void Save(CatLook look)
        {
            if (_sessionLook.HasValue)
            {
                _sessionLook = look;
            }
            else
            {
                PlayerPrefs.SetString(Key, look.Encode());
                PlayerPrefs.Save();
            }
            Changed?.Invoke(look);
        }

        /// <summary>This process wears <paramref name="look"/> without touching the PC's preferences (autoplay).</summary>
        public static void UseForThisSession(CatLook look)
        {
            _sessionLook = look;
            Changed?.Invoke(look);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            _sessionLook = null;
        }
    }
}
