#region

using TMPro;
using TooltipSystem;
using UI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.UI.PowerBar
{
    /// <summary>
    /// Implémentation UI de PowersBarObject utilisant des éléments UI Canvas (Image, Text, etc.)
    /// </summary>
    public class PowerBarObjectUI : PowersBarObject
    {
        [SerializeField] private Image powerImage;
        [SerializeField] private TMP_Text powerNameText;
        
        private CustomButton customButton;
        private HoverTooltipComponent hoverTooltipComponent;

        protected override void InitializeComponents()
        {
            customButton = GetComponent<CustomButton>();
            hoverTooltipComponent = GetComponentInChildren<HoverTooltipComponent>();
        }

        protected override void SetupInteraction()
        {
            if (customButton != null)
            {
                customButton.onButtonClicked += OnButtonClicked;
            }
        }

        protected override void UpdatePowerDisplay()
        {
            if (power == null) return;

            // Mise à jour du nom
            if (powerNameText != null)
            {
                powerNameText.text = power.powerName.ToString();
            }

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

        public override void SetInteractable(bool _interactable)
        {
            if (customButton != null)
            {
                customButton.enabled = _interactable;
            }
        }
    }
}