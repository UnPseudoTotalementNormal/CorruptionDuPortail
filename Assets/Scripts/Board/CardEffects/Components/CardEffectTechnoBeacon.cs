using Characters.Powers.PowerObjects;
using UnityEngine;

namespace Board.Components
{
    public class CardEffectTechnoBeacon : CardEffectComponent
    {
        [SerializeField] private Material notCorruptedMaterial;
        [SerializeField] private Material corruptedMaterial;
        [SerializeField] private Renderer targetRenderer;
        
        private PersonalBeaconObject beacon;
        
        public override void Initialize(Card _card, object _effectData = null)
        {
            base.Initialize(_card, _effectData);
            
            beacon = _effectData as PersonalBeaconObject;
            if (beacon == null)
            {
                Debug.LogError("CardEffectTechnoBeacon: effectData is not a PersonalBeaconObject");
                return;
            }
            
            beacon.onCorruptedBeaconChanged += OnCorruptedStateChanged;
            
            UpdateMaterial(beacon.isCorruptedValue);
        }
        
        private void OnCorruptedStateChanged(bool isCorrupted)
        {
            UpdateMaterial(isCorrupted);
        }
        
        private void UpdateMaterial(bool isCorrupted)
        {
            if (targetRenderer == null)
            {
                Debug.LogWarning("CardEffectTechnoBeacon: targetRenderer is not assigned");
                return;
            }
            
            targetRenderer.material = isCorrupted ? corruptedMaterial : notCorruptedMaterial;
        }
        
        private void OnDestroy()
        {
            if (beacon != null)
            {
                beacon.onCorruptedBeaconChanged -= OnCorruptedStateChanged;
            }
        }
    }
}

