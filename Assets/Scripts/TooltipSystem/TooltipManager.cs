using UnityEngine;
using UnityEngine.UI;

namespace TooltipSystem
{
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager instance;
        
        [SerializeField] private TooltipWindow tooltipPrefab;
        
        [SerializeField] private Transform tooltipCanvas;
        
        public TooltipWindow CreateNewTooltip(GameObject _linkedGameObject, string _tooltipTitle, string _tooltipDescription)
        {
            TooltipWindow _newTooltip = Instantiate(tooltipPrefab, tooltipCanvas);
            _newTooltip.TitleText.text = _tooltipTitle;
            _newTooltip.DescriptionText.text = _tooltipDescription;

            if (_linkedGameObject.TryGetComponent(out ITooltipTrigger _tooltipTrigger))
            {
                _tooltipTrigger.onTooltipTryClose += () => { TryCloseTooltip(_newTooltip); };
                _tooltipTrigger.onTooltipForceClose += () => { CloseTooltip(_newTooltip); };
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_newTooltip.GetComponent<RectTransform>());
            return _newTooltip;
        }
        
        public void TryCloseTooltip(TooltipWindow _tooltip)
        {
            if (!_tooltip)
            {
                return;
            }
            
            CloseTooltip(_tooltip);
        }
        
        public void CloseTooltip(TooltipWindow _tooltip)
        {
            if (!_tooltip)
            {
                return;
            }
            
            Destroy(_tooltip.gameObject);
        }
    }
}
