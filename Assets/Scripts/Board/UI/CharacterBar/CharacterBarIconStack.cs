#region

using System.Collections.Generic;
using System.Threading;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain.PlayerIcons;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.UI.CharacterBar
{
    /// <summary>
    /// Thin display adapter for the PRIVATE power icons stacked on ONE CharactersBar thumbnail. Lives as a
    /// child of the thumbnail's <c>hoverVisual</c> (precedent: <see cref="CharacterAwakenTimer"/>) so it
    /// rides the existing hover transform instead of animating against it.
    ///
    /// All the placement maths is the pure <see cref="IconStackLayout"/> POCO — this class only turns
    /// <see cref="LayoutPoint"/>s into anchored positions and tweens between the collapsed and expanded
    /// arrangement, reusing the thumbnail's OWN hover events, duration and easing so there is never a
    /// second competing animation.
    ///
    /// Nothing here is Canvas-specific beyond the Image renderer: offsets are local units, so when the
    /// thumbnails become 3D objects only the renderer swaps out.
    /// </summary>
    public class CharacterBarIconStack : MonoBehaviour
    {
        [Tooltip("Inactive template Image cloned once per visible icon. Must be a child of this object.")]
        [SerializeField] private Image iconTemplate;

        [Tooltip("Optional '+X' badge shown when more icons are marked than fit in the collapsed pile. " +
                 "Hidden while expanded and when there is no overflow.")]
        [SerializeField] private TextMeshProUGUI overflowLabel;

        [Tooltip("Local X step between icons in the COLLAPSED pile (tight overlap).")]
        [SerializeField] private float collapsedStep = 8f;

        [Tooltip("Local X step between icons when the thumbnail is HOVERED (the fanned-out reading state).")]
        [SerializeField] private float expandedStep = 34f;

        [Tooltip("How many icons the collapsed pile shows before the '+X' badge takes over.")]
        [SerializeField] private int maxVisible = 3;

        // Pure placement. Stateless, so one instance per view costs nothing.
        private readonly IconStackLayout _layout = new IconStackLayout();

        private readonly List<Image> _spawnedIcons = new();
        private readonly List<Sprite> _renderableSprites = new();

        private CharactersBarObject _barObject;
        private PlayerIconManager _iconManager;
        private LocalBarIconRegistry _localRegistry;
        private Character _observedCharacter;
        private bool _expanded;
        private bool _subscribedToManager;
        private bool _subscribedToLocalRegistry;

        // Generation stamp, NOT a bool. Enable/disable cycles can leave a previous frame-loop mid-await, and
        // a flag it already passed cannot stop it — two loops would then resolve and subscribe twice. Every
        // OnEnable/OnDisable bumps the stamp, and a loop whose stamp is stale returns on its next tick.
        private int _loopGeneration;

        // Single source of truth: the fan-out rides the thumbnail's OWN hover motion, so it must read that
        // motion's duration rather than keep a copy that can silently drift.
        private float ExpandTweenDuration => _barObject != null ? _barObject.HoverTweenDuration : 0.35f;

        private void Awake()
        {
            _barObject = GetComponentInParent<CharactersBarObject>();
            if (iconTemplate != null)
            {
                iconTemplate.gameObject.SetActive(false);
                // The stack is decoration on top of the thumbnail: it must never steal the hover that
                // drives its own expansion (nor the click that opens the RoleCard).
                iconTemplate.raycastTarget = false;
            }
            if (overflowLabel != null)
            {
                overflowLabel.raycastTarget = false;
                overflowLabel.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (_barObject != null)
            {
                _barObject.onCharacterBarObjectHovered += OnHovered;
                _barObject.onCharacterBarObjectUnhovered += OnUnhovered;
            }
            _loopGeneration++;
            WatchAsync(_loopGeneration, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void OnDisable()
        {
            // Bump FIRST so the in-flight loop is already stale when it next resumes.
            _loopGeneration++;

            if (_barObject != null)
            {
                _barObject.onCharacterBarObjectHovered -= OnHovered;
                _barObject.onCharacterBarObjectUnhovered -= OnUnhovered;
            }
            UnsubscribeFromManager();
            UnsubscribeFromLocalRegistry();
            KillIconTweens();
            _observedCharacter = null;
            _expanded = false;
        }

        private void OnDestroy()
        {
            // Cloned icons outlive nothing, but a live tween holding a destroyed RectTransform does.
            KillIconTweens();
        }

        private void UnsubscribeFromManager()
        {
            if (_iconManager != null && _subscribedToManager)
            {
                _iconManager.onLocalIconsChanged -= Rebuild;
            }
            _subscribedToManager = false;
            _iconManager = null;
        }

        private void UnsubscribeFromLocalRegistry()
        {
            if (_localRegistry != null && _subscribedToLocalRegistry)
            {
                _localRegistry.onLocalIconsChanged -= Rebuild;
            }
            _subscribedToLocalRegistry = false;
            _localRegistry = null;
        }

        /// <summary>
        /// ONE long-lived loop per enable cycle. It does two jobs that both have to survive an ordering race:
        /// the manager is a spawned NetworkObject that may not be registered yet, and — the load-bearing part —
        /// CharactersBar.ResetCharactersBar INSTANTIATES the thumbnail and only THEN calls SetCharacter, so the
        /// first rebuild would otherwise run against a null character with nothing to re-trigger it. The rebuild
        /// is therefore driven by the character being assigned, never by OnEnable alone.
        /// </summary>
        private async UniTaskVoid WatchAsync(int _generation, CancellationToken _token)
        {
            while (_generation == _loopGeneration)
            {
                if (this == null || _barObject == null)
                {
                    return;
                }

                if (_iconManager == null)
                {
                    TryResolveManager();
                }

                if (_localRegistry == null)
                {
                    TryResolveLocalRegistry();
                }

                Character _current = _barObject.playerCharacter;
                if (!ReferenceEquals(_current, _observedCharacter))
                {
                    _observedCharacter = _current;
                    Rebuild();
                }

                bool _cancelled = await UniTask.NextFrame(_token).SuppressCancellationThrow();
                if (_cancelled)
                {
                    return;
                }
            }
        }

        private void TryResolveManager()
        {
            NetworkManager _networkManager = ResolveNetworkManager();
            if (_networkManager == null)
            {
                return;
            }

            PlayerIconManager _manager = PlayerIconManager.For(_networkManager);
            if (_manager == null)
            {
                // A bare harness (or a bar shown outside a live session) legitimately has no manager; the
                // loop simply keeps looking. Nothing to log.
                return;
            }

            _iconManager = _manager;
            if (!_subscribedToManager)
            {
                _iconManager.onLocalIconsChanged += Rebuild;
                _subscribedToManager = true;
            }
            Rebuild();
        }

        /// <summary>
        /// Resolves the CLIENT-side local channel (<see cref="LocalBarIconRegistry"/>) for this thumbnail's
        /// NetworkManager and subscribes its change event. Unlike the server manager the registry is born on
        /// first <c>For(nm)</c>, so once the character (and thus the NetworkManager) is known this never fails.
        /// </summary>
        private void TryResolveLocalRegistry()
        {
            NetworkManager _networkManager = ResolveNetworkManager();
            if (_networkManager == null)
            {
                return;
            }

            LocalBarIconRegistry _registry = LocalBarIconRegistry.For(_networkManager);
            if (_registry == null)
            {
                return;
            }

            _localRegistry = _registry;
            if (!_subscribedToLocalRegistry)
            {
                _localRegistry.onLocalIconsChanged += Rebuild;
                _subscribedToLocalRegistry = true;
            }
            Rebuild();
        }

        /// <summary>
        /// NEVER <c>NetworkManager.Singleton</c>: this view is a plain MonoBehaviour with no
        /// <c>base.NetworkManager</c>, and with two in-process NetworkManagers the Singleton is the HOST's —
        /// a client view would then read the host's manager and show the host's icons. The thumbnail's
        /// character is a spawned NetworkObject, so it names its own NetworkManager unambiguously.
        /// </summary>
        private NetworkManager ResolveNetworkManager()
        {
            Character _character = _barObject != null ? _barObject.playerCharacter : null;
            if (_character == null)
            {
                return null;
            }
            // TryGetComponent rather than NetworkBehaviour.NetworkObject: the latter logs its own error
            // when the object is not spawned yet, and "not spawned yet" is a normal frame here.
            return _character.TryGetComponent(out NetworkObject _networkObject) ? _networkObject.NetworkManager : null;
        }

        private void OnHovered(Character _character) => SetExpanded(true);
        private void OnUnhovered(Character _character) => SetExpanded(false);

        private void SetExpanded(bool _value)
        {
            if (_expanded == _value)
            {
                return;
            }
            _expanded = _value;
            // Expanding reveals the overflowed icons, so the set of shown icons changes, not just their
            // positions — rebuild, then tween each one to its new place.
            Rebuild();
        }

        /// <summary>Recreates the icon set for the current marker slice and animates it into place.</summary>
        private void Rebuild()
        {
            if (this == null || _barObject == null || iconTemplate == null)
            {
                return;
            }

            // Only icons that can ACTUALLY RENDER take part. A power with no barIcon draws nothing — a
            // normal case, never an error — and must not occupy a slot nor inflate the "+X" counter with
            // icons the player could never see however far the stack is fanned out.
            ResolveRenderableSprites();
            int _count = _renderableSprites.Count;

            int _visible = _layout.VisibleCount(_count, _expanded, maxVisible);
            int _overflow = _layout.OverflowCount(_count, _expanded, maxVisible);
            IReadOnlyList<LayoutPoint> _points =
                _layout.Compute(_count, _expanded, collapsedStep, expandedStep, maxVisible);

            EnsureIconCount(_visible);

            for (int _i = 0; _i < _visible; _i++)
            {
                Image _icon = _spawnedIcons[_i];
                _icon.sprite = _renderableSprites[_i];
                _icon.gameObject.SetActive(true);

                var _rect = (RectTransform)_icon.transform;
                var _destination = new Vector2(_points[_i].X, _points[_i].Y);
                // DOKill before re-tweening so a hover-exit mid-expansion folds back in the same easing
                // instead of fighting a live tween.
                _rect.DOKill();
                _rect.DOAnchorPos(_destination, ExpandTweenDuration).SetEase(Ease.OutQuint);
            }

            FoldAwaySurplusIcons(_visible, _points);

            if (overflowLabel != null)
            {
                bool _showOverflow = _overflow > 0;
                overflowLabel.gameObject.SetActive(_showOverflow);
                if (_showOverflow)
                {
                    overflowLabel.text = $"+{_overflow}";
                }
            }
        }

        /// <summary>
        /// Collapsing REMOVES icons from the visible set. They travel back into the pile in the same motion
        /// and only then disappear — snapping them off mid-hover would read as a glitch, not as a fold.
        /// </summary>
        private void FoldAwaySurplusIcons(int _visible, IReadOnlyList<LayoutPoint> _points)
        {
            Vector2 _foldTarget = _visible > 0 && _points.Count >= _visible
                ? new Vector2(_points[_visible - 1].X, _points[_visible - 1].Y)
                : Vector2.zero;

            for (int _i = _visible; _i < _spawnedIcons.Count; _i++)
            {
                Image _icon = _spawnedIcons[_i];
                if (_icon == null)
                {
                    continue;
                }

                var _rect = (RectTransform)_icon.transform;
                _rect.DOKill();
                if (!_icon.gameObject.activeSelf)
                {
                    continue;
                }

                _rect.DOAnchorPos(_foldTarget, ExpandTweenDuration)
                    .SetEase(Ease.OutQuint)
                    .OnComplete(() =>
                    {
                        if (_icon != null)
                        {
                            _icon.gameObject.SetActive(false);
                        }
                    });
            }
        }

        // This thumbnail's marked-player id, then the icons THIS peer is allowed to see on it, reduced to the
        // ones that resolve to a real sprite. A peer only ever holds its own slice, so there is nothing to
        // filter for privacy here.
        private void ResolveRenderableSprites()
        {
            _renderableSprites.Clear();
            if (_barObject.playerCharacter == null)
            {
                return;
            }

            ulong _markedClientId = _barObject.playerCharacter.ownerClientId.Value;

            // CLIENT channel FIRST: locally-derived icons (e.g. corruption knowledge) draw ahead of the
            // server power markers, matching the intended [corrupt][power...] order.
            if (_localRegistry != null)
            {
                foreach (Sprite _sprite in _localRegistry.GetIconsFor(_markedClientId))
                {
                    if (_sprite != null)
                    {
                        _renderableSprites.Add(_sprite);
                    }
                }
            }

            // SERVER slice: the private power markers this peer is allowed to see on this thumbnail.
            if (_iconManager != null)
            {
                List<ulong> _iconIds = _iconManager.GetLocalIconsFor(_markedClientId);
                foreach (ulong _iconId in _iconIds)
                {
                    Sprite _sprite = ResolveSprite(_iconId);
                    if (_sprite != null)
                    {
                        _renderableSprites.Add(_sprite);
                    }
                }
            }
        }

        // An icon id is the declaring Power's NetworkObjectId; the sprite is that power's BarIcon. A
        // despawned power or a power with no sprite yields null and the icon is simply not shown.
        private Sprite ResolveSprite(ulong _iconId)
        {
            NetworkManager _networkManager = ResolveNetworkManager();
            if (_networkManager == null || _networkManager.SpawnManager == null)
            {
                return null;
            }
            if (!_networkManager.SpawnManager.SpawnedObjects.TryGetValue(_iconId, out NetworkObject _object) || _object == null)
            {
                return null;
            }
            Power _power = _object.GetComponent<Power>();
            return _power != null ? _power.BarIcon : null;
        }

        // Grows only — shrinking is the animated fold in FoldAwaySurplusIcons.
        private void EnsureIconCount(int _count)
        {
            while (_spawnedIcons.Count < _count)
            {
                Image _clone = Instantiate(iconTemplate, iconTemplate.transform.parent);
                _clone.raycastTarget = false;
                _clone.gameObject.SetActive(true);
                _spawnedIcons.Add(_clone);
            }
        }

        private void KillIconTweens()
        {
            foreach (Image _icon in _spawnedIcons)
            {
                if (_icon != null)
                {
                    _icon.transform.DOKill();
                }
            }
        }
    }
}
