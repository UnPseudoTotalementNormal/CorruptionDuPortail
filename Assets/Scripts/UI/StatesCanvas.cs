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
            Instance = this;
        }
    }
}