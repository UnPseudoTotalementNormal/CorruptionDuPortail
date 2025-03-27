using UnityEngine;

namespace Characters.Powers
{
    //[CreateAssetMenu(fileName = "NewPower", menuName = "Characters/Power")]
    public abstract class Power : ScriptableObject
    {
        public float maxWaitTime;
        
        public abstract bool CanUse();
        public abstract void Use();
    }
}