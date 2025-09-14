using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

namespace TooltipSystem
{
    public class TooltipLinkReferenceHolder : MonoBehaviour
    {
        [SerializeField] private SerializedDictionary<string, TooltipReference> textTooltipReferences = new();

        private void Awake()
        {
            foreach (var key in new List<string>(textTooltipReferences.Keys))
            {
                var value = textTooltipReferences[key];
                textTooltipReferences.Remove(key);
                textTooltipReferences[key.ToLower()] = value;
            }
        }

        public TooltipReference GetTooltipReference(string linkKey)
        {
            linkKey = linkKey.ToLower();
            
            if (linkKey.StartsWith("power_"))
            {
                return GetPowerTooltipReference(linkKey);
            }
            
            return textTooltipReferences.GetValueOrDefault(linkKey);
        }
        
        private TooltipReference GetPowerTooltipReference(string linkID)
        {
            //TODO: jarvis, faudra faire ça avec des Addressables
            return null;
        }
    }
}
