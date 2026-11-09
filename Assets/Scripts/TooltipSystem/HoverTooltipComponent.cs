using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    /// <summary>
    /// Something that shows a bubble when the pointer rests on it (power object, counter, cork portrait). It only
    /// reports the pointer to <see cref="TooltipManager"/>, which decides when the bubble opens, closes and where it goes.
    /// A trigger with neither title nor description never opens one.
    /// </summary>
    public class HoverTooltipComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private string tooltipTitle;
        [SerializeField] private string tooltipDescription;
        [field:SerializeField] public Vector2 tooltipOffsetDirection { get; set; } = Vector2.up;

        [Tooltip("Extra pixel offset added to the final tooltip position. Leave at (0,0) unless the " +
                 "tooltip needs a manual nudge.")]
        [field:SerializeField] public Vector2 tooltipPixelOffset { get; private set; } = Vector2.zero;

        [Tooltip("Optional. If set, the tooltip anchors to this static rect instead of the object's animated " +
                 "child bounds — keeps it pinned to the resting slot when the visual moves/zooms on hover.")]
        [SerializeField] private RectTransform tooltipBoundsOverride;
        public RectTransform TooltipBoundsOverride => tooltipBoundsOverride;

        // Read when the bubble opens, so a text built from live game state ({var:…}) is never stale.
        private Func<string> descriptionSource;

        public string Title => tooltipTitle;
        public string Description => descriptionSource != null ? descriptionSource() : tooltipDescription;
        public bool HasContent => !string.IsNullOrEmpty(tooltipTitle) || !string.IsNullOrEmpty(Description);

        public void SetTooltipTitle(string _title)
        {
            tooltipTitle = _title;
            NotifyChanged();
        }

        public void SetTooltipDescription(string _description)
        {
            tooltipDescription = _description;
            descriptionSource = null;
            NotifyChanged();
        }

        public void SetTooltipDescription(Func<string> _source)
        {
            descriptionSource = _source;
            NotifyChanged();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            if (TooltipManager.instance != null) TooltipManager.instance.PointerEntered(this);
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            if (TooltipManager.instance != null) TooltipManager.instance.PointerExited(this);
        }

        private void OnDisable()
        {
            if (TooltipManager.instance != null) TooltipManager.instance.TriggerGone(this);
        }

        private void NotifyChanged()
        {
            if (TooltipManager.instance != null) TooltipManager.instance.ContentChanged(this);
        }
    }
}
