using System;
using System.Collections.Generic;
using GameLogic;
using MessageSystem;
using Unity.Netcode;
using UnityEngine;

namespace UI.MessageJournal
{
    // Live data source: projects MessageManager's replicated turnStats + revealedMessages into per-turn
    // views for the journal overlay, and re-notifies whenever either replicated list changes. Stats are
    // deduped by day (late-joiner NetworkList replay guard) and shown in ascending day order.
    public class GameMessageJournalDataSource : MonoBehaviour, IMessageJournalDataSource
    {
        public event Action OnChanged;

        private MessageManager _messageManager;
        private bool _subscribed;

        // Unity-alive AND network-spawned: after despawn the NetworkLists are disposed, so reading them would
        // throw ObjectDisposedException — guard every list access on this.
        private bool ManagerAlive => _messageManager != null && _messageManager.IsSpawned;

        private void OnEnable() => TryResolveAndSubscribe();

        private void Start() => TryResolveAndSubscribe();

        private void OnDisable() => Unsubscribe();

        private void TryResolveAndSubscribe()
        {
            if (_subscribed && ManagerAlive)
            {
                return;
            }
            if (_subscribed) // subscribed but the manager despawned (scene reload / rematch) — drop the stale bind, rebind
            {
                Unsubscribe();
            }
            if (NetworkManager.Singleton == null)
            {
                return;
            }
            MessageManager _resolved = CompositionRoot.For(NetworkManager.Singleton).MessageManager;
            if (_resolved == null || !_resolved.IsSpawned)
            {
                return; // not spawned yet — retried lazily from GetTurns / on next enable
            }
            _messageManager = _resolved;
            _messageManager.turnStats.OnListChanged += OnStatsChanged;
            _messageManager.revealedMessages.OnListChanged += OnRevealedChanged;
            _subscribed = true;
            // No OnChanged here: this can be called lazily from within GetTurns (a build), and notifying
            // mid-build would re-enter Rebuild. The caller (controller Open/init) already builds; later
            // NetworkList changes drive OnChanged.
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }
            _subscribed = false;
            if (_messageManager != null && _messageManager.IsSpawned) // only touch the lists while they're alive
            {
                _messageManager.turnStats.OnListChanged -= OnStatsChanged;
                _messageManager.revealedMessages.OnListChanged -= OnRevealedChanged;
            }
            _messageManager = null;
        }

        private void OnStatsChanged(NetworkListEvent<TurnStat> _event) => OnChanged?.Invoke();

        private void OnRevealedChanged(NetworkListEvent<MessageInfo> _event) => OnChanged?.Invoke();

        public IReadOnlyList<JournalTurnView> GetTurns()
        {
            if (!_subscribed || !ManagerAlive)
            {
                TryResolveAndSubscribe(); // lazy resolve after a late network spawn, or rebind after a respawn
            }
            if (!ManagerAlive)
            {
                return Array.Empty<JournalTurnView>(); // no manager, or it despawned (NetworkLists disposed)
            }

            // NetworkList<T> exposes an enumerator (foreach) but does not implement IEnumerable<T>, so
            // materialize before handing it to the pure dedup helper.
            List<TurnStat> _rawStats = new();
            foreach (TurnStat _stat in _messageManager.turnStats)
            {
                _rawStats.Add(_stat);
            }
            List<TurnStat> _stats = TurnStatCalculator.DedupByDay(_rawStats);
            _stats.Sort((_a, _b) => _a.day.CompareTo(_b.day));

            Dictionary<int, List<string>> _messagesByDay = new();
            HashSet<(ulong sender, int day)> _seenMessages = new();
            foreach (MessageInfo _info in _messageManager.revealedMessages)
            {
                // A player sends at most one message per turn, so (sender, day) is unique — any repeat is the
                // known NGO NetworkList late-joiner replay (last same-tick entry delivered twice). Skip it.
                if (!_seenMessages.Add((_info.senderClientId, _info.day)))
                {
                    continue;
                }
                if (!_messagesByDay.TryGetValue(_info.day, out List<string> _dayMessages))
                {
                    _dayMessages = new List<string>();
                    _messagesByDay[_info.day] = _dayMessages;
                }
                _dayMessages.Add(_info.message.ToString());
            }

            List<JournalTurnView> _views = new(_stats.Count);
            foreach (TurnStat _stat in _stats)
            {
                _messagesByDay.TryGetValue(_stat.day, out List<string> _messages);
                _views.Add(new JournalTurnView(
                    _stat.day, _stat.corruptedCount, _stat.nonAnomalyTotal, _stat.robotTargetCount, _messages));
            }
            return _views;
        }
    }
}
