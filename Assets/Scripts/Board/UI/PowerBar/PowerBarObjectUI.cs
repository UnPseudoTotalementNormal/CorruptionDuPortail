#region

using Characters.Powers;
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
                // Read when the bubble opens: {var:…} values follow the game.
                Power _power = power;
                hoverTooltipComponent.SetTooltipDescription(() => PowerTooltipText.Describe(_power));
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