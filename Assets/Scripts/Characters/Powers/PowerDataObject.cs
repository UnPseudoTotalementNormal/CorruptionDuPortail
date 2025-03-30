using UnityEngine;

namespace Characters.Powers
{
    [CreateAssetMenu(fileName = "NewPower", menuName = "Roles/Power")]
    public class PowerDataObject : ScriptableObject
    { 
        [SerializeReference]
        public Power power;
    }
}
