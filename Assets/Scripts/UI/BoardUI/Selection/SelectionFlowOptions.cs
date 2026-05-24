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
        public string[] stepDescriptions;

        public string GetStepDescription(int _step) =>
            stepDescriptions != null && _step < stepDescriptions.Length
                ? stepDescriptions[_step]
                : null;
    }
}
