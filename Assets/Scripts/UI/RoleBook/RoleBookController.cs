#region

using System;
using System.Collections.Generic;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using UI.LobbyRoles;
using UI.RoleCard;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

#endregion

namespace UI.RoleBook
{
    /// <summary>
    /// The main menu's role book (RoleBook.uxml): a leather-bound grimoire, one spread per playable role, browsable
    /// outside any game. Left page = the role's card, seal, name, faction and difficulty; right page = how it wins,
    /// its passives and powers. Both come from <see cref="RoleSheet"/>, so the book says exactly what the in-game
    /// role overlay says.
    ///
    /// The book opens by swinging its cover around the spine; pages turn with a flipping leaf (dog-eared corners,
    /// arrows / Q D keys); the faction ribbons open their chapter's first role; Échap, the close tab or a click outside
    /// closes it. A new turn asked during an animation finishes the running one at once, so fast browsing stays fluid.
    ///
    /// The roles are the authored attribution pool (RoleAttributionState asset), the same list the lobby tablet
    /// offers; no network, no game state.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RoleBookController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const string RibbonClass = "role-book__ribbon";
        private const string RibbonActiveClass = "role-book__ribbon--active";
        private const string RibbonIconClass = "role-book__ribbon-icon";
        private const string RibbonLabelClass = "role-book__ribbon-label";
        private const float CardWidth = 300f;
        private const int OpenDurationMs = 760;
        private const int TurnDurationMs = 780;
        private const long CollapseDelayMs = 260;
        // The turning sheet: strips across the page; how far the free edge leads the spine (curl); spine overlap of a page.
        private const int Strips = 20;
        private const float Curl = 0.55f;
        private const float PageOverlap = 6f; // .role-book__page--left/right margins: each page dives 6px under the spine
        private const float CastShadowWidth = 170f;
        private const float Lift = 0.05f;     // perspective: a strip standing up looks this much taller (it comes at the eye)

        [SerializeField] private UIDocument document;

        [Tooltip("The authored role pool: the book shows its roles, like the lobby tablet.")]
        [SerializeField] private RoleAttributionState rolePool;

        [Tooltip("Faction names, icons and colours (pages, seals, ribbons).")]
        [SerializeField] private FactionDatabase factionDatabase;

        [Tooltip("Role portraits for the card on the left page.")]
        [SerializeField] private PortraitTable portraitTable;

        [Tooltip("The designer's passive lines, as on the in-game role overlay.")]
        [SerializeField] private RoleCardTexts roleCardTexts;

        private readonly List<Role> _pages = new();
        private readonly Dictionary<FactionType, VisualElement> _ribbons = new();
        private VisualElement _root;
        private VisualElement _stage;
        private VisualElement _cover;
        private VisualElement _boardLeft;
        private VisualElement _leftPage;
        private VisualElement _rightPage;
        private VisualElement _leftContent;
        private VisualElement _rightContent;
        private Label _leftFolio;
        private Label _rightFolio;
        private VisualElement _cornerLeft;
        private VisualElement _cornerRight;
        private VisualElement _pagesArea;
        private VisualElement _flip;
        private VisualElement _shadowRight;
        private VisualElement _shadowLeft;
        private int _current;
        private bool _initialized;
        private bool _open;

        // The running animation (cover or turning sheet): its finish action jumps it to its end state at once.
        private IValueAnimation _animation;
        private Action _finishAnimation;
        private int _animationToken;

        private RoleSheetAssets Assets => new RoleSheetAssets(factionDatabase, portraitTable, roleCardTexts);

        public bool IsOpen => _open;

        /// <summary>Raised when the book opens / closes (the main menu hides its own buttons meanwhile).</summary>
        public event Action Opened;
        public event Action Closed;

        private void OnEnable() => TryInitialize();

        // UIDocument builds its tree in its own OnEnable (order not guaranteed): Start is the fallback.
        private void Start() => TryInitialize();

        private void TryInitialize()
        {
            if (_initialized) return;
            if (document == null) document = GetComponent<UIDocument>();
            _root = document != null ? document.rootVisualElement?.Q<VisualElement>("role-book") : null;
            if (_root == null) return;

            _stage = _root.Q<VisualElement>("book-stage");
            _cover = _root.Q<VisualElement>("book-cover");
            _boardLeft = _root.Q<VisualElement>("board-left");
            _leftPage = _root.Q<VisualElement>("page-left");
            _rightPage = _root.Q<VisualElement>("page-right");
            _leftContent = _root.Q<VisualElement>("page-left-content");
            _rightContent = _root.Q<VisualElement>("page-right-content");
            _leftFolio = _root.Q<Label>("page-left-number");
            _rightFolio = _root.Q<Label>("page-right-number");
            _cornerLeft = _root.Q<VisualElement>("corner-left");
            _cornerRight = _root.Q<VisualElement>("corner-right");
            _pagesArea = _root.Q<VisualElement>("book-pages");
            _flip = _root.Q<VisualElement>("book-flip");
            _shadowRight = _root.Q<VisualElement>("book-shadow-right");
            _shadowLeft = _root.Q<VisualElement>("book-shadow-left");

            _cornerLeft.RegisterCallback<ClickEvent>(_ => TurnPage(-1));
            _cornerRight.RegisterCallback<ClickEvent>(_ => TurnPage(1));
            _root.Q<Button>("book-close").clicked += Close;
            // A click on the dark backdrop (not on the book) closes it.
            _root.RegisterCallback<PointerDownEvent>(evt => { if (evt.target == _root) Close(); });

            BuildPages();
            BuildRibbons(_root.Q<VisualElement>("book-ribbons"));
            _root.pickingMode = PickingMode.Ignore;
            _initialized = true;
        }

        private void BuildPages()
        {
            _pages.Clear();
            var roles = new List<Role>();
            if (rolePool != null)
                foreach (RoleDataObject rdo in rolePool.roleAttributionDictionary.Keys)
                {
                    Role role = LobbyRoleDetail.From(rdo);
                    if (role != null) roles.Add(role);
                }
            else
                Debug.LogWarning("[RoleBook] No role pool wired: the book is empty.");
            _pages.AddRange(RoleBookPages.Order(roles, r => r.factionType));
        }

        private void BuildRibbons(VisualElement host)
        {
            host.Clear();
            _ribbons.Clear();
            RoleSheetAssets assets = Assets;
            foreach (FactionType faction in RoleBookPages.ChapterOrder)
            {
                int first = RoleBookPages.FirstPageOf(_pages, r => r.factionType, faction);
                if (first < 0) continue;

                FactionData data = assets.Faction(faction);
                var ribbon = new VisualElement();
                ribbon.AddToClassList(RibbonClass);
                ribbon.style.unityBackgroundImageTintColor = Color.Lerp(RoleSheet.FactionColor(assets, faction), Color.black, 0.35f);
                ribbon.RegisterCallback<ClickEvent>(_ => GoTo(first));

                if (data != null && data.icon != null)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList(RibbonIconClass);
                    icon.style.backgroundImage = new StyleBackground(data.icon);
                    icon.pickingMode = PickingMode.Ignore;
                    ribbon.Add(icon);
                }
                var label = new Label(data != null ? data.displayName : faction.ToString());
                label.AddToClassList(RibbonLabelClass);
                label.pickingMode = PickingMode.Ignore;
                ribbon.Add(label);

                host.Add(ribbon);
                _ribbons[faction] = ribbon;
            }
        }

        // ------------------------------------------------------------------------------------------ open / close

        /// <summary>Open the book where it was last left (first role the first time): the cover swings open.</summary>
        public void Open()
        {
            TryInitialize();
            if (_root == null || _pages.Count == 0 || _open) return;

            _open = true;
            _root.RemoveFromClassList(CollapsedClass);
            _root.pickingMode = PickingMode.Position;
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));
            ShowSpread(Mathf.Clamp(_current, 0, _pages.Count - 1));
            PlayOpening();
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!_open) return;
            FinishAnimation();
            _open = false;
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;
            _root.schedule.Execute(() =>
            {
                if (!_open) _root.AddToClassList(CollapsedClass);
            }).ExecuteLater(CollapseDelayMs);
            Closed?.Invoke();
        }

        // Closed book = the cover centred on screen; it swings open around the spine (left edge) while the book slides
        // to the centre, then the inside of the cover (left board) and the left page unfold from the spine.
        private void PlayOpening()
        {
            float closedShift = -0.25f * _stage.resolvedStyle.width;
            if (float.IsNaN(closedShift) || closedShift == 0f) closedShift = -370f;

            void Apply(float t)
            {
                float cover = Mathf.Clamp01(t / 0.5f);
                float page = Mathf.Clamp01((t - 0.5f) / 0.5f);
                _cover.style.display = cover < 1f ? DisplayStyle.Flex : DisplayStyle.None;
                _cover.style.scale = new Scale(new Vector2(1f - Easing.InCubic(cover), 1f));
                var unfold = new Scale(new Vector2(Easing.OutCubic(page), 1f));
                _boardLeft.style.scale = unfold;
                _leftPage.style.scale = unfold;
                _stage.style.translate = new Translate(closedShift * (1f - Easing.InOutCubic(t)), 0);
            }

            Apply(0f);
            Animate(OpenDurationMs, Apply, () => Apply(1f));
        }

        // ------------------------------------------------------------------------------------------ navigation

        private void Update()
        {
            if (!_open) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // Key = physical position (US layout names): aKey / dKey are Q / D on an AZERTY keyboard.
            if (kb.escapeKey.wasPressedThisFrame) Close();
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) TurnPage(-1);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) TurnPage(1);
        }

        private void TurnPage(int delta)
        {
            FinishAnimation();
            GoTo(RoleBookPages.Turn(_current, delta, _pages.Count));
        }

        private void GoTo(int page)
        {
            if (!_open) return;
            FinishAnimation();
            if (page < 0 || page >= _pages.Count || page == _current) return;
            Turn(page);
        }

        /// <summary>Show a spread at once, no animation (also the offscreen capture's entry point).</summary>
        internal void ShowSpread(int page)
        {
            _current = page;
            SetLeft(page);
            SetRight(page);
            ClearFlip();
            _cover.style.display = DisplayStyle.None;
            _cover.style.scale = new Scale(Vector2.one);
            _leftPage.style.scale = new Scale(Vector2.one);
            _boardLeft.style.scale = new Scale(Vector2.one);
            _stage.style.translate = new Translate(0, 0);
            RefreshChrome();
        }

        // Forward: the right sheet lifts and turns over the spine, its free edge first so it curls, and lands as the next
        // role's left page; the next role's right page was already under it. Backward: the mirror image.
        //
        // The sheet is drawn as vertical strips, each a window on a full copy of the face it shows (recto = the page it
        // leaves, verso = the page it lands as). Each strip rotates around the spine by its own angle (later for strips
        // near the spine), projected as its width (cos); past 90 degrees it shows the verso. Light on each strip and the
        // shadow cast on the page beneath follow the angles.
        private void Turn(int target)
        {
            int from = _current;
            bool forward = target > from;
            _current = target;
            RoleSheetAssets assets = Assets;

            float spine = _pagesArea.resolvedStyle.width * 0.5f;
            float pageWidth = spine;           // arc length of the sheet, from the spine to its free edge
            float height = _pagesArea.resolvedStyle.height;
            if (float.IsNaN(spine) || spine <= 0f || float.IsNaN(height))
            {
                ShowSpread(target);            // not laid out (yet): no animation
                return;
            }

            // Recto / verso copies: the page left, and the page landed on.
            VisualElement Recto() => forward
                ? Face(RoleSheet.BuildKit(_pages[from], assets), 2 * from + 2, false, pageWidth)
                : Face(Identity(_pages[from], assets), 2 * from + 1, true, pageWidth);
            VisualElement Verso() => forward
                ? Face(Identity(_pages[target], assets), 2 * target + 1, true, pageWidth)
                : Face(RoleSheet.BuildKit(_pages[target], assets), 2 * target + 2, false, pageWidth);

            ClearFlip();
            float stripWidth = pageWidth / Strips;
            var strips = new VisualElement[Strips];
            var rectos = new VisualElement[Strips];
            var versos = new VisualElement[Strips];
            var shades = new VisualElement[Strips];
            for (var i = 0; i < Strips; i++)
            {
                float arc = i * stripWidth;   // distance of the strip's spine side from the spine
                // Page coordinates under the strip, for each face (see Face: a right page starts PageOverlap under the spine).
                float rightPageOffset = -(arc + PageOverlap);
                float leftPageOffset = -(pageWidth - arc - stripWidth);

                var strip = new VisualElement { pickingMode = PickingMode.Ignore };
                strip.AddToClassList("role-book__strip");
                strip.style.width = stripWidth + 1f; // 1px overlap hides the seams between strips
                rectos[i] = Recto();
                rectos[i].style.left = forward ? rightPageOffset : leftPageOffset;
                versos[i] = Verso();
                versos[i].style.left = forward ? leftPageOffset : rightPageOffset;
                versos[i].style.display = DisplayStyle.None;
                shades[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                shades[i].AddToClassList("role-book__strip-shade");
                strip.Add(rectos[i]);
                strip.Add(versos[i]);
                strip.Add(shades[i]);
                _flip.Add(strip);
                strips[i] = strip;
            }

            if (forward) SetRight(target);
            else SetLeft(target);
            RefreshChrome();

            float direction = forward ? 1f : -1f;
            void Apply(float t)
            {
                float projected = 0f;
                float minX = spine, maxX = spine;
                for (var i = 0; i < Strips; i++)
                {
                    // The free edge leads: a strip's own progress starts later the closer it is to the spine.
                    float centre = (i + 0.5f) / Strips;
                    float local = Mathf.Clamp01(t * (1f + Curl) - Curl * (1f - centre));
                    float angle = Mathf.PI * Mathf.SmoothStep(0f, 1f, local);
                    float cos = Mathf.Cos(angle);

                    float a = spine + direction * projected;
                    projected += cos * stripWidth;
                    float b = spine + direction * projected;
                    bool recto = (b - a) * direction >= 0f;

                    VisualElement strip = strips[i];
                    strip.style.left = Mathf.Min(a, b);
                    strip.style.scale = new Scale(new Vector2(Mathf.Max(Mathf.Abs(b - a) / stripWidth, 0.0001f), 1f + Lift * Mathf.Sin(angle)));
                    rectos[i].style.display = recto ? DisplayStyle.Flex : DisplayStyle.None;
                    versos[i].style.display = recto ? DisplayStyle.None : DisplayStyle.Flex;
                    // A strip seen edge-on catches less light; the verso starts a little darker (the sheet's underside).
                    float edgeOn = 1f - Mathf.Abs(cos);
                    shades[i].style.opacity = recto ? 0.55f * Mathf.Pow(edgeOn, 1.2f) : 0.10f + 0.45f * edgeOn;

                    minX = Mathf.Min(minX, Mathf.Min(a, b));
                    maxX = Mathf.Max(maxX, Mathf.Max(a, b));
                }

                // The lifted sheet shadows the page under it beside its outer edge, strongest mid-turn.
                float lift = Mathf.Sin(Mathf.PI * t);
                ShowCastShadow(_shadowRight, maxX, maxX > spine + 2f ? lift * Mathf.Clamp01((maxX - spine) / (pageWidth * 0.3f)) : 0f);
                ShowCastShadow(_shadowLeft, minX - CastShadowWidth, minX < spine - 2f ? lift * Mathf.Clamp01((spine - minX) / (pageWidth * 0.3f)) : 0f);
            }

            void Finish()
            {
                if (forward) SetLeft(target);
                else SetRight(target);
                ClearFlip();
            }

            Apply(0f);
            Animate(TurnDurationMs, Apply, Finish);
        }

        private static void ShowCastShadow(VisualElement shadow, float left, float strength)
        {
            shadow.style.left = left;
            shadow.style.opacity = 0.85f * strength;
        }

        private void ClearFlip()
        {
            _flip.Clear();
            _shadowRight.style.opacity = 0f;
            _shadowLeft.style.opacity = 0f;
        }

        // A full copy of a page (parchment, content, folio, the spine's shadow) for the turning sheet. Same size as the
        // real page: half the spread plus the part that dives under the spine.
        private static VisualElement Face(VisualElement content, int folio, bool leftPage, float pageWidth)
        {
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("role-book__face");
            face.AddToClassList("role-sheet");
            face.AddToClassList("role-sheet__page");
            face.style.width = pageWidth + PageOverlap;

            var host = new VisualElement { pickingMode = PickingMode.Ignore };
            host.AddToClassList("role-book__page-content");
            host.Add(content);
            face.Add(host);

            var number = new Label(folio.ToString()) { pickingMode = PickingMode.Ignore };
            number.AddToClassList("role-book__folio");
            number.AddToClassList("role-sheet__sc");
            face.Add(number);

            // The gutter is centred on the spine: a left page's spine is at its width minus the overlap, a right page's
            // at the overlap.
            var gutter = new VisualElement { pickingMode = PickingMode.Ignore };
            gutter.AddToClassList("role-book__face-gutter");
            gutter.style.left = (leftPage ? pageWidth : PageOverlap) - 75f;
            face.Add(gutter);
            return face;
        }

        private void SetLeft(int page)
        {
            _leftContent.Clear();
            _leftContent.Add(Identity(_pages[page], Assets));
            _leftFolio.text = (2 * page + 1).ToString();
        }

        private void SetRight(int page)
        {
            _rightContent.Clear();
            _rightContent.Add(RoleSheet.BuildKit(_pages[page], Assets));
            _rightFolio.text = (2 * page + 2).ToString();
        }

        private static VisualElement Identity(Role role, RoleSheetAssets assets) => RoleSheet.BuildIdentity(role, assets, CardWidth);

        private void RefreshChrome()
        {
            _cornerLeft.style.visibility = _current > 0 ? Visibility.Visible : Visibility.Hidden;
            _cornerRight.style.visibility = _current < _pages.Count - 1 ? Visibility.Visible : Visibility.Hidden;
            FactionType chapter = _pages[_current].factionType;
            foreach (KeyValuePair<FactionType, VisualElement> ribbon in _ribbons)
                ribbon.Value.EnableInClassList(RibbonActiveClass, ribbon.Key == chapter);
        }

        // ------------------------------------------------------------------------------------------ animation

        // One animation at a time, driven by the panel scheduler (linear 0 -> 1; the callers ease each part).
        private void Animate(int durationMs, Action<float> apply, Action finish)
        {
            FinishAnimation();
            int token = ++_animationToken;
            _finishAnimation = finish;
            var animation = _root.experimental.animation.Start(0f, 1f, durationMs, (_, t) =>
            {
                if (token == _animationToken) apply(t);
            }).Ease(Easing.Linear);
            animation.OnCompleted(() =>
            {
                if (token == _animationToken) FinishAnimation();
            });
            _animation = animation;
        }

        private void FinishAnimation()
        {
            _animationToken++;
            if (_animation != null && _animation.isRunning) _animation.Stop();
            _animation = null;
            Action finish = _finishAnimation;
            _finishAnimation = null;
            finish?.Invoke();
        }
    }
}
