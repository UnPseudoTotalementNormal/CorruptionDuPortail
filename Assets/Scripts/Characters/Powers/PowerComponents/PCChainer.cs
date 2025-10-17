using System;
using Characters.Powers.Interfaces;
using Unity.Collections;
using Unity.Netcode;

namespace Characters.Powers.PowerComponents
{
    public class PCChainer : PowerComponent
    {
        public NetworkVariable<int> currentChain = new(0);
        public int maxChain = 2;

        private void Reset()
        {
            componentName = "En chaîne";
            description = "Lorsque ce pouvoir réussi, il peut être réutilisé, jusqu'à {var:maxChain} fois.";
        }

        protected override void Init()
        {
            var _failablePower = power as IFailablePower;
            
            if (!NetworkManager.IsServer)
            {
                return;
            }
            
            ownerCharacter.onCharacterAwakened += OnCharacterAwakened;
            
            if (_failablePower == null)
            {
                power.onPowerUsedServer += OnPowerSuccessful;
                return;
            }
            
            _failablePower.onPowerSuccessful += OnPowerSuccessful;
            _failablePower.onPowerFailed += OnPowerFailed;
        }

        private void OnCharacterAwakened()
        {
            currentChain.Value = 0;
        }

        private void OnPowerSuccessful()
        {
            currentChain.Value++;
            if (currentChain.Value >= maxChain)
            {
                power.powerUseLeft.Value = 0;
            }
        }

        private void OnPowerFailed()
        {
            power.powerUseLeft.Value = 0;
        }
    }
}