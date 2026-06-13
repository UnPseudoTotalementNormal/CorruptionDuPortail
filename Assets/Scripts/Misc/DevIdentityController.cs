using UnityEngine;
using Unity.Netcode;
using Characters;
using GameLogic;
using System.Collections.Generic;
using System.Linq;

namespace Misc
{
    // Story 12.3: debug F-key controller — reroutes its CharacterManager God-Object façade reads onto the
    // sanctioned CompositionRoot.For(Singleton) (verify-don't-force; an Update-driven debug MonoBehaviour has
    // no lane-A/C seam, so it resolves through the one allowed static).
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
                    CompositionRoot.For(NetworkManager.Singleton).CharacterManager.SpawnSimulatedPlayer();
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
                CompositionRoot.For(NetworkManager.Singleton).CharacterManager.SetPossessedIdentity(null);
                Debug.Log("Dev: Reset to original Identity");
            }
        }

        private void CycleIdentity(int direction)
        {
            var _characters = CompositionRoot.For(NetworkManager.Singleton).CharacterManager.GetCharacters(false).Where(c => !c.isFake).ToList();
            if (_characters.Count <= 1) return;

            ulong _currentId = CompositionRoot.For(NetworkManager.Singleton).CharacterManager.GetLocalClientId();
            int _currentIndex = _characters.FindIndex(c => c.ownerClientId.Value == _currentId);

            int _nextIndex = (_currentIndex + direction) % _characters.Count;
            if (_nextIndex < 0) _nextIndex = _characters.Count - 1;

            ulong _nextId = _characters[_nextIndex].ownerClientId.Value;
            
            // If the next ID is the same as host's actual ID, we can treat it as null (reset)
            if (_nextId == NetworkManager.Singleton.LocalClientId)
            {
                CompositionRoot.For(NetworkManager.Singleton).CharacterManager.SetPossessedIdentity(null);
            }
            else
            {
                CompositionRoot.For(NetworkManager.Singleton).CharacterManager.SetPossessedIdentity(_nextId);
            }

            Debug.Log($"Dev: Now possessing Character with ID {_nextId} ({_characters[_nextIndex].GetRole()?.roleName ?? "No Role"})");
        }
    }
}
