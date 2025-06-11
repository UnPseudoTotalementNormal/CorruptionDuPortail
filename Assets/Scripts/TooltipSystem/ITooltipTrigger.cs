using System;

namespace TooltipSystem
{
    public interface ITooltipTrigger
    {
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;
    }
}