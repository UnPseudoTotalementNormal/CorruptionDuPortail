using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Characters;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "RoleAttributionState", menuName = "GameStates/RoleAttributionState")]
    public class RoleAttributionState : GameState
    {
        public SerializedDictionary<Role, RoleAttributionSetting> roleAttributionDictionary = new();
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            Dictionary<Role, RoleAttributionSetting> _rolesToAttribute = roleAttributionDictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            //remove roles that have no roleToAttribute
            foreach (KeyValuePair<Role, RoleAttributionSetting> _roleToAttribute in _rolesToAttribute.ToList())
            {
                if (_roleToAttribute.Value.roleToAttribute <= 0)
                {
                    _rolesToAttribute.Remove(_roleToAttribute.Key);
                }
            }
            
            float _fakeRoleAmountToRemove = Mathf.Abs(gameManager.characters.Count - roleAttributionDictionary.Values.Sum(setting => setting.roleToAttribute));

            Dictionary<Role, RoleAttributionSetting> _fakeRoles = _rolesToAttribute
                .Where(_roleToAttribute => _roleToAttribute.Value.canBeFake)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            //remove fake roles from dictionary
            for (int i = 0; i < _fakeRoleAmountToRemove; i++)
            {
                GiveRandomRole(_fakeRoles, null, out Role _removedRole);
                if (_removedRole)
                {
                    _rolesToAttribute.Remove(_removedRole);
                }
            }

            //give random roles to character
            foreach (Character _character in gameManager.characters.ToList())
            {
                GiveRandomRole(_rolesToAttribute, _character, out Role _removedRole);
            }
        }

        private static void GiveRandomRole(Dictionary<Role, RoleAttributionSetting> _rolesToAttribute, Character _character, out Role _removedRole)
        {
            _removedRole = null;
            int _randomRoleIndex = Random.Range(0, _rolesToAttribute.Count);
            Role _randomRole = _rolesToAttribute.Keys.ToList()[_randomRoleIndex];
            RoleAttributionSetting _randomRoleSettings = _rolesToAttribute[_randomRole];

            if (_character != null)
            {
                _character.role = _randomRole;
            }
            _randomRoleSettings.roleToAttribute -= 1;
            if (_randomRoleSettings.roleToAttribute <= 0)
            {
                _rolesToAttribute.Remove(_randomRole);
                _removedRole = _randomRole;
            }
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }

    [Serializable]
    public class RoleAttributionSetting
    {
        [Range(0, 10)] public int roleToAttribute;
        public bool canBeFake = true;
    }
}