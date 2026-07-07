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

        // Extra screen-space (pixel) offset added to the final tooltip position. Default (0,0).
        // Lets a trigger nudge its tooltip a bit without touching the edge-anchoring math.
        public Vector2 tooltipPixelOffset { get; }

        // Optional static rect the tooltip anchors to instead of the linked object's child bounding box.
        // Lets a trigger keep its tooltip pinned to a fixed slot while its visuals animate (move/scale/rotate).
        public RectTransform TooltipBoundsOverride { get; }
    }
}