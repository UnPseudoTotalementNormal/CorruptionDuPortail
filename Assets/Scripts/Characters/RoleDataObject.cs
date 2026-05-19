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
        
        public List<Power> powers;
        
        public object Clone()
        {
            var _clone = CreateInstance<RoleDataObject>();
            _clone.role = (Role)role.Clone();
            _clone.powers = new List<Power>(powers);
            return _clone;
        }
    }
}