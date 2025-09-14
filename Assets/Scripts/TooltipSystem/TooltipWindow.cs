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
            if (TooltipManager.instance.IsTooltipOpenForGameObject(gameObject))
            {
                return;
            }
            
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(DescriptionText, Input.mousePosition, null);

            if (linkIndex != -1)
            {
                var linkInfo = DescriptionText.textInfo.linkInfo[linkIndex];
                TooltipManager.instance.CreateNewTooltipFromGameObject(gameObject, linkInfo.GetLinkID(), "test tooltip in tooltip");
            }
        }
    }
}