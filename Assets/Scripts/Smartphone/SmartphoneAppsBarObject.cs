using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Smartphone
{
    public class SmartphoneAppsBarObject : MonoBehaviour
    {
        public SmartphoneApp app;
        public Image iconImage;
        [SerializeField] private Color enabledColor = Color.white;
        [SerializeField] private Color disabledColor = Color.gray;

        [SerializeField] private float unselectedScale = 0.5f;
        [SerializeField] private float selectedScale = 1f;
        [SerializeField] private float scaleDuration = 0.5f;
        
        private bool isSelected = false;

        public void Setup(SmartphoneApp appToCheck)
        {
            app = appToCheck;
            iconImage.sprite = app.icon;
            
            app.isActiveChanged += OnIsActiveChanged;
            OnIsActiveChanged();
            
            OnUnselect();
        }

        private void OnIsActiveChanged()
        {
            iconImage.color = app.isActive ? enabledColor : disabledColor;
        }

        public void TryUnselect()
        {
            if (!isSelected)
            {
                return;
            }
            OnUnselect();
        }
        
        public void TrySelect()
        {
            if (isSelected)
            {
                return;
            }
            OnSelect();
        }
        
        private void OnUnselect()
        {
            isSelected = false;
            transform.DOScale(unselectedScale, scaleDuration).SetEase(Ease.OutQuint);
        }
        
        private void OnSelect()
        {
            isSelected = true;
            transform.DOScale(selectedScale, scaleDuration).SetEase(Ease.OutQuint);
        }
    }
}