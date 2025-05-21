#region

using System;
using System.Collections.Generic;
using Characters.Powers;
using UnityEngine;

#endregion

namespace Characters
{
    [CreateAssetMenu(fileName = "NewRole", menuName = "Roles/Role")]
    public class RoleDataObject : ScriptableObject, ICloneable
    {
        [SerializeField] public Role role;
        
        public List<PowerDataObject> powers;
        
        public object Clone()
        {
            var _clone = CreateInstance<RoleDataObject>();
            _clone.role = this.role.CopyRole();
            _clone.powers = new List<PowerDataObject>(this.powers);
            return _clone;
        }
    }
}