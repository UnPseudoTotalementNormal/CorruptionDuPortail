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

        // Optional static rect the tooltip anchors to instead of the linked object's child bounding box.
        // Lets a trigger keep its tooltip pinned to a fixed slot while its visuals animate (move/scale/rotate).
        public RectTransform TooltipBoundsOverride { get; }
    }
}