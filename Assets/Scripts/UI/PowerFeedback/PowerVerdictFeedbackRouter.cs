using System.Collections.Generic;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain.Powers;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace UI.PowerFeedback
{
    /// <summary>
    /// Bridges the gameplay-side <see cref="Power.onPowerVerdict"/> channel to the presentation side.
    /// Permanent infrastructure: it holds the "which power / is it mine / when" logic so that no feedback
    /// implementation ever has to know about NGO. See <see cref="IPowerVerdictFeedback"/> for the swap point.
    ///
    /// WHY IT LISTENS TO EVERY POWER RATHER THAN FILTERING AT SPAWN: on a client, a power's replicated
    /// ownerClientId is not guaranteed to have arrived when onPowerSpawned fires, so filtering there would
    /// be a race. Subscribing to all of them and testing ownership at VERDICT time is race-free — the grade
    /// always lands long after the spawn handshake.
    /// </summary>
    public class PowerVerdictFeedbackRouter : MonoBehaviour
    {
        // Serialized as MonoBehaviour[] because Unity cannot serialize interface references directly; each
        // entry is asserted to implement IPowerVerdictFeedback at startup, so a mis-wire fails loudly in the
        // editor instead of silently swallowing every verdict.
        [SerializeField] private MonoBehaviour[] feedbackTargets;

        private readonly List<IPowerVerdictFeedback> _feedbacks = new();
        private readonly Dictionary<Power, System.Action<PowerVerdict>> _handlers = new();
        private CharacterManager _characterManager;

        private void Awake()
        {
            if (feedbackTargets == null) return;
            foreach (MonoBehaviour _target in feedbackTargets)
            {
                if (!_target) continue;
                var _feedback = _target as IPowerVerdictFeedback;
                Assert.IsNotNull(_feedback,
                    $"PowerVerdictFeedbackRouter.feedbackTargets contains '{_target.GetType().Name}', which does " +
                    "not implement IPowerVerdictFeedback — wire it in GameScene.");
                if (_feedback != null) _feedbacks.Add(_feedback);
            }
        }

        // Subscribe unconditionally on every peer (mirrors PowerManager.Start): the handler itself decides
        // whether the verdict belongs to this screen.
        private void Start() => Power.onPowerSpawned += OnPowerSpawned;

        private void OnDestroy()
        {
            Power.onPowerSpawned -= OnPowerSpawned;
            foreach (var _entry in _handlers)
            {
                if (_entry.Key) _entry.Key.onPowerVerdict -= _entry.Value;
            }
            _handlers.Clear();
        }

        private void OnPowerSpawned(Power _power)
        {
            if (!_power || _handlers.ContainsKey(_power)) return;

            System.Action<PowerVerdict> _handler = _verdict => OnVerdict(_power, _verdict);
            _handlers[_power] = _handler;
            _power.onPowerVerdict += _handler;
        }

        private void OnVerdict(Power _power, PowerVerdict _verdict)
        {
            if (_verdict == PowerVerdict.None || !_power) return;

            // STRICT local test, not IsLocalOrSimulated: on the host, a simulated bot's verdict is delivered
            // locally by GetSafeRpcTarget interception. Flashing the host's screen for a bot's power would be
            // wrong — and would leak that the bot failed. Same distinction PLackOfAffection draws.
            CharacterManager _characters = ResolveCharacterManager();
            if (_characters == null || _characters.GetLocalClientId() != _power.ownerClientId.Value) return;

            foreach (IPowerVerdictFeedback _feedback in _feedbacks)
            {
                _feedback.Play(_verdict);
            }
        }

        // Resolved lazily rather than in Awake: this router lives on a plain UI canvas, which may awake
        // before the NetworkManager has a composition root to hand out.
        private CharacterManager ResolveCharacterManager()
        {
            if (_characterManager) return _characterManager;
            if (!NetworkManager.Singleton) return null;
            _characterManager = CompositionRoot.For(NetworkManager.Singleton).CharacterManager;
            return _characterManager;
        }
    }
}
