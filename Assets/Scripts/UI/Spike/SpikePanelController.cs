using UnityEngine;
using UnityEngine.UIElements;

namespace UI.Spike
{
    /// <summary>
    /// THROWAWAY SPIKE (tablet InfoTable -> UI Toolkit feasibility). Drives a UIDocument panel, reused on both
    /// render paths under test: (A) native World-Space PanelSettings, and (B) a screen-space panel rendered to
    /// a RenderTexture shown on a world mesh. Presentation only, zero game plumbing. Every cell click emits a
    /// [SPIKE] log so the play-mode observer can confirm pointer routing actually reaches the panel per path,
    /// and the intentionally oversized child ("spike-overflow") proves whether overflow:hidden clips content to
    /// the panel bounds (the UITK equivalent of the current uGUI stencil Mask).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SpikePanelController : MonoBehaviour
    {
        [SerializeField] private string _label = "SPIKE";
        [SerializeField] private UIDocument _document;

        private void OnEnable()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            VisualElement _root = _document != null ? _document.rootVisualElement : null;
            if (_root == null)
            {
                Debug.LogWarning("[SPIKE] SpikePanelController: rootVisualElement null on OnEnable (retrying at Start).");
                return;
            }

            Bind(_root);
        }

        // UIDocument builds its tree in its own OnEnable; order vs this component is not guaranteed, so retry.
        private void Start()
        {
            if (_document != null && _document.rootVisualElement != null)
            {
                Bind(_document.rootVisualElement);
            }
        }

        private bool _bound;

        private void Bind(VisualElement _root)
        {
            if (_bound)
            {
                return;
            }

            // Spike diagnostic: root stays pickable (default Position) so we can observe input reaching it.
            // Log EVERY pointer-down that reaches the panel (TrickleDown = fires before children), with the
            // element under the pointer — disambiguates "input never reaches the panel" (camera / raycaster /
            // world-space collider problem) from "reaches but the Button click doesn't fire" (propagation).
            _root.RegisterCallback<PointerDownEvent>(_e =>
            {
                string _targetName = (_e.target as VisualElement)?.name;
                Debug.Log($"[SPIKE] {_label}: POINTER DOWN reached panel at {_e.position} -> target '{_targetName}'.");
            }, TrickleDown.TrickleDown);

            Label _tag = _root.Q<Label>("spike-tag");
            if (_tag != null)
            {
                _tag.text = _label;
            }

            int _count = 0;
            _root.Query<Button>(className: "spike-cell").ForEach(_cell =>
            {
                _cell.RegisterCallback<ClickEvent>(_ =>
                    Debug.Log($"[SPIKE] {_label}: cell '{_cell.name}' CLICKED."));
                _count++;
            });

            Debug.Log($"[SPIKE] {_label}: bound {_count} clickable cells. Render mode = {(_document.panelSettings != null ? _document.panelSettings.renderMode.ToString() : "no-panelsettings")}.");
            _bound = true;
        }
    }
}
