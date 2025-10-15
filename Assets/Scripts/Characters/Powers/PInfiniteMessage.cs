using System;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PInfiniteMessage : Power
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CharacterManager.instance.GetCharacter(ownerClientId.Value).messageLeft.Value = Int32.MaxValue;
        }
    }
}