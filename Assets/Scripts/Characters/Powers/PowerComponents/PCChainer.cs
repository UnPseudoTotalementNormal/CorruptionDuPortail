using Characters.Powers.Interfaces;
using Unity.Netcode;

namespace Characters.Powers.PowerComponents
{
    public class PCChainer : PowerComponent
    {
        public NetworkVariable<int> currentChain = new(0);
        public int maxChain = 2;
        
        protected override void Init()
        {
            if (power is not IFailablePower _failablePower)
            {
                throw new System.Exception("PCChainer can only be used with powers that implement IFailablePower");
            }
            
            if (!NetworkManager.IsServer)
            {
                return;
            }
            
            ownerCharacter.onCharacterAwakened += () => currentChain.Value = 0;
            _failablePower.onPowerSuccessful += () =>
            {
                if (currentChain.Value < maxChain)
                {
                    currentChain.Value++;
                }
            };
            _failablePower.onPowerFailed += () =>
            {
                power.powerUseLeft.Value = 0;
            };
        }
    }
}