using System;

namespace TooltipSystem
{
    public interface ITooltipTrigger
    {
        public event Action onTooltipTryClose;
        public event Action onTooltipForceClose;
    }
}