#region

using TooltipSystem;
using UnityEngine;
using UnityEngine.EventSystems;

#endregion

namespace Board.UI.PowerBar
{
    /// <summary>
    /// Implémentation 3D de PowersBarObject utilisant des objets 3D dans le monde
    /// </summary>
    public class PowerBarObject3D : PowersBarObject, IPointerClickHandler
    {
        private Collider powerCollider;
        private HoverTooltipComponent hoverTooltipComponent;

        protected override void InitializeComponents()
        {
            powerCollider = GetComponent<Collider>();
            hoverTooltipComponent = GetComponentInChildren<HoverTooltipComponent>();
        }

        protected override void SetupInteraction()
        {
            SetTooltip();
        }
        
        private void SetTooltip()
        {
            if (hoverTooltipComponent != null)
            {
                hoverTooltipComponent.SetTooltipTitle(power.powerName.ToString());
                
                var _description = power.powerDescription.ToString();
                
                if (power.powerComponents.Count == 0)
                {
                    hoverTooltipComponent.SetTooltipDescription(_description);
                    return;
                }
                
                _description += "\n";
                
                for (var _index = 0; _index < power.powerComponents.Count; _index++)
                {
                    var _powerComponent = power.powerComponents[_index];
                    if (_index > 0)
                    {
                        _description += ", ";
                    }
                    _description += $"<link=powercomponent_{power.ownerClientId.Value}_{power.NetworkObjectId}_{_index}>{_powerComponent.componentName.ToString()}</link>";
                }
                
                hoverTooltipComponent.SetTooltipDescription(_description);
            }
        }

        protected override void UpdatePowerDisplay()
        {
            if (power == null) return;
        }

        public override void SetInteractable(bool _interactable)
        {
            if (powerCollider != null)
            {
                powerCollider.enabled = _interactable;
            }
        }

        private void OnMouseDown()
        {
            OnButtonClicked();
        }

        public void OnPointerClick(PointerEventData _eventData)
        {
            OnMouseDown();
        }
    }
}