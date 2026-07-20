#region

using System.Collections.Generic;
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
    /// arrangement, reusing the thumbnail's OWN hover events, duration and easing (0.35s / Ease.OutQuint)
    /// so there is never a second competing animation.
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

        [Tooltip("Must match CharactersBarObject.hoverTweenDuration — the fan-out rides the same hover motion.")]
        [SerializeField] private float expandTweenDuration = 0.35f;

        // Pure placement. Stateless, so one instance per view costs nothing.
        private readonly IconStackLayout _layout = new IconStackLayout();

        private readonly List<Image> _spawnedIcons = new();

        private CharactersBarObject _barObject;
        private PlayerIconManager _iconManager;
        private bool _expanded;
        private bool _subscribedToManager;

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
            // The bar is rebuilt wholesale by CharactersBar.ResetCharactersBar, which re-instantiates this
            // component — so re-resolving and rebuilding here IS the re-application after a rebuild.
            ResolveManagerAndRebuild().Forget();
        }

        private void OnDisable()
        {
            if (_barObject != null)
            {
                _barObject.onCharacterBarObjectHovered -= OnHovered;
                _barObject.onCharacterBarObjectUnhovered -= OnUnhovered;
            }
            if (_iconManager != null && _subscribedToManager)
            {
                _iconManager.onLocalIconsChanged -= Rebuild;
            }
            _subscribedToManager = false;
            _iconManager = null;
            _expanded = false;
        }

        /// <summary>
        /// The manager is a spawned NetworkObject, so it may not be registered yet on the frame this view
        /// enables. Poll briefly, then give up SILENTLY — a bare harness (or a bar shown outside a live
        /// session) legitimately has no manager, and that is not an error worth logging.
        /// </summary>
        private async UniTaskVoid ResolveManagerAndRebuild()
        {
            const int _maxFrames = 300;
            for (int _frame = 0; _frame < _maxFrames; _frame++)
            {
                if (this == null || !isActiveAndEnabled)
                {
                    return;
                }

                _iconManager = PlayerIconManager.For(NetworkManager.Singleton);
                if (_iconManager != null)
                {
                    _iconManager.onLocalIconsChanged += Rebuild;
                    _subscribedToManager = true;
                    Rebuild();
                    return;
                }
                await UniTask.NextFrame();
            }
        }

        private void OnHovered(Characters.Character _character) => SetExpanded(true);
        private void OnUnhovered(Characters.Character _character) => SetExpanded(false);

        private void SetExpanded(bool _value)
        {
            if (_expanded == _value)
            {
                return;
            }
            _expanded = _value;
            // Expanding reveals the overflowed icons, so the set of spawned icons changes, not just their
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

            List<ulong> _iconIds = ResolveIconIds();

            int _visible = _layout.VisibleCount(_iconIds.Count, _expanded, maxVisible);
            int _overflow = _layout.OverflowCount(_iconIds.Count, _expanded, maxVisible);
            IReadOnlyList<LayoutPoint> _points =
                _layout.Compute(_iconIds.Count, _expanded, collapsedStep, expandedStep, maxVisible);

            EnsureIconCount(_visible);

            for (int _i = 0; _i < _visible; _i++)
            {
                Image _icon = _spawnedIcons[_i];
                _icon.sprite = ResolveSprite(_iconIds[_i]);
                // A power with no barIcon draws nothing — a NORMAL case, never an error.
                _icon.gameObject.SetActive(_icon.sprite != null);

                var _rect = (RectTransform)_icon.transform;
                var _destination = new Vector2(_points[_i].X, _points[_i].Y);
                // DOKill before re-tweening so a hover-exit mid-expansion folds back in the same easing
                // instead of fighting a live tween.
                _rect.DOKill();
                _rect.DOAnchorPos(_destination, expandTweenDuration).SetEase(Ease.OutQuint);
            }

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

        // This thumbnail's marked-player id, then the icons THIS peer is allowed to see on it. A client
        // only ever holds its own slice, so there is nothing to filter for privacy here.
        private List<ulong> ResolveIconIds()
        {
            if (_iconManager == null || _barObject.playerCharacter == null)
            {
                return new List<ulong>();
            }
            return _iconManager.GetLocalIconsFor(_barObject.playerCharacter.ownerClientId.Value);
        }

        // An icon id is the declaring Power's NetworkObjectId; the sprite is that power's BarIcon. A
        // despawned power or a power with no sprite yields null and the slot simply stays blank.
        private Sprite ResolveSprite(ulong _iconId)
        {
            NetworkManager _networkManager = NetworkManager.Singleton;
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

        private void EnsureIconCount(int _count)
        {
            while (_spawnedIcons.Count < _count)
            {
                Image _clone = Instantiate(iconTemplate, iconTemplate.transform.parent);
                _clone.raycastTarget = false;
                _clone.gameObject.SetActive(true);
                _spawnedIcons.Add(_clone);
            }

            for (int _i = _count; _i < _spawnedIcons.Count; _i++)
            {
                if (_spawnedIcons[_i] != null)
                {
                    _spawnedIcons[_i].transform.DOKill();
                    _spawnedIcons[_i].gameObject.SetActive(false);
                }
            }
        }
    }
}
