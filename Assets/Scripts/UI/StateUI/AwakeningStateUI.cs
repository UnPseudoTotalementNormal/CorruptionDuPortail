using GameLogic;
using TMPro;

namespace UI
{
    public class AwakeningStateUI : StateUI
    {
        // Kept hidden: the night parchment (NightTurnBanner, board task T21) now says "Vous vous éveillez" instead of
        // this 3 s text.
        public TMP_Text awakeningHelpText;

        protected override void OnStateStart()
        {
            base.OnStateStart();
            if (awakeningHelpText)
            {
                awakeningHelpText.alpha = 0;
            }
        }
    }
}
