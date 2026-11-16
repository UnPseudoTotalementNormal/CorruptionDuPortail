using Localization;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;

namespace UI.Components
{
    public class AwakeningRecapCorruption : AwakeningRecapEventComponent
    {
        public TMP_Text corruptionText;
        
        public override void SetupEvent(AwakeningRecapEvent _recapEvent)
        {
            base.SetupEvent(_recapEvent);
        }

        public override void ShowEvent()
        {
            base.ShowEvent();

            // Story 12.2: read characters off the slice pushed by the AwakeningRecapStateUI host instead of the façade.
            var _characters = CharacterQuery.GetCharacters(false)
                .Where(_c => !_c.isFake && _c.role.factionType != FactionType.anomaly);

            int _corruptedAmount = _characters.Count(_c => _c.isCorrupted.Value);
            int _totalAmount = _characters.Count();
            corruptionText.SetLoc("<color=red>{0} sur {1}</color> non-anomalies ont été corrompues.", _corruptedAmount, _totalAmount);
        }
        
        public override void HideEvent()
        {
            base.HideEvent();
        }
    }
}