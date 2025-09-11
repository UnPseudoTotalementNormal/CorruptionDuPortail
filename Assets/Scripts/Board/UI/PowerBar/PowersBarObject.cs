#region

using System;
using Characters;
using Characters.Powers;
using TMPro;
using TooltipSystem;
using UI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.UI.PowerBar
{
    public class PowersBarObject : MonoBehaviour
    {
        [HideInInspector] public Power power;
        [HideInInspector] public Character fromCharacter;
        
        [SerializeField] private Image powerImage;
        [SerializeField] private TMP_Text powerNameText;
        [HideInInspector] public CustomButton customButton;
        [HideInInspector] public HoverTooltipComponent hoverTooltipComponent;
        
        public event Action<Power> onPowerBarObjectClicked;

        private void Awake()
        {
            customButton = GetComponent<CustomButton>();
            hoverTooltipComponent = GetComponentInChildren<HoverTooltipComponent>();
        }

        private void Start()
        {
            GetComponentInChildren<CustomButton>().onButtonClicked += OnButtonClicked;
        }

        public void SetPower(Power _power, Character _fromCharacter)
        {
            power = _power;
            fromCharacter = _fromCharacter;
            Init();
        }

        private void Init()
        {
            powerNameText.text = power.powerName.ToString();
            hoverTooltipComponent.SetTooltipTitle(power.powerName.ToString());
            hoverTooltipComponent.SetTooltipDescription(power.powerDescription.ToString());
        }

        private void OnButtonClicked()
        {
            onPowerBarObjectClicked?.Invoke(power);
        }
    }
}