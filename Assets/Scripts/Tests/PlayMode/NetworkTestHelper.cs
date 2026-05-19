using System;
using System.Collections;
using System.Linq;
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
    }
}
