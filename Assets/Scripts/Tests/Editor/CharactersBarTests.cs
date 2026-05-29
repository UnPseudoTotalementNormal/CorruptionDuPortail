using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEngine;
using Unity.Netcode;

namespace Tests.Editor
{
    public class CharactersBarTests
    {
        private CharactersBar _charactersBar;
        private AwakeningState _mockAwakeningState;

        [SetUp]
        public void Setup()
        {
            GameObject _go = new GameObject("CharactersBar");
            _charactersBar = _go.AddComponent<CharactersBar>();
            _mockAwakeningState = ScriptableObject.CreateInstance<AwakeningState>();
            _mockAwakeningState.awakeningOrder = new List<AwakeningLayerObject>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_charactersBar != null)
            {
                Object.DestroyImmediate(_charactersBar.gameObject);
            }
            if (_mockAwakeningState != null)
            {
                Object.DestroyImmediate(_mockAwakeningState);
            }
        }

        [Test]
        public void SortCharacters_OrdersByAwakeningLayer()
        {
            Role _roleA = new Role { roleName = "RoleA", roleID = RoleID.Abyss };
            Role _roleB = new Role { roleName = "RoleB", roleID = RoleID.Abyss };
            
            _mockAwakeningState.awakeningOrder.Add(new AwakeningLayerObject { 
                awakeningCharacters = new List<RoleDataObject> { 
                    CreateRoleDataObject(_roleA) 
                } 
            });
            _mockAwakeningState.awakeningOrder.Add(new AwakeningLayerObject { 
                awakeningCharacters = new List<RoleDataObject> { 
                    CreateRoleDataObject(_roleB) 
                } 
            });

            Character _charA = CreateCharacter(_roleA, 1);
            Character _charB = CreateCharacter(_roleB, 2);
            
            List<Character> _toSort = new List<Character> { _charB, _charA };
            
            var _sorted = _charactersBar.SortCharacters(_toSort, _mockAwakeningState).ToList();
            
            Assert.AreEqual(_charA, _sorted[0], "RoleA should be first (Layer 0)");
            Assert.AreEqual(_charB, _sorted[1], "RoleB should be second (Layer 1)");
        }

        [Test]
        public void SortCharacters_ResolvesTiesWithRoleID()
        {
            Role _roleA = new Role { roleName = "RoleA", roleID = RoleID.Abyss }; // 1042
            Role _roleB = new Role { roleName = "RoleB", roleID = RoleID.DrGloubi }; // 5871
            
            _mockAwakeningState.awakeningOrder.Add(new AwakeningLayerObject { 
                awakeningCharacters = new List<RoleDataObject> { 
                    CreateRoleDataObject(_roleA),
                    CreateRoleDataObject(_roleB)
                } 
            });

            Character _charA = CreateCharacter(_roleA, 1);
            Character _charB = CreateCharacter(_roleB, 2);
            
            List<Character> _toSort = new List<Character> { _charB, _charA };
            
            var _sorted = _charactersBar.SortCharacters(_toSort, _mockAwakeningState).ToList();
            
            Assert.AreEqual(_charA, _sorted[0], "RoleID 1042 should come before 5871");
            Assert.AreEqual(_charB, _sorted[1]);
        }

        [Test]
        public void SortCharacters_ResolvesTiesWithClientID()
        {
            Role _roleA1 = new Role { roleName = "RoleA", roleID = RoleID.Abyss };
            Role _roleA2 = new Role { roleName = "RoleA", roleID = RoleID.Abyss };
            
            _mockAwakeningState.awakeningOrder.Add(new AwakeningLayerObject { 
                awakeningCharacters = new List<RoleDataObject> { 
                    CreateRoleDataObject(_roleA1)
                } 
            });

            Character _char1 = CreateCharacter(_roleA1, 1);
            Character _char2 = CreateCharacter(_roleA2, 2);
            
            List<Character> _toSort = new List<Character> { _char2, _char1 };
            
            var _sorted = _charactersBar.SortCharacters(_toSort, _mockAwakeningState).ToList();
            
            Assert.AreEqual(_char1, _sorted[0], "ClientID 1 should come before 2");
            Assert.AreEqual(_char2, _sorted[1]);
        }

        [Test]
        public void SortCharacters_UnknownRolesAtEnd()
        {
            Role _roleKnown = new Role { roleName = "Known", roleID = RoleID.Abyss };
            Role _roleUnknown = new Role { roleName = "Unknown", roleID = RoleID.Abyss };
            
            _mockAwakeningState.awakeningOrder.Add(new AwakeningLayerObject { 
                awakeningCharacters = new List<RoleDataObject> { 
                    CreateRoleDataObject(_roleKnown)
                } 
            });

            Character _charKnown = CreateCharacter(_roleKnown, 1);
            Character _charUnknown = CreateCharacter(_roleUnknown, 2);
            
            List<Character> _toSort = new List<Character> { _charUnknown, _charKnown };
            
            var _sorted = _charactersBar.SortCharacters(_toSort, _mockAwakeningState).ToList();
            
            Assert.AreEqual(_charKnown, _sorted[0]);
            Assert.AreEqual(_charUnknown, _sorted[1], "Unknown role should be at the end");
        }

        private RoleDataObject CreateRoleDataObject(Role _role)
        {
            RoleDataObject _rdo = ScriptableObject.CreateInstance<RoleDataObject>();
            _rdo.role = _role;
            return _rdo;
        }

        private Character CreateCharacter(Role _role, ulong _clientId)
        {
            GameObject _go = new GameObject("Character");
            Character _character = _go.AddComponent<Character>();
            _character.role = _role;
            _character.ownerClientId = new NetworkVariable<ulong>(_clientId);
            
            return _character;
        }
    }
}
