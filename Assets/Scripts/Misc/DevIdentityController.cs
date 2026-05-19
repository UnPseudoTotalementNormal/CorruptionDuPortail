using UnityEngine;
using Unity.Netcode;
using Characters;
using System.Collections.Generic;
using System.Linq;

namespace Misc
{
    public class DevIdentityController : MonoBehaviour
    {
        private void Update()
        {
            // Only allow in Editor or Debug build
            #if !UNITY_EDITOR && !DEBUG
            return;
            #endif

            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    CharacterManager.instance.SpawnSimulatedPlayer();
                    Debug.Log("Dev: Spawned Simulated Player");
                }
                else
                {
                    Debug.LogWarning("Dev: Only Server/Host can spawn players.");
                }
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                CycleIdentity(1);
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                CycleIdentity(-1);
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                CharacterManager.instance.SetPossessedIdentity(null);
                Debug.Log("Dev: Reset to original Identity");
            }
        }

        private void CycleIdentity(int direction)
        {
            var _characters = CharacterManager.instance.GetCharacters(false).Where(c => !c.isFake).ToList();
            if (_characters.Count <= 1) return;

            ulong _currentId = CharacterManager.instance.GetLocalClientId();
            int _currentIndex = _characters.FindIndex(c => c.ownerClientId.Value == _currentId);

            int _nextIndex = (_currentIndex + direction) % _characters.Count;
            if (_nextIndex < 0) _nextIndex = _characters.Count - 1;

            ulong _nextId = _characters[_nextIndex].ownerClientId.Value;
            
            // If the next ID is the same as host's actual ID, we can treat it as null (reset)
            if (_nextId == NetworkManager.Singleton.LocalClientId)
            {
                CharacterManager.instance.SetPossessedIdentity(null);
            }
            else
            {
                CharacterManager.instance.SetPossessedIdentity(_nextId);
            }

            Debug.Log($"Dev: Now possessing Character with ID {_nextId} ({_characters[_nextIndex].GetRole()?.roleName ?? "No Role"})");
        }
    }
}
