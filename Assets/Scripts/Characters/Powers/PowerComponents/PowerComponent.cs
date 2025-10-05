using System;
using Unity.Netcode;
using UnityEngine.Assertions;

namespace Characters.Powers.PowerComponents
{
    public abstract class PowerComponent : NetworkBehaviour
    {
        private Power power;
        private Character ownerCharacter => GameLogic.GameManager.instance.characterManager.GetCharacter(ownerClientId, false);
        private ulong ownerClientId => power.ownerClientId;
        protected void Awake()
        {
            power = GetComponent<Power>();
            Assert.IsNotNull(power, "PowerComponent must be attached to a GameObject with a Power component");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Init();
        }

        protected abstract void Init();
    }
}