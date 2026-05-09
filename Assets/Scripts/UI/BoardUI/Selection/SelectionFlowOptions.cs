using System;
using FocusSystem;

namespace UI.BoardUI.Selection
{
    [Serializable]
    public class SelectionFlowOptions
    {
        public FocusType? focusType;
        public bool clearFocusOnFinish = true;
        public Action onCanceled;
    }
}
