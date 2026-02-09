
using UnityEngine;
using UnityEngine.EventSystems;

namespace Smartphone
{
    public class SwipeButton : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private SmartphoneController smartphoneController;
        [SerializeField] private SmartphoneController.SwipeDirection swipeDirection;
        
        public void OnPointerClick(PointerEventData _eventData)
        {
            smartphoneController.OnSwipe(swipeDirection);
        }
    }
}
