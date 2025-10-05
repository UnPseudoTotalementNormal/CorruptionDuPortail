using Unity.Netcode;

namespace Characters.Powers.PowerComponents
{
    public class PCChainer : PowerComponent
    {
        public NetworkVariable<int> currentChain = new(0);
        public int maxChain = 2;
        
        protected override void Init()
        {
            
        }
    }
}