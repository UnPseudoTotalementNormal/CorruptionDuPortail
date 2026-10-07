using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    public class TooltipWindow : MonoBehaviour, ITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
    {
        public TMP_Text TitleText;
        public TMP_Text DescriptionText;
        
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;
        public Vector2 tooltipOffsetDirection { get; set; } = Vector2.up;
        public Vector2 tooltipPixelOffset => Vector2.zero;
        public RectTransform TooltipBoundsOverride => null;

        private int linkIndex = -1;


        public void OnPointerEnter(PointerEventData eventData)
        {
            onMouseEnterTrigger?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onMouseExitTrigger?.Invoke();
        }

        void Update()
        {
            // Input System pointer, not the legacy Input.mousePosition (the OS cursor): with autoplay's virtual mouse the
            // real devices are disabled, and the user's own cursor must never drive an unfocused player.
            var _pointer = UnityEngine.InputSystem.Pointer.current;
            Vector3 _pointerPosition = _pointer != null ? (Vector3)_pointer.position.ReadValue() : Input.mousePosition;
            int _linkIndex = TMP_TextUtilities.FindIntersectingLink(DescriptionText, _pointerPosition, null);

            var _tooltipManager = TooltipManager.instance;
            if (_linkIndex == -1)
            {
                if (_tooltipManager.IsTooltipOpenForGameObject(gameObject))
                {
                    /*
                    var _tooltipInstanceInfo = _tooltipManager.GetTooltipInstanceInfo(gameObject);
                    _tooltipManager.CloseTooltip(_tooltipInstanceInfo.tooltipWindow, _tooltipInstanceInfo);
                    */
                }
                else
                {
                    linkIndex = _linkIndex;
                }
                return;
            }

            if (_linkIndex != linkIndex)
            {
                linkIndex = _linkIndex;
                if (_tooltipManager.IsTooltipOpenForGameObject(gameObject))
                {
                    var _tooltipInstanceInfo = _tooltipManager.GetTooltipInstanceInfo(gameObject);
                    _tooltipManager.CloseTooltip(_tooltipInstanceInfo.tooltipWindow, _tooltipInstanceInfo);
                }
            }
            else if (_tooltipManager.IsTooltipOpenForGameObject(gameObject))
            {
                return;
            }

            var _linkInfo = DescriptionText.textInfo.linkInfo[_linkIndex];
            var _tooltipReference = _tooltipManager.tooltipLinkParser.GetTooltipReference(_linkInfo.GetLinkID());
            _tooltipManager.CreateNewTooltipFromGameObject(gameObject, _tooltipReference.title,
                _tooltipReference.description);
        }
    }
}