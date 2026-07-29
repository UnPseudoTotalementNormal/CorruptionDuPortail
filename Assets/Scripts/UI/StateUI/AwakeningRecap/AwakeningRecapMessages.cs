using MessageSystem;
using Unity.Netcode;
using UI.MessageJournal;
using UnityEngine;

namespace UI.Components
{
    // Message-journal refonte: this recap event no longer renders its own message list. It now DRIVES the
    // shared MessageJournalController overlay (the unified per-turn journal). The legacy viewing DURATION is
    // preserved verbatim (EvaluateDuration = baseDuration + per-message dwell), so the awakening reveal stays
    // paced and non-skippable exactly as before — only the visual moved to the journal overlay. Server-side it
    // still reveals the turn's messages (messagesToReveal -> revealedMessages); the overlay then shows the
    // newest turn writing in. The old serialized UI fields on this component's prefab are now unused.
    public class AwakeningRecapMessages : AwakeningRecapEventComponent
    {
        [Header("Timing Configuration (drives the reveal hold time — keep in sync with the legacy pacing)")]
        public float baseTimePerMessage = 2f;
        public float timePerCharacter = 0.05f;
        public float minMessageDisplayTime = 3f;
        public float maxMessageDisplayTime = 10f;

        private MessageJournalController _journal;

        private float CalculateMessageDisplayTime(string _message)
        {
            float _calculatedTime = baseTimePerMessage + (_message.Length * timePerCharacter);
            return Mathf.Clamp(_calculatedTime, minMessageDisplayTime, maxMessageDisplayTime);
        }

        public override float EvaluateDuration()
        {
            float _totalTime = baseDuration;
            foreach (var _messageInfo in MessageManager.instance.messagesToReveal)
            {
                _totalTime += CalculateMessageDisplayTime(_messageInfo.message.ToString());
            }
            return _totalTime;
        }

        public override void ShowEvent()
        {
            // Server records this turn's stat (corruption + Robot targeting) and reveals this turn's messages
            // into the replicated archive; the journal (reading turnStats + revealedMessages) then shows them.
            // Record BEFORE reveal so the turn's stat exists when the panel rebuilds. Do NOT call
            // base.ShowEvent — the old CanvasGroup stays hidden.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                MessageManager.instance.RecordCurrentTurnStat();
                MessageManager.instance.RevealAllMessage();
            }
            EnsureJournal();
            _journal?.OpenForReveal();
        }

        public override void HideEvent()
        {
            EnsureJournal();
            _journal?.Close();
        }

        private void EnsureJournal()
        {
            if (_journal == null)
            {
                _journal = FindFirstObjectByType<MessageJournalController>();
            }
        }
    }
}
