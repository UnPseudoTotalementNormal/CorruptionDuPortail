using System;
using Characters;
using Characters.Powers;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI.PowerBar
{
    public class PowersBarObject : MonoBehaviour
    {
        [HideInInspector] public Power power;
        [HideInInspector] public Character fromCharacter;
        
        [SerializeField] private Image powerImage;
        [SerializeField] private TMP_Text powerNameText;
        
        public event Action<Power> onPowerBarObjectClicked;
        
        public void SetPower(Power _power, Character _fromCharacter)
        {
            power = _power;
            fromCharacter = _fromCharacter;
            Init();
        }

        private void Init()
        {
            powerNameText.text = power.powerName.ToString();
            GetComponentInChildren<CustomButton>().onButtonClicked += () =>
            {
                onPowerBarObjectClicked?.Invoke(power);
            };
        }
    }
}