using System;
using Characters;
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
        /// <summary>Values for the {0}, {1}... of the step descriptions (a player's pseudo...).</summary>
        public object[] stepDescriptionArgs;

        public Character pinnedCharacter;
        public Role pinnedRole;

        public string GetStepDescription(int _step) =>
            stepDescriptions != null && _step < stepDescriptions.Length
                ? stepDescriptions[_step]
                : null;
    }
}

