using System;
using UnityEngine;

namespace Board
{
    public class CardEffectComponent : MonoBehaviour
    {
        public event Action onObjectDestroyed;
        
        public virtual void Initialize(Card _card, object _effectData = null)
        {
            
        }

        private void OnDestroy()
        {
            onObjectDestroyed?.Invoke();
        }
    }
}