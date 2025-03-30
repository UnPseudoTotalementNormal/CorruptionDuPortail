using System.Collections.Generic;
using Characters.Powers;
using UnityEngine;

namespace Characters
{
    [CreateAssetMenu(fileName = "NewRole", menuName = "Roles/Role")]
    public class RoleDataObject : ScriptableObject
    {
        [SerializeField] public Role role;
        
        public List<PowerDataObject> powers;
    }
}