using Board;
using UnityEngine;

public class CardEffectBoolEnabler : CardEffectComponent
{
    [SerializeField] private GameObject enableOnTrue;
    [SerializeField] private GameObject enableOnFalse;
    
    public override void Initialize(Card _card, object _effectData = null)
    {
        base.Initialize(_card, _effectData);
        if (_effectData is not bool _boolValue)
        {
            Debug.LogError("CardEffectBoolEnabler: effectData is not a bool");
            return;
        }

        enableOnTrue.SetActive(_boolValue);
        enableOnFalse.SetActive(!_boolValue);
    }
}
