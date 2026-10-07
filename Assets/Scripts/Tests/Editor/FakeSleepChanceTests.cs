using System;
using System.Linq;
using Characters.Powers;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// How long an awake fake role stays awake must not depend on the host's framerate, and a fake Ugës must not
    /// give itself away by skipping its turn instantly (its only power, the Marque, is passive: a 0 s wait made
    /// its awakening layer end at once, while a real Ugës had stolen powers with a real wait).
    /// </summary>
    [Category("Awakening")]
    public class FakeSleepChanceTests
    {
        // Chance that a fake role is still awake after one second at a given framerate.
        private static double StillAwakeAfterOneSecond(int _fps)
        {
            double _perFrame = AwakeningState.FakeSleepChance(1f / _fps);
            return Math.Pow(1.0 - _perFrame, _fps);
        }

        [Test]
        public void PerFrameChance_MatchesTheOriginalTuningAt60Fps()
        {
            Assert.AreEqual(0.00045, AwakeningState.FakeSleepChance(1f / 60f), 0.000005,
                "At 60 fps the per-frame chance stays the value it was tuned with (0.00045).");
        }

        [Test]
        public void SleepChancePerSecond_IsTheSameAtEveryFramerate()
        {
            double _reference = StillAwakeAfterOneSecond(60);
            foreach (int _fps in new[] { 20, 30, 144, 240, 500 })
            {
                Assert.AreEqual(_reference, StillAwakeAfterOneSecond(_fps), 0.0001,
                    $"A fake role sleeps at the same rate per second at {_fps} fps as at 60 fps.");
            }
        }

        [Test]
        public void NoTimeElapsed_NoChanceToSleep()
        {
            Assert.AreEqual(0f, AwakeningState.FakeSleepChance(0f));
            Assert.AreEqual(0f, AwakeningState.FakeSleepChance(-1f));
        }

        [Test]
        public void MarqueHurluberluges_WaitsLikeAnActivePower()
        {
            Power _marque = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Powers/MarqueHurluberluges.prefab")
                .GetComponent<Power>();
            float _activeWait = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Powers" })
                .Select(_guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(_guid)))
                .Select(_go => _go != null ? _go.GetComponent<Power>() : null)
                .Where(_p => _p != null && !_p.isPassive)
                .Select(_p => _p.maxWaitTime)
                .DefaultIfEmpty(0f)
                .Max();

            Assert.IsTrue(_marque.isPassive, "The Marque itself stays a passive power.");
            Assert.Greater(_activeWait, 0f, "Active powers have a wait time.");
            Assert.AreEqual(_activeWait, _marque.maxWaitTime,
                "The Marque gives Ugës's awakening layer the same length as an active power, so a fake Ugës (or a real " +
                "one with nothing left to use) is not given away by an instant layer.");
        }
    }
}
