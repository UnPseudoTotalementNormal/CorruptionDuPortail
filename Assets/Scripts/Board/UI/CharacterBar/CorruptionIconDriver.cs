#region

using System.Collections.Generic;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Board.UI.CharacterBar
{
    /// <summary>
    /// CLIENT-side producer of the "corruption" CharactersBar icon — the replacement for the old
    /// full-thumbnail CorruptOverlay. For every roster character this peer is ALLOWED to see corrupted
    /// (forceCorruptOnRoleRevealed granted to the local viewer by PCorruptionKnowledge / PPersonalBeacons)
    /// AND that IS corrupted, it writes a "corruption" icon into the per-NetworkManager
    /// <see cref="LocalBarIconRegistry"/>; <see cref="CharacterBarIconStack"/> renders it beside the server
    /// power icons.
    ///
    /// Deliberately NOT part of the thumbnail view: ONE driver per peer writes the shared per-NM channel, so
    /// every view of a player (main bar + NoteRibbon) reads one consistent source. The icons it wrote are
    /// cleared on <see cref="OnDestroy"/> (real teardown), NOT on <see cref="OnDisable"/> — a transient
    /// disable must not wipe icons other views are still reading. The visibility gate is IDENTICAL to the
    /// overlay it replaces, so PCorruptionKnowledge and PPersonalBeacons keep working with zero decision
    /// changes — only the renderer moved.
    /// </summary>
    public sealed class CorruptionIconDriver : MonoBehaviour
    {
        // The one icon key this driver owns on the local channel.
        private const string CorruptionIconKey = "corruption";

        [Tooltip("Scene-wired roster source — the same CharacterManager instance CharactersBar uses.")]
        [SerializeField] private CharacterManager characterManager;

        [Tooltip("Sprite shown on a KNOWN-corrupted player's thumbnail. Reuses the old CorruptOverlay sprite.")]
        [SerializeField] private Sprite corruptionSprite;

        private ICharacterQuery CharacterQuery => characterManager;

        // Characters we currently watch, so isCorrupted is subscribed/unsubscribed exactly once each.
        private readonly List<Character> _watched = new();
        // Marked ids we have set an icon for, so ones that leave the roster are cleared.
        private readonly HashSet<ulong> _markedIds = new();
        private readonly List<ulong> _staleBuffer = new();

        private GameInfoRevealer _revealer;
        private bool _subscribedToRevealer;

        // Last NetworkManager we resolved. Cached because at teardown (scene unload / despawn) the inherited
        // characterManager.NetworkManager can already be null — the same reason CharactersBarObject.OnDestroy
        // null-guards it — and we still need the right channel to clear our icons from.
        private NetworkManager _resolvedNetworkManager;

        // Generation stamp for the dependency-resolution loop (mirror CharacterBarIconStack): each
        // OnEnable/OnDisable bumps it so a loop left mid-await returns on its next tick instead of resolving
        // twice.
        private int _loopGeneration;

        private void OnEnable()
        {
            if (characterManager != null)
            {
                CharacterQuery.onCharactersListUpdated += OnCharactersListUpdated;
                // triggerUpdate:false — a passive reader must never re-raise onCharactersListUpdated, or it
                // would drive a self-sustaining per-frame loop through its own subscription.
                RebindWatched(CharacterQuery.GetCharacters(false));
            }

            // The revealer is an NGO-spawn-order dependency: resolve it in a one-shot loop rather than only
            // lazily inside RecomputeAll, so a reveal-only knowledge change (onCharacterInfoRevealedChanged
            // with no accompanying roster/isCorrupted event) can never be missed because we hadn't subscribed.
            _loopGeneration++;
            ResolveDependenciesAsync(_loopGeneration, this.GetCancellationTokenOnDestroy()).Forget();

            RecomputeAll();
        }

        private void OnDisable()
        {
            _loopGeneration++;

            if (characterManager != null)
            {
                CharacterQuery.onCharactersListUpdated -= OnCharactersListUpdated;
            }
            UnwatchAll();
            UnsubscribeRevealer();
        }

        private void OnDestroy()
        {
            // Real teardown: remove the icons this driver wrote so a rematch that reuses the same
            // NetworkManager (its channel survives — reset only on domain reload) never inherits stale ones.
            ClearAllIcons();
        }

        private void OnCharactersListUpdated(List<Character> _characters)
        {
            RebindWatched(_characters);
            RecomputeAll();
        }

        // ---- dependency resolution (NGO spawn-order tolerant) -----------------------------------

        private async UniTaskVoid ResolveDependenciesAsync(int _generation, CancellationToken _token)
        {
            while (_generation == _loopGeneration)
            {
                if (this == null || characterManager == null)
                {
                    return;
                }

                NetworkManager _networkManager = characterManager.NetworkManager;
                if (_networkManager != null)
                {
                    _resolvedNetworkManager = _networkManager;

                    if (_subscribedToRevealer)
                    {
                        return;
                    }

                    GameInfoRevealer _resolved = CompositionRoot.For(_networkManager).GameInfoRevealer;
                    if (_resolved != null)
                    {
                        _revealer = _resolved;
                        _revealer.onCharacterInfoRevealedChanged += RecomputeAll;
                        _subscribedToRevealer = true;
                        RecomputeAll();
                        return;
                    }
                }

                bool _cancelled = await UniTask.NextFrame(_token).SuppressCancellationThrow();
                if (_cancelled)
                {
                    return;
                }
            }
        }

        private void UnsubscribeRevealer()
        {
            if (_subscribedToRevealer && _revealer != null)
            {
                _revealer.onCharacterInfoRevealedChanged -= RecomputeAll;
            }
            _revealer = null;
            _subscribedToRevealer = false;
        }

        // ---- per-character isCorrupted watching -------------------------------------------------

        private void RebindWatched(List<Character> _characters)
        {
            UnwatchAll();
            if (_characters == null)
            {
                return;
            }
            foreach (var _character in _characters)
            {
                if (_character == null || _watched.Contains(_character))
                {
                    continue;
                }
                _character.isCorrupted.OnValueChanged += OnCorruptedChanged;
                _watched.Add(_character);
            }
        }

        private void UnwatchAll()
        {
            foreach (var _character in _watched)
            {
                if (_character != null)
                {
                    _character.isCorrupted.OnValueChanged -= OnCorruptedChanged;
                }
            }
            _watched.Clear();
        }

        private void OnCorruptedChanged(bool _previous, bool _current) => RecomputeAll();

        // ---- the gate ---------------------------------------------------------------------------

        private void RecomputeAll()
        {
            if (characterManager == null)
            {
                return;
            }
            NetworkManager _networkManager = characterManager.NetworkManager;
            if (_networkManager == null)
            {
                return;
            }
            _resolvedNetworkManager = _networkManager;

            LocalBarIconRegistry _registry = LocalBarIconRegistry.For(_networkManager);
            if (_registry == null || _revealer == null)
            {
                return;
            }

            // triggerUpdate:false — never re-raise the roster event from a passive read (see OnEnable).
            var _characters = CharacterQuery.GetCharacters(false);
            var _present = new HashSet<ulong>();
            foreach (var _character in _characters)
            {
                if (_character == null)
                {
                    continue;
                }
                ulong _id = _character.ownerClientId.Value;
                _present.Add(_id);

                // Identical gate to the old overlay: the LOCAL viewer's knowledge (default observer) that
                // this player's corruption may be shown, AND the player actually being corrupted now.
                bool _known = _revealer.GetCharacterInfo(_id).forceCorruptOnRoleRevealed > RevealLevel.False;
                bool _visible = _known && _character.isCorrupted.Value;

                if (_visible)
                {
                    _registry.SetIcon(_id, CorruptionIconKey, corruptionSprite);
                    _markedIds.Add(_id);
                }
                else
                {
                    _registry.ClearIcon(_id, CorruptionIconKey);
                    _markedIds.Remove(_id);
                }
            }

            // A player that left the roster keeps no stale corruption icon behind (rematch reuses ids).
            if (_markedIds.Count > 0)
            {
                _staleBuffer.Clear();
                foreach (ulong _id in _markedIds)
                {
                    if (!_present.Contains(_id))
                    {
                        _staleBuffer.Add(_id);
                    }
                }
                foreach (ulong _id in _staleBuffer)
                {
                    _registry.ClearIcon(_id, CorruptionIconKey);
                    _markedIds.Remove(_id);
                }
            }
        }

        private void ClearAllIcons()
        {
            if (_markedIds.Count == 0)
            {
                return;
            }
            // Prefer the cached NetworkManager: the live characterManager.NetworkManager may already be null
            // during teardown, and For(null) would silently fail to clear, orphaning entries in the channel.
            NetworkManager _networkManager = _resolvedNetworkManager != null
                ? _resolvedNetworkManager
                : (characterManager != null ? characterManager.NetworkManager : null);

            LocalBarIconRegistry _registry = LocalBarIconRegistry.For(_networkManager);
            if (_registry != null)
            {
                foreach (ulong _id in _markedIds)
                {
                    _registry.ClearIcon(_id, CorruptionIconKey);
                }
            }
            _markedIds.Clear();
        }
    }
}
