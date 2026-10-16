#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using UI;
using UnityEngine;

namespace Autoplay
{
    /// <summary>
    /// Board task T21 (tell the player when it is their turn at night): probe <c>-autoplay-turn-captures</c> saves this
    /// process's screen, clean, the moment its own seat wakes (<c>turn-mine-d&lt;day&gt;-0.jpg</c>, then 1.5 s later
    /// <c>-1</c>, then <c>-burn</c> as the night parchment burns; the bot waits meanwhile), when it falls asleep again
    /// (<c>turn-sleep-d&lt;day&gt;-0</c> / <c>-burn</c>), and while it sleeps through another layer
    /// (<c>turn-other-d&lt;day&gt;-before.jpg</c> before its own turn, <c>-after</c> after it). Journal <c>turn.capture</c>
    /// with the parchment's text (<see cref="NightTurnBanner"/>).
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private bool turnLocalAwake;
        private int turnOtherCapturedDay = -1;
        private int turnOtherCapturedLayer = -1;
        private int turnMineDay = -1;

        private void UpdateTurnCaptures(GameState _state)
        {
            if (!options.turnCaptures)
            {
                return;
            }
            if (!networkManager.IsServer && Screen.width < 1600)
            {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed); // clients start at 640 x 360: full-size captures
            }
            // This process's own seat, not GetLocalCharacter: on the host it follows the bot being possessed.
            Character _local = characterManager.GetCharacters(false)
                .FirstOrDefault(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            if (_local == null)
            {
                return;
            }
            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            int _day = _gameManager != null ? _gameManager.currentDay : 0;
            bool _awake = _local.isAwakened.Value;
            if (_awake && !turnLocalAwake)
            {
                turnMineDay = _day;
                StartCoroutine(TurnMineCaptures(_day));
            }
            else if (!_awake && turnLocalAwake)
            {
                // Checked in every phase: the last layer's seats fall asleep as the night ends.
                StartCoroutine(TurnSleepCaptures(_day));
            }
            else if (_state is AwakeningState _awakening && !_awake && turnOtherCapturedDay != _day
                     && _awakening.currentAwakeningIndex != turnOtherCapturedLayer && Time.time - stateEnterGameTime > 2f)
            {
                turnOtherCapturedDay = _day;
                turnOtherCapturedLayer = _awakening.currentAwakeningIndex;
                StartCoroutine(SaveTurn($"turn-other-d{_day}-{(turnMineDay == _day ? "after" : "before")}"));
            }
            turnLocalAwake = _awake;
        }

        // The night parchment (NightTurnBanner): unrolled, whole, burning.
        private IEnumerator TurnMineCaptures(int _day)
        {
            inputBusy = true;
            // Host: show the host's own seat, not the bot it possessed last (the cards re-turn on a switch).
            Possess(networkManager.LocalClientId);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return SaveTurn($"turn-mine-d{_day}-0");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return SaveTurn($"turn-mine-d{_day}-1");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return SaveTurn($"turn-mine-d{_day}-burn");
            inputBusy = false;
            // A second "other" capture once this seat sleeps again (a later layer of the same night).
            turnOtherCapturedDay = -1;
        }

        private IEnumerator TurnSleepCaptures(int _day)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            yield return SaveTurn($"turn-sleep-d{_day}-0");
            yield return new WaitForSecondsRealtime(1.4f);
            yield return SaveTurn($"turn-sleep-d{_day}-burn");
        }

        private IEnumerator SaveTurn(string _name)
        {
            NightTurnBanner _banner = FindFirstObjectByType<NightTurnBanner>();
            Journal.Record("turn.capture", $"{_name} banner={(_banner != null ? _banner.ShownText ?? "none" : "missing")}");
            yield return SaveClean(_name);
        }
    }
}
#endif
