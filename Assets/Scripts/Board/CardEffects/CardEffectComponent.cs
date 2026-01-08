using System;
using UnityEngine;

namespace Board
{
    public class CardEffectComponent : MonoBehaviour
    {
        public event Action onObjectDestroyed;
        
        public void Initialize(Card _card)
        {
            
        }

        private void OnDestroy()
        {
            onObjectDestroyed?.Invoke();
        }
    }
}