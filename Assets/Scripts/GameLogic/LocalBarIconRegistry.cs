using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// CLIENT-ONLY companion to <see cref="PlayerIconManager"/>: a per-NetworkManager channel of
    /// locally-derived CharactersBar icons that NEVER touch the wire. Where PlayerIconManager holds
    /// server-authoritative private markers pushed as per-viewer slices, this registry holds icons a
    /// peer computes for ITSELF from data it already replicates. The first consumer is corruption
    /// knowledge (forceCorruptOnRoleRevealed + isCorrupted); <see cref="Board.UI.CharacterBar.CharacterBarIconStack"/>
    /// merges the two sources, drawing the local channel first.
    ///
    /// Keyed markedClientId -> ordered (key -> sprite): the string key lets independent client systems
    /// own their own icon on the same thumbnail without colliding — <see cref="SetIcon"/> replaces by key.
    ///
    /// BORN CLEAN (mirror <see cref="Avatars.AvatarManager"/> / PlayerIconManager): NO static instance —
    /// resolution is <see cref="For"/>(nm)-only, so the StaticSingletonCensusGuard needs no whitelist entry
    /// and two in-process NetworkManagers keep separate channels (no host/client leak in the 2-NM fixture).
    /// </summary>
    public sealed class LocalBarIconRegistry
    {
        // One keyed sprite on a thumbnail. Held in an ordered list (not a Dictionary) so the stack draws
        // icons in a stable registration order.
        private readonly struct KeyedSprite
        {
            public readonly string Key;
            public readonly Sprite Sprite;

            public KeyedSprite(string _key, Sprite _sprite)
            {
                Key = _key;
                Sprite = _sprite;
            }
        }

        // Per-NetworkManager registry (mirror AvatarManager.s_byNetworkManager). No `instance` facade.
        private static readonly Dictionary<NetworkManager, LocalBarIconRegistry> s_byNetworkManager = new();

        /// <summary>
        /// Resolves — creating it on first use — the registry owned by the given NetworkManager
        /// (null only when <paramref name="_networkManager"/> is null). Unlike the NetworkBehaviour
        /// managers this has no spawn hook, so it is born lazily here rather than self-registered.
        /// </summary>
        public static LocalBarIconRegistry For(NetworkManager _networkManager)
        {
            if (_networkManager == null)
            {
                return null;
            }
            if (!s_byNetworkManager.TryGetValue(_networkManager, out var _registry) || _registry == null)
            {
                _registry = new LocalBarIconRegistry();
                s_byNetworkManager[_networkManager] = _registry;
            }
            return _registry;
        }

#if UNITY_EDITOR
        // Domain reload is disabled — statics survive Play sessions (project-context.md Domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
        }
#endif

        private readonly Dictionary<ulong, List<KeyedSprite>> _byMarked = new();

        /// <summary>Raised whenever any icon changes — the icon-stack views rebuild on it.</summary>
        public event Action onLocalIconsChanged;

        /// <summary>
        /// Sets (or replaces) the icon the client system named <paramref name="_key"/> shows on
        /// <paramref name="_markedClientId"/>'s thumbnail. A null sprite clears it (same as
        /// <see cref="ClearIcon"/>). A no-op when nothing actually changes — raises nothing.
        /// </summary>
        public void SetIcon(ulong _markedClientId, string _key, Sprite _sprite)
        {
            if (string.IsNullOrEmpty(_key))
            {
                return;
            }
            if (_sprite == null)
            {
                ClearIcon(_markedClientId, _key);
                return;
            }

            if (!_byMarked.TryGetValue(_markedClientId, out var _icons))
            {
                _icons = new List<KeyedSprite>();
                _byMarked[_markedClientId] = _icons;
            }

            for (int _i = 0; _i < _icons.Count; _i++)
            {
                if (_icons[_i].Key == _key)
                {
                    if (_icons[_i].Sprite == _sprite)
                    {
                        return; // unchanged — no event
                    }
                    _icons[_i] = new KeyedSprite(_key, _sprite);
                    onLocalIconsChanged?.Invoke();
                    return;
                }
            }

            _icons.Add(new KeyedSprite(_key, _sprite));
            onLocalIconsChanged?.Invoke();
        }

        /// <summary>Removes the <paramref name="_key"/> icon from the thumbnail. Unknown key/player = silent no-op.</summary>
        public void ClearIcon(ulong _markedClientId, string _key)
        {
            if (string.IsNullOrEmpty(_key) || !_byMarked.TryGetValue(_markedClientId, out var _icons))
            {
                return;
            }

            for (int _i = 0; _i < _icons.Count; _i++)
            {
                if (_icons[_i].Key == _key)
                {
                    _icons.RemoveAt(_i);
                    onLocalIconsChanged?.Invoke();
                    return;
                }
            }
        }

        /// <summary>
        /// The sprites on <paramref name="_markedClientId"/>'s thumbnail, in registration order. A fresh
        /// list every call (never a shared buffer) so a caller can hold it without aliasing surprises.
        /// Empty when there are none.
        /// </summary>
        public IReadOnlyList<Sprite> GetIconsFor(ulong _markedClientId)
        {
            var _result = new List<Sprite>();
            if (_byMarked.TryGetValue(_markedClientId, out var _icons))
            {
                foreach (var _keyed in _icons)
                {
                    _result.Add(_keyed.Sprite);
                }
            }
            return _result;
        }
    }
}
