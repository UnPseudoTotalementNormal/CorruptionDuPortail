using System;
using UnityEngine;

namespace TooltipSystem
{
    public interface ITooltipTrigger
    {
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;
        public Vector2 tooltipOffsetDirection { get; set; }
    }
}