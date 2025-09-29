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

            var _characters = GameManager.instance.characterManager.GetCharacters(false)
                .Where(_c => !_c.isFake && _c.role.factionType != FactionType.anomaly);

            int _corruptedAmount = _characters.Count(_c => _c.isCorrupted.Value);
            int _totalAmount = _characters.Count();
            corruptionText.text = $"<color=red>{_corruptedAmount} sur {_totalAmount}</color> non-anomalies ont été corrompues.";
        }
        
        public override void HideEvent()
        {
            base.HideEvent();
        }
    }
}