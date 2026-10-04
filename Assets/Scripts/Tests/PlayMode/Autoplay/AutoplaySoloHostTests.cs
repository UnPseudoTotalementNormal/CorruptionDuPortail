using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autoplay;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Autoplay
{
    /// <summary>
    /// Autoplay T1 — plays a WHOLE game on the real GameScene in one process: the host (client 0) plus 7 simulated bots
    /// (ids &gt;= 100), all driven by <see cref="AutoplayDriver"/> through the same code paths a human uses (see
    /// <see cref="AutoplaySession"/>, shared with the dev-build bootstrap). Accelerated with Time.timeScale; NGO ticks
    /// stay real-time.
    /// <para>
    /// Output under <c>&lt;project&gt;/AutoplayRuns/&lt;stamp&gt;-&lt;scenario&gt;-seed&lt;n&gt;/</c>: <c>report.json</c>
    /// (read first), <c>events.ndjson</c> (full trace) and one state file per capture point (+ a PNG when rendering).
    /// </para>
    /// <para>
    /// Explicit: it loads Boot/Menu/GameScene and owns NetworkManager.Singleton for minutes, so it never runs inside the
    /// default PlayMode suite. Run it on demand: <c>tools/autoplay/unityctl.sh playmode AutoplaySoloHostTests</c>.
    /// The transport listens on UDP 7850 — never the 7777 / 7788 ports the other suites and playtests use.
    /// </para>
    /// </summary>
    [Category("Autoplay")]
    public class AutoplaySoloHostTests
    {
        private AutoplaySessionResult _result;
        private bool _previousIgnoreFailingMessages;

        [UnityTest]
        [Explicit("Plays a full 8-player game on the real GameScene (minutes). Run on demand with --include_explicit.")]
        [Timeout(900000)]
        public IEnumerator EightPlayers_HostPlusSevenBots_PlayToGameEnding()
        {
            // The game logs its own errors (no Steam, no UGS in a test). They are collected into the report instead of
            // failing the test on the first one, so a run always reaches its verdict.
            _previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            int _seed = Environment.TickCount & 0x7FFFFFFF;
            const string Scenario = "solohost-8p-randomvalid";
            var _config = new AutoplaySessionConfig
            {
                scenario = Scenario,
                seed = _seed,
                outputDirectory = AutoplaySession.NewRunDirectory(
                    Path.Combine(Application.dataPath, "..", "AutoplayRuns"), Scenario, _seed),
            };

            _result = new AutoplaySessionResult();
            yield return AutoplaySession.RunSoloHost(_config, _result);

            Assert.IsNull(_result.failure, $"Autoplay game did not finish: {_result.failure}. Report: {_config.outputDirectory}");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            if (_result?.driver != null)
            {
                _result.driver.End();
                Object.Destroy(_result.driver.gameObject);
            }

            var _networkManager = _result?.networkManager;
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
                yield return NetworkTestHelper.WaitUntilOrElapsed(() => !_networkManager.ShutdownInProgress, 10f, _ => { });
            }

            // The boot objects live in DontDestroyOnLoad: remove them so later tests start from a clean slate.
            foreach (DontDestroyOnLoadComponent _persistent in Object.FindObjectsByType<DontDestroyOnLoadComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.Destroy(_persistent.gameObject);
            }
            if (_networkManager != null)
            {
                Object.Destroy(_networkManager.gameObject);
            }
            _result = null;
            yield return null;

            FieldInfo _existingIds = typeof(DontDestroyOnLoadComponent).GetField("existingIds", BindingFlags.NonPublic | BindingFlags.Static);
            (_existingIds?.GetValue(null) as HashSet<int>)?.Clear();
            GameManager.ResetSessionStatics();
            CompositionRoot.ResetSessionStatics();
            Infra.TestStaticReset.ResetAll();

            Scene _empty = SceneManager.CreateScene($"AutoplayEmpty-{Guid.NewGuid():N}");
            SceneManager.SetActiveScene(_empty);
            for (int _i = SceneManager.sceneCount - 1; _i >= 0; _i--)
            {
                Scene _scene = SceneManager.GetSceneAt(_i);
                if (_scene != _empty && _scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(_scene);
                }
            }

            LogAssert.ignoreFailingMessages = _previousIgnoreFailingMessages;
        }
    }
}
