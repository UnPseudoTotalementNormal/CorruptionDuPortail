using System;
using System.Collections;
using System.Linq;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Tests.PlayMode
{
    /// <summary>
    /// Test helpers that bound Netcode spawn waits with a timeout so a silent
    /// NGO failure (e.g. a UDP port left open by a previous test) fails the test
    /// instead of hanging the CI main thread forever.
    /// </summary>
    public static class NetworkTestHelper
    {
        /// <summary>
        /// Yields until <paramref name="netObj"/> is spawned, or fails the test
        /// once <paramref name="timeoutSeconds"/> of play time has elapsed.
        /// </summary>
        public static IEnumerator WaitUntilSpawnedOrTimeout(NetworkObject netObj, float timeoutSeconds = 5f)
        {
            yield return WaitUntilOrTimeout(
                () => netObj != null && netObj.IsSpawned,
                timeoutSeconds,
                $"NetworkObject '{(netObj != null ? netObj.name : "null")}' was not spawned within {timeoutSeconds}s.");
        }

        /// <summary>
        /// Yields until <paramref name="netBehaviour"/> is spawned, or fails the
        /// test once <paramref name="timeoutSeconds"/> of play time has elapsed.
        /// </summary>
        public static IEnumerator WaitUntilSpawnedOrTimeout(NetworkBehaviour netBehaviour, float timeoutSeconds = 5f)
        {
            yield return WaitUntilOrTimeout(
                () => netBehaviour != null && netBehaviour.IsSpawned,
                timeoutSeconds,
                $"NetworkBehaviour '{(netBehaviour != null ? netBehaviour.name : "null")}' was not spawned within {timeoutSeconds}s.");
        }

        /// <summary>
        /// Yields until every behaviour is spawned, or fails the test once
        /// <paramref name="timeoutSeconds"/> of play time has elapsed.
        /// </summary>
        public static IEnumerator WaitUntilAllSpawnedOrTimeout(float timeoutSeconds, params NetworkBehaviour[] netBehaviours)
        {
            yield return WaitUntilOrTimeout(
                () => netBehaviours != null && netBehaviours.All(nb => nb != null && nb.IsSpawned),
                timeoutSeconds,
                $"Not all NetworkBehaviours were spawned within {timeoutSeconds}s.");
        }

        /// <summary>
        /// Yields until every behaviour is spawned, or fails the test after the
        /// default 5s timeout.
        /// </summary>
        public static IEnumerator WaitUntilAllSpawnedOrTimeout(params NetworkBehaviour[] netBehaviours)
        {
            yield return WaitUntilAllSpawnedOrTimeout(5f, netBehaviours);
        }

        /// <summary>
        /// Story 7.5 — production resolves GameInfoRevealer through
        /// <c>CompositionRoot.For(nm).GameInfoRevealer</c>, backed by a scene-placed CompositionRoot.
        /// PlayMode harnesses have no scene root, so they register one here: created inactive, wired
        /// via reflection, then activated so the Awake non-null asserts pass and it registers in the
        /// per-NetworkManager registry. Returns the GameObject so the caller can <c>Destroy</c> it in
        /// teardown (its OnDestroy value-scans itself out of the registry).
        /// </summary>
        public static GameObject RegisterCompositionRoot(GameManager gameManager, CharacterManager characterManager, GameInfoRevealer gameInfoRevealer)
        {
            // Code-review hardening (7.5): CompositionRoot.Awake binds to NetworkManager.Singleton and
            // only registers when it is non-null. Calling this before StartHost would silently produce a
            // root that resolves for nobody — fail loudly on that contract violation instead.
            Assert.IsNotNull(NetworkManager.Singleton, "RegisterCompositionRoot must be called after StartHost — CompositionRoot binds to NetworkManager.Singleton at Awake.");
            var go = new GameObject("CompositionRoot");
            go.SetActive(false);
            var root = go.AddComponent<CompositionRoot>();
            ReflectionHelper.SetPrivateField(root, "gameManager", gameManager);
            ReflectionHelper.SetPrivateField(root, "characterManager", characterManager);
            ReflectionHelper.SetPrivateField(root, "gameInfoRevealer", gameInfoRevealer);
            go.SetActive(true);
            return go;
        }

        /// <summary>
        /// Generic bounded wait: yields until <paramref name="condition"/> is
        /// true, or fails the test with <paramref name="failureMessage"/> once
        /// the timeout elapses. Uses <see cref="Time.deltaTime"/> accumulation
        /// so it never blocks the main thread indefinitely.
        /// </summary>
        public static IEnumerator WaitUntilOrTimeout(Func<bool> condition, float timeoutSeconds, string failureMessage)
        {
            float elapsed = 0f;
            while (!condition())
            {
                if (elapsed >= timeoutSeconds)
                {
                    Assert.Fail(failureMessage);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Quiescence primitive (spec R8): yields until <paramref name="condition"/> has held true for
        /// <paramref name="stableFrames"/> CONSECUTIVE frames — i.e. the observed state reached its value AND
        /// stopped changing — or fails after the timeout. Use for "assert client state after quiescence" so a
        /// test never reads a mid-replication tick. This is the single shared definition of "settled".
        /// </summary>
        public static IEnumerator WaitUntilStableOrTimeout(Func<bool> condition, float timeoutSeconds, int stableFrames = 3, string failureMessage = null)
        {
            float elapsed = 0f;
            int stable = 0;
            while (stable < stableFrames)
            {
                stable = condition() ? stable + 1 : 0;
                if (stable >= stableFrames)
                {
                    yield break;
                }
                if (elapsed >= timeoutSeconds)
                {
                    Assert.Fail(failureMessage ?? $"Condition never held stable for {stableFrames} consecutive frames within {timeoutSeconds}s.");
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Drains <paramref name="ticks"/> frames so both NetworkManagers flush pending replication.
        /// Prefer <see cref="WaitUntilStableOrTimeout"/> when the settled condition can be expressed.</summary>
        public static IEnumerator WaitForTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                yield return null;
            }
        }
    }
}
