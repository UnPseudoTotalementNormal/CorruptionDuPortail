using System.Collections.Generic;
using Board;
using CorruptionDuPortail.Domain;
using DG.Tweening;
using TMPro;
using TransformComposition;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Presentation
{
    /// <summary>
    /// T23, the day in first person (mockups jour-pov v1 R3, choices in memory "T23"): by day the cards leave the table
    /// for a row at the bottom of the screen (key 1, the players) above the roles of the game with their two counters
    /// (key 2, the cork's characters bar and the corruption / robot counters). Both open when the day starts; a closed
    /// row slides out under the screen.
    ///
    /// The rows are the REAL objects (the cards with everything they show: known roles, marks, "A voté"; the cork's
    /// bar), moved in front of the camera and flown back to the table at night, so nothing has to be drawn twice and the
    /// flight has no visible cut. They hang under the camera, scaled by its field of view (<see cref="ScreenRowLayout"/>
    /// works in screen half-heights), so they stay put on screen while the head turns. Cards dealt by day
    /// (<see cref="BoardManager.ShowAllPlayerCards"/>) fly straight to the row: this is the board's layout override.
    /// Local presentation only.
    /// </summary>
    [DefaultExecutionOrder(310)]
    public class DayScreenRows : MonoBehaviour, ICardLayoutOverride
    {
        private const string TransformLayer = "Transform";
        // The cards' and the roles bar's lit UI shaders (Shader Graphs/UI_LitImage*): 0 = lit by the room (the table), 1 =
        // their own colours, evenly, like screen UI (Poyo 2026-10-21: the cards in the rows were dark).
        private static readonly int CardUiLitId = Shader.PropertyToID("_CardUiLit");

        [SerializeField] private BoardManager _boardManager;
        [Tooltip("The cork's characters bar (the roles of the game, grouped by side): row 2.")]
        [SerializeField] private RectTransform _rolesBar;
        [Tooltip("The corruption and robot counters, stacked right of the bar in row 2.")]
        [SerializeField] private RectTransform[] _counters = new RectTransform[0];
        [Tooltip("Distance of the rows in front of the camera (world units). Near enough for no cat to come between.")]
        [SerializeField] private float _distance = 2f;
        [Tooltip("A card's size in its own units at scale 1 (its 3D model): width, height.")]
        [SerializeField] private Vector2 _cardModelSize = new Vector2(6.3f, 8.8f);
        [Tooltip("Seconds for a card or the bar to fly between the table and the screen.")]
        [SerializeField] private float _flySeconds = 0.8f;
        [Tooltip("Seconds for a row to slide in / out (keys 1 and 2).")]
        [SerializeField] private float _slideSeconds = 0.35f;
        [Tooltip("Colour the other cards take while one cat is pointed at (mockups jour-pov v1 H2: its card stands out).")]
        [SerializeField] private Color _dimmed = new Color(0.62f, 0.62f, 0.62f, 1f);

        private readonly List<Card> _cards = new();
        private readonly Dictionary<Graphic, Color> _dimmedGraphics = new();
        private readonly List<Graphic> _graphicsBuffer = new();
        private readonly List<SavedPose> _saved = new();
        private Camera _camera;
        private Transform _cardAnchor;
        private Canvas _rolesCanvas;
        private float _aspect;
        // Card size in the row: a card at layer scale 1 is this many screen half-heights per model unit. It lives on the
        // anchor (the cards' parent), not on the cards: the card's compositor drives its root from its layers, and its
        // own animations (the flip lifts it 4 units, the hover 0.35) are in parent units, so they keep their table
        // proportions only if the parent carries the size.
        private float _cardScale = 1f;
        private ulong? _raisedSeat;
        private ulong? _litSeat;
        private Card _spotlit;
        private float _uiLit;
        private Tween _uiLitTween;

        /// <summary>Whether the day rows are up (between <see cref="Enter"/> and <see cref="Exit"/>).</summary>
        public bool IsActive { get; private set; }
        public bool CardsOpen { get; private set; } = true;
        public bool RolesOpen { get; private set; } = true;
        /// <summary>Both rows sent under the screen for a moment, whatever the keys say (T19: the chained cat falls to
        /// the floor, which the rows would hide).</summary>
        public bool Away { get; private set; }
        private bool CardsShown => CardsOpen && !Away;
        private bool RolesShown => RolesOpen && !Away;

        private struct SavedPose
        {
            public RectTransform Rect;
            public Transform Parent;
            public int Sibling;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        private void Awake()
        {
            Assert.IsNotNull(_boardManager, "DayScreenRows._boardManager is not wired — wire the BoardManager in GameScene.");
            Assert.IsNotNull(_rolesBar, "DayScreenRows._rolesBar is not wired — wire the cork's CharactersBar in GameScene.");
        }

        /// <summary>The day starts: both rows open, the cards and the roles fly from the table to the screen.</summary>
        public void Enter()
        {
            if (IsActive)
            {
                return;
            }
            if (!EnsureAnchors())
            {
                return;
            }
            IsActive = true;
            CardsOpen = true;
            RolesOpen = true;
            Away = false;
            _spotlit = null;
            _raisedSeat = null;
            _boardManager.cardLayoutOverride = this;
            PlaceCards(_boardManager.visibleCards);
            LiftRoles();
            FadeUiLit(1f);
            Diagnostics.Logs.GameLog.Info("ui", "day.rows.enter", "cards", _boardManager.visibleCards.Count);
        }

        /// <summary>The day ends: the cards fly back to their places on the table, the bar back onto the cork.</summary>
        public void Exit()
        {
            if (!IsActive)
            {
                return;
            }
            IsActive = false;
            _spotlit = null;
            SetLitSeat(null);
            _raisedSeat = null;
            if (ReferenceEquals(_boardManager.cardLayoutOverride, this))
            {
                _boardManager.cardLayoutOverride = null;
            }
            foreach (Card _card in _boardManager.visibleCards)
            {
                if (_card == null)
                {
                    continue;
                }
                _card.visualComponents.inScreenRow = false;
                // The compositor folds the parent change into the Transform layer: no jump, the card flies from here.
                _card.transform.SetParent(_boardManager.transform, true);
                _card.visualComponents.compositor.ApplyComposedTransform(); // fold now, before any tween starts
            }
            _cards.Clear();
            var _from = new Dictionary<Card, Vector3>();
            foreach (Card _card in _boardManager.visibleCards)
            {
                if (_card != null) _from[_card] = _card.visualComponents.compositor.GetLayer(TransformLayer).localScale;
            }
            _boardManager.PlaceAllCardsToPosition();
            // The board sets the cards' table size at once and leaves their rotation: ease both instead.
            foreach (KeyValuePair<Card, Vector3> _entry in _from)
            {
                var _layer = _entry.Key.visualComponents.compositor.GetLayer(TransformLayer);
                Vector3 _tableScale = _layer.localScale;
                _layer.localScale = _entry.Value;
                _layer.DOScale(_tableScale, _flySeconds).SetEase(Ease.InOutCubic);
                SlerpToIdentity(_layer, _flySeconds);
            }
            LowerRoles();
            FadeUiLit(0f);
        }

        // The light eases from the room's to the screen's while the cards fly (no cut when they enter the rows).
        private void FadeUiLit(float _to)
        {
            _uiLitTween?.Kill();
            _uiLitTween = DOTween.To(() => _uiLit, SetUiLit, _to, _flySeconds).SetEase(Ease.InOutSine).SetLink(gameObject);
        }

        private void SetUiLit(float _value)
        {
            _uiLit = _value;
            Shader.SetGlobalFloat(CardUiLitId, _value);
        }

        /// <summary>Keys 1 / 2: open or close a row.</summary>
        public void ToggleCards()
        {
            CardsOpen = !CardsOpen;
            Relayout(_slideSeconds);
        }

        public void ToggleRoles()
        {
            RolesOpen = !RolesOpen;
            Relayout(_slideSeconds);
            PlaceRoles(_slideSeconds);
        }

        // ---- T19: one card leaves its row (the chained player's, to the front of its cat) and comes back

        /// <summary>Sends both rows under the screen (true) or brings the open ones back (false).</summary>
        public void SetAway(bool _away)
        {
            if (Away == _away)
            {
                return;
            }
            Away = _away;
            Relayout(_slideSeconds);
            PlaceRoles(_slideSeconds);
        }

        /// <summary>Takes <paramref name="_seat"/>'s card out of the row's layout; false when it has none here.</summary>
        public bool BeginSpotlight(ulong _seat)
        {
            Card _card = IsActive ? CardOf(_seat) : null;
            if (_card == null)
            {
                return false;
            }
            _spotlit = _card;
            _card.visualComponents.compositor.GetLayer(TransformLayer).DOKill();
            return true;
        }

        /// <summary>Where <paramref name="_seat"/>'s card sits in its row: centre and height, in screen half-heights.</summary>
        public bool TryGetSlot(ulong _seat, out Vector2 _centre, out float _height)
        {
            _centre = default;
            _height = 0f;
            int _index = _cards.IndexOf(CardOf(_seat));
            if (!IsActive || _index < 0)
            {
                return false;
            }
            float _cardAspect = _cardModelSize.x / _cardModelSize.y;
            _height = ScreenRowLayout.CardHeightFor(_cards.Count, _aspect, _cardAspect);
            ScreenRowSlot _slot = ScreenRowLayout.CardSlots(_cards.Count, _aspect, _cardAspect, CardsOpen, RolesOpen)[_index];
            _centre = new Vector2(_slot.X, _slot.Y);
            return true;
        }

        /// <summary>Puts the taken card at <paramref name="_centre"/>, <paramref name="_height"/> high (screen half-heights).</summary>
        public void SetSpotlightPose(Vector2 _centre, float _height)
        {
            if (_spotlit == null || !IsActive)
            {
                return;
            }
            var _layer = _spotlit.visualComponents.compositor.GetLayer(TransformLayer);
            float _rowHeight = Mathf.Max(1e-4f, _cardScale * _cardModelSize.y);
            _layer.localPosition = new Vector3(_centre.x / _cardScale, 0f, _centre.y / _cardScale);
            _layer.localScale = Vector3.one * (_height / _rowHeight);
            _layer.localRotation = Quaternion.identity;
        }

        /// <summary>The taken card goes back to its slot.</summary>
        public void EndSpotlight()
        {
            if (_spotlit == null)
            {
                return;
            }
            _spotlit = null;
            Relayout(_slideSeconds);
        }

        /// <summary>My vote (mockups jour-pov v2 M2): my voted player's card stays raised. Null = none.</summary>
        public void SetRaisedSeat(ulong? _seat)
        {
            if (_raisedSeat == _seat)
            {
                return;
            }
            _raisedSeat = _seat;
            if (IsActive)
            {
                Relayout(_slideSeconds);
            }
        }

        /// <summary>The cat the player points at (mockups jour-pov v1 H2): its card stands out, the others dim. Null = none.</summary>
        public void SetLitSeat(ulong? _seat)
        {
            if (_litSeat == _seat)
            {
                return;
            }
            _litSeat = _seat;
            RestoreGraphics();
            if (_seat == null || !IsActive)
            {
                return;
            }
            foreach (Card _card in _cards)
            {
                if (_card == null || SeatOf(_card) == _seat)
                {
                    continue;
                }
                _card.GetComponentsInChildren(true, _graphicsBuffer);
                foreach (Graphic _graphic in _graphicsBuffer)
                {
                    _dimmedGraphics[_graphic] = _graphic.color;
                    _graphic.color = _graphic.color * _dimmed;
                }
            }
        }

        /// <summary>The seat whose card is this one, or null (no character yet).</summary>
        public static ulong? SeatOf(Card _card) =>
            _card != null && _card.characterInfo != null ? _card.characterInfo.ownerClientId.Value : (ulong?)null;

        /// <summary>The card of <paramref name="_seat"/> in the row, or null.</summary>
        public Card CardOf(ulong _seat)
        {
            foreach (Card _card in _cards)
            {
                if (SeatOf(_card) == _seat)
                {
                    return _card;
                }
            }
            return null;
        }

        // ---- the board's layout override: the cards go to the row

        public bool PlaceCards(IReadOnlyList<Card> _boardCards)
        {
            if (!IsActive || !EnsureAnchors())
            {
                return false;
            }
            _cards.Clear();
            foreach (Card _card in _boardCards)
            {
                if (_card != null)
                {
                    _cards.Add(_card);
                }
            }
            UpdateCardScale();
            foreach (Card _card in _cards)
            {
                _card.visualComponents.inScreenRow = true;
                if (_card.transform.parent != _cardAnchor)
                {
                    // The compositor folds the parent change into the Transform layer: the card flies from the table.
                    _card.transform.SetParent(_cardAnchor, true);
                    _card.visualComponents.compositor.ApplyComposedTransform(); // fold now, before any tween starts
                }
            }
            if (_litSeat != null)
            {
                ulong? _lit = _litSeat;
                _litSeat = null;
                SetLitSeat(_lit);
            }
            Relayout(_flySeconds);
            return true;
        }

        // Every card to its slot (eased over _seconds), scaled to the row's card height. The size goes on the card's ROOT,
        // not its Transform layer: the card's own animations move its other layers in root units (the flip lifts the
        // card 4 units off the table, toward the camera in the row), so they keep the proportions they have on the table.
        private void Relayout(float _seconds)
        {
            if (!IsActive || _cards.Count == 0)
            {
                return;
            }
            UpdateCardScale();
            float _cardAspect = _cardModelSize.x / _cardModelSize.y;
            float _height = ScreenRowLayout.CardHeightFor(_cards.Count, _aspect, _cardAspect);
            ScreenRowSlot[] _slots = ScreenRowLayout.CardSlots(_cards.Count, _aspect, _cardAspect, CardsShown, RolesShown);
            float _scale = _cardScale; // positions are in anchor units, i.e. screen half-heights / _cardScale
            for (int _i = 0; _i < _cards.Count; _i++)
            {
                Card _card = _cards[_i];
                if (_card == null || _card == _spotlit)
                {
                    continue;
                }
                var _layer = _card.visualComponents.compositor.GetLayer(TransformLayer);
                // Anchor space: x = screen right, z = screen up (the anchor is turned so the cards face the camera); the
                // layer's position is in root units, hence the division by the root's scale.
                float _raise = CardsShown && _raisedSeat != null && SeatOf(_card) == _raisedSeat
                    ? ScreenRowLayout.RaisedShare * _height
                    : 0f;
                _layer.DOKill();
                _layer.DOLocalMoveX(_slots[_i].X / _scale, _seconds).SetEase(Ease.OutCubic);
                _layer.DOLocalMoveY(0f, _seconds).SetEase(Ease.OutCubic);
                _layer.DOLocalMoveZ((_slots[_i].Y + _raise) / _scale, _seconds).SetEase(Ease.OutCubic);
                _layer.DOScale(1f, _seconds).SetEase(Ease.OutCubic);
                SlerpToIdentity(_layer, _seconds);
            }
        }

        // The card size for the current count and screen, applied to the anchor.
        private void UpdateCardScale()
        {
            _aspect = _camera != null ? _camera.aspect : _aspect;
            float _height = ScreenRowLayout.CardHeightFor(Mathf.Max(1, _cards.Count), _aspect, _cardModelSize.x / _cardModelSize.y);
            _cardScale = _height / _cardModelSize.y;
            if (_camera != null && _cardAnchor != null)
            {
                ScaleAnchors();
            }
        }

        private static void SlerpToIdentity(TransformLayer _layer, float _seconds)
        {
            Quaternion _start = _layer.localRotation;
            if (_seconds <= 0f)
            {
                _layer.localRotation = Quaternion.identity;
                return;
            }
            DOTween.To(() => 0f, _t => _layer.localRotation = Quaternion.Slerp(_start, Quaternion.identity, _t), 1f, _seconds)
                .SetEase(Ease.OutCubic).SetTarget(_layer);
        }

        // ---- row 2: the cork's bar and the counters

        private void LiftRoles()
        {
            _saved.Clear();
            Save(_rolesBar);
            foreach (RectTransform _counter in _counters)
            {
                Save(_counter);
            }
            foreach (SavedPose _pose in _saved)
            {
                _pose.Rect.DOKill();
                _pose.Rect.SetParent(_rolesCanvas.transform, true);
            }
            PlaceRoles(_flySeconds);
        }

        private void Save(RectTransform _rect)
        {
            if (_rect == null)
            {
                return;
            }
            _saved.Add(new SavedPose
            {
                Rect = _rect,
                Parent = _rect.parent,
                Sibling = _rect.GetSiblingIndex(),
                LocalPosition = _rect.localPosition,
                LocalRotation = _rect.localRotation,
                LocalScale = _rect.localScale,
            });
        }

        // The bar fills the roles row's height; the counters stack at its right; all smaller if the row would not fit.
        private void PlaceRoles(float _seconds)
        {
            if (!IsActive || _rolesBar == null || _rolesBar.parent != _rolesCanvas.transform)
            {
                return;
            }
            float _height = ScreenRowLayout.RolesHeight;
            float _barScale = _height / Mathf.Max(1f, _rolesBar.rect.height);
            float _barWidth = _rolesBar.rect.width * _barScale;
            float _counterHeight = _height * 0.48f;
            float _countersWidth = 0f;
            foreach (RectTransform _counter in _counters)
            {
                if (_counter != null)
                {
                    _countersWidth = Mathf.Max(_countersWidth, _counter.rect.width * _counterHeight / Mathf.Max(1f, _counter.rect.height));
                }
            }
            float _gap = _height * 0.15f;
            float _total = _barWidth + (_countersWidth > 0f ? _gap + _countersWidth : 0f);
            float _fit = Mathf.Min(1f, ScreenRowLayout.MaxWidthShare * 2f * _aspect / Mathf.Max(0.001f, _total));
            float _left = -_total * _fit / 2f;
            float _y = ScreenRowLayout.RolesCentreY(RolesShown);

            MoveRect(_rolesBar, new Vector2(_left + _barWidth * _fit / 2f, _y), _barScale * _fit, _seconds);
            float _counterX = _left + (_barWidth + _gap + _countersWidth / 2f) * _fit;
            for (int _i = 0; _i < _counters.Length; _i++)
            {
                RectTransform _counter = _counters[_i];
                if (_counter == null)
                {
                    continue;
                }
                float _scale = _counterHeight / Mathf.Max(1f, _counter.rect.height) * _fit;
                float _offset = (_i - (_counters.Length - 1) / 2f) * -(_height / 2f) * _fit;
                MoveRect(_counter, new Vector2(_counterX, _y + _offset), _scale, _seconds);
            }
        }

        // Puts a rect's CENTRE at _centre (canvas space = screen half-heights), facing the camera.
        private static void MoveRect(RectTransform _rect, Vector2 _centre, float _scale, float _seconds)
        {
            Vector3 _target = new Vector3(_centre.x - _rect.rect.center.x * _scale, _centre.y - _rect.rect.center.y * _scale, 0f);
            _rect.DOKill();
            _rect.DOLocalMove(_target, _seconds).SetEase(Ease.InOutCubic);
            _rect.DOLocalRotateQuaternion(Quaternion.identity, _seconds).SetEase(Ease.InOutCubic);
            _rect.DOScale(Vector3.one * _scale, _seconds).SetEase(Ease.InOutCubic);
        }

        private void LowerRoles()
        {
            foreach (SavedPose _pose in _saved)
            {
                if (_pose.Rect == null || _pose.Parent == null)
                {
                    continue;
                }
                _pose.Rect.DOKill();
                _pose.Rect.SetParent(_pose.Parent, true);
                _pose.Rect.SetSiblingIndex(_pose.Sibling);
                _pose.Rect.DOLocalMove(_pose.LocalPosition, _flySeconds).SetEase(Ease.InOutCubic);
                _pose.Rect.DOLocalRotateQuaternion(_pose.LocalRotation, _flySeconds).SetEase(Ease.InOutCubic);
                _pose.Rect.DOScale(_pose.LocalScale, _flySeconds).SetEase(Ease.InOutCubic);
            }
            _saved.Clear();
        }

        // ---- the anchors under the camera

        private bool EnsureAnchors()
        {
            Camera _main = Camera.main;
            if (_main == null)
            {
                return false;
            }
            if (_main != _camera || _cardAnchor == null || _rolesCanvas == null)
            {
                _camera = _main;
                if (_cardAnchor == null)
                {
                    _cardAnchor = new GameObject("DayCardRow").transform;
                }
                _cardAnchor.SetParent(_camera.transform, false);
                // Turned so a card's face (its local up) looks at the camera and its top (its local forward) points up.
                _cardAnchor.localPosition = new Vector3(0f, 0f, _distance);
                _cardAnchor.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                if (_rolesCanvas == null)
                {
                    var _canvasObject = new GameObject("DayRolesRow", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                    _rolesCanvas = _canvasObject.GetComponent<Canvas>();
                    _rolesCanvas.renderMode = RenderMode.WorldSpace;
                    // Never above the cards for the UI raycast (project-context: canvases rank by sorting order first).
                    _rolesCanvas.sortingOrder = -1;
                }
                _rolesCanvas.worldCamera = _camera;
                RectTransform _canvasRect = (RectTransform)_rolesCanvas.transform;
                _canvasRect.SetParent(_camera.transform, false);
                _canvasRect.localPosition = new Vector3(0f, 0f, _distance);
                _canvasRect.localRotation = Quaternion.identity;
                _canvasRect.sizeDelta = Vector2.zero;
                _aspect = _camera.aspect;
            }
            ScaleAnchors();
            return true;
        }

        // One anchor unit = one screen half-height at the rows' distance, whatever the field of view does.
        private void ScaleAnchors()
        {
            float _halfHeight = _distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _cardAnchor.localScale = Vector3.one * (_halfHeight * _cardScale);
            _rolesCanvas.transform.localScale = Vector3.one * _halfHeight;
        }

        private void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }
            if (!EnsureAnchors())
            {
                return;
            }
            if (!Mathf.Approximately(_camera.aspect, _aspect))
            {
                _aspect = _camera.aspect;
                Relayout(0f);
                PlaceRoles(0f);
            }
            ReadKeys();
        }

        private void ReadKeys()
        {
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard == null || IsTyping())
            {
                return;
            }
            if (_keyboard.digit1Key.wasPressedThisFrame || _keyboard.numpad1Key.wasPressedThisFrame)
            {
                ToggleCards();
                Diagnostics.Logs.GameLog.Info("ui", "day.rows.cards", "open", CardsOpen);
            }
            if (_keyboard.digit2Key.wasPressedThisFrame || _keyboard.numpad2Key.wasPressedThisFrame)
            {
                ToggleRoles();
                Diagnostics.Logs.GameLog.Info("ui", "day.rows.roles", "open", RolesOpen);
            }
        }

        /// <summary>A text field has the keyboard (the chat): the digits are typed, not row keys.</summary>
        public static bool IsTyping()
        {
            GameObject _selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (_selected == null)
            {
                return false;
            }
            return (_selected.TryGetComponent(out TMP_InputField _tmp) && _tmp.isFocused)
                   || (_selected.TryGetComponent(out InputField _legacy) && _legacy.isFocused);
        }

        private void RestoreGraphics()
        {
            foreach (KeyValuePair<Graphic, Color> _entry in _dimmedGraphics)
            {
                if (_entry.Key != null)
                {
                    _entry.Key.color = _entry.Value;
                }
            }
            _dimmedGraphics.Clear();
        }

        private void OnDestroy()
        {
            _uiLitTween?.Kill();
            Shader.SetGlobalFloat(CardUiLitId, 0f);
            RestoreGraphics();
            if (_boardManager != null && ReferenceEquals(_boardManager.cardLayoutOverride, this))
            {
                _boardManager.cardLayoutOverride = null;
            }
        }
    }
}
