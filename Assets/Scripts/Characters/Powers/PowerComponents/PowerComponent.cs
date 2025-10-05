using System;
using Unity.Netcode;
using UnityEngine.Assertions;

namespace Characters.Powers.PowerComponents
{
    public abstract class PowerComponent : NetworkBehaviour
    {
        protected Power power;
        protected Character ownerCharacter => GameLogic.GameManager.instance.characterManager.GetCharacter(ownerClientId, false);
        protected ulong ownerClientId => power.ownerClientId.Value;
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