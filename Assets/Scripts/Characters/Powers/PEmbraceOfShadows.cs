using System;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power
    {
        public override bool CanUse()
        {
            return true;
        }

        public override void Use()
        {
            // Implement the logic for using the power here
            // For example, you might want to change the player's state or apply effects
            Debug.Log("Embrace of Shadows used!");
        }

        public override Power CopyPower()
        {
            Debug.Log("Power copied!");
            return this;
        }
    }
}
