using System.Collections.Generic;
using Diagnostics.Logs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UI.CatPaw
{
    /// <summary>
    /// The free mouse is a cat paw (maquettes curseur-patte v1: C3, T2): claws out over what clicks, pressed while
    /// clicking it, a cross over what refuses right now, at rest elsewhere. Everywhere the mouse is free (menus, lobby,
    /// tablet, night board); while it is locked the seated reticle (<see cref="Reticle.ReticleHUD"/>) takes over.
    /// Presentation only: it reads what is under the pointer, it never clicks.
    /// </summary>
    public class CatPawCursor : MonoBehaviour
    {
        /// <summary>A UI Toolkit element that reacts to a click without being a Button (a tab, a swatch, a page corner):
        /// give it this class so the paw shows its claws over it.</summary>
        public const string ClickableClass = "paw-clickable";

        private const float PressHold = 0.15f; // a quick click still shows the pressed paw for a moment
        private const float DocsRefresh = 0.5f;
        private const float WorldMaxDistance = 200f;

        private static CatPawCursor s_instance;

        private readonly Texture2D[] _textures = new Texture2D[4];
        private readonly List<RaycastResult> _uiResults = new();
        private readonly List<UIDocument> _docs = new();
        private PointerEventData _pointerData;
        private float _docsAge = DocsRefresh;
        private float _pressedAt = -10f;
        private int _shown = -1;
        private int _set;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (s_instance != null || Application.isBatchMode)
            {
                return;
            }
            var _go = new GameObject("CatPawCursor");
            DontDestroyOnLoad(_go);
            s_instance = _go.AddComponent<CatPawCursor>();
        }

        private void Awake()
        {
            _set = Screen.height >= 1800 ? 96 : 48;
            int _loaded = 0;
            for (int _i = 0; _i < _textures.Length; _i++)
            {
                _textures[_i] = Resources.Load<Texture2D>($"CatPaw/cursor{_set}_{PawArt.Name((PawState)_i)}");
                if (_textures[_i] != null) _loaded++;
            }
            GameLog.Info("ui", "cursor.paw.ready", "size", _set, "loaded", _loaded);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
                UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        private void Update()
        {
            if (UnityEngine.Cursor.lockState == CursorLockMode.Locked)
            {
                return; // the seated reticle shows the paw at the centre
            }
            Mouse _mouse = Mouse.current;
            if (_mouse == null)
            {
                return;
            }

            Vector2 _screen = _mouse.position.ReadValue();
            CursorHint _hint = HintAt(_screen);
            if (_mouse.leftButton.wasPressedThisFrame)
            {
                _pressedAt = Time.unscaledTime;
                if (_hint == CursorHint.Refused)
                {
                    GameLog.Info("ui", "cursor.click.refused", "target", _lastTarget);
                }
            }
            bool _pressing = _mouse.leftButton.isPressed || Time.unscaledTime - _pressedAt < PressHold;
            Show(CursorHints.Paw(_hint, _pressing));
        }

        private string _lastTarget = "-";

        private void Show(PawState _state)
        {
            if ((int)_state == _shown)
            {
                return;
            }
            Texture2D _tex = _textures[(int)_state];
            if (_tex == null)
            {
                return;
            }
            _shown = (int)_state;
            Vector2 _h = PawArt.Hotspot(_state);
            UnityEngine.Cursor.SetCursor(_tex, new Vector2(_h.x * _tex.width, _h.y * _tex.height), CursorMode.Auto);
        }

        // What is under the pointer, front to back: a UI Toolkit control, then uGUI (and anything the EventSystem
        // raycasters see), then the 3D world from the main camera (powers and board buttons click through OnMouseDown).
        private CursorHint HintAt(Vector2 _screen)
        {
            _lastTarget = "-";
            if (TryUitk(_screen, out CursorHint _uitk))
            {
                return _uitk;
            }

            EventSystem _es = EventSystem.current;
            if (_es != null)
            {
                _pointerData ??= new PointerEventData(_es);
                _pointerData.position = _screen;
                _uiResults.Clear();
                _es.RaycastAll(_pointerData, _uiResults);
                if (_uiResults.Count > 0)
                {
                    GameObject _top = _uiResults[0].gameObject;
                    _lastTarget = _top.name;
                    return CursorHints.Of(_top);
                }
            }

            Camera _cam = Camera.main;
            if (_cam == null)
            {
                return CursorHint.None;
            }
            Ray _ray = _cam.ScreenPointToRay(_screen);
            if (Physics.Raycast(_ray, out RaycastHit _hit, WorldMaxDistance, _cam.eventMask, QueryTriggerInteraction.Collide))
            {
                _lastTarget = _hit.collider.name;
                CursorHint _h = CursorHints.Of(_hit.collider.gameObject);
                if (_h != CursorHint.None)
                {
                    return _h;
                }
            }
            // Inert 3D objects lose their collider (a power that cannot be used): they say so themselves.
            if (Board.UI.PowerBar.PowerBarObject3D.InertUnder(_ray, out string _name))
            {
                _lastTarget = _name;
                return CursorHint.Refused;
            }
            return CursorHint.None;
        }

        private bool TryUitk(Vector2 _screen, out CursorHint _hint)
        {
            _hint = CursorHint.None;
            _docsAge += Time.unscaledDeltaTime;
            if (_docsAge >= DocsRefresh)
            {
                _docsAge = 0f;
                _docs.Clear();
                _docs.AddRange(FindObjectsByType<UIDocument>(FindObjectsSortMode.None));
                _docs.Sort((_a, _b) => SortOf(_b).CompareTo(SortOf(_a)));
            }
            Vector2 _flipped = new(_screen.x, Screen.height - _screen.y);
            foreach (UIDocument _doc in _docs)
            {
                if (_doc == null || !_doc.isActiveAndEnabled)
                {
                    continue;
                }
                VisualElement _root = _doc.rootVisualElement;
                IPanel _panel = _root?.panel;
                if (_panel == null)
                {
                    continue;
                }
                VisualElement _picked = _panel.Pick(RuntimePanelUtils.ScreenToPanel(_panel, _flipped));
                for (VisualElement _e = _picked; _e != null && _e != _root; _e = _e.parent)
                {
                    if (_e is Button || _e is Toggle || _e is DropdownField || _e is Slider || _e is SliderInt
                        || _e.ClassListContains(ClickableClass))
                    {
                        _lastTarget = string.IsNullOrEmpty(_e.name) ? _e.GetType().Name : _e.name;
                        _hint = _e.enabledInHierarchy ? CursorHint.Clickable : CursorHint.Refused;
                        return true;
                    }
                }
            }
            return false;
        }

        private static float SortOf(UIDocument _doc) =>
            _doc != null && _doc.panelSettings != null ? _doc.panelSettings.sortingOrder : 0f;
    }
}
