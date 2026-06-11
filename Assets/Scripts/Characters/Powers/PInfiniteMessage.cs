using System;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PInfiniteMessage : Power
    {
        private void Awake()
        {
            onPowerReparented += OnPowerReparented;
        }

        private void OnPowerReparented()
        {
            if (NetworkManager.IsServer)
            {
                characterManager.GetCharacter(ownerClientId.Value).messageLeft.Value = Int32.MaxValue;
            }
        }
    }
}