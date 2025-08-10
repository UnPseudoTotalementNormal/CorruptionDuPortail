using System;
using TMPro;
using UnityEngine;

namespace TooltipSystem
{
    public class TextTagTooltipComponent : MonoBehaviour, ITooltipTrigger
    {
        [SerializeField] private TMP_Text text;
        
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;
        [field:SerializeField] public Vector2 tooltipOffsetDirection { get; set; } = Vector2.right;

        private void Reset()
        {
            text = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            if (!text)
            {
                text = GetComponentInChildren<TMP_Text>();
            }
        }
    }
}