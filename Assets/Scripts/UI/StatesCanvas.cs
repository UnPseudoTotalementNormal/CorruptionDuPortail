#region

using Unity.Netcode;

#endregion

namespace UI
{
    public class StatesCanvas : NetworkBehaviour
    {
        // Story 10.4 (Epic 10 / D4): the sole consumer (GameState.OnStateCreated) now reads an injected
        // statesCanvas (lane-B push from the composition root). Guard #1 locks this global; the static
        // backs only the composition-root accessor that serves it. // recorded: dies in 12.3
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