#region

using Unity.Netcode;

#endregion

namespace UI
{
    public class StatesCanvas : NetworkBehaviour
    {
        public static StatesCanvas Instance { get; private set; }
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }
    }
}