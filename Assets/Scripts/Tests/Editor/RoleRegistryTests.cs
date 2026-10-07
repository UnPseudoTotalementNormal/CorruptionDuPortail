using Characters;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// A game that joins mid-game (fresh after a relaunch) can spawn characters before its role pool is loaded: their
    /// role is rebuilt when the registry learns the id, which it announces once per id.
    /// </summary>
    public class RoleRegistryTests
    {
        [Test]
        public void Register_AnnouncesANewRoleId_Once()
        {
            // No authored role uses such an id; unique per run (the registry is static and outlives a test).
            var _id = (RoleID)(400000 + Random.Range(0, 1000000));
            var _data = ScriptableObject.CreateInstance<RoleDataObject>();
            _data.role = new Role { roleID = _id, roleName = "TestRole-Registry" };
            int _announced = 0;
            bool _known;
            void Count(RoleID _registered)
            {
                if (_registered == _id)
                {
                    _announced++;
                }
            }

            RoleRegistry.onRegistered += Count;
            try
            {
                RoleRegistry.Register(_data);
                RoleRegistry.Register(_data);
                _known = RoleRegistry.TryGet(_id, out _);
            }
            finally
            {
                RoleRegistry.onRegistered -= Count;
                Object.DestroyImmediate(_data);
            }

            Assert.AreEqual(1, _announced, "A new id is announced once; registering it again is silent.");
            Assert.IsTrue(_known, "The id is now known.");
        }
    }
}
