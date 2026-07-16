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

        private void OnEnable() => TryResolveAndSubscribe();

        private void Start() => TryResolveAndSubscribe();

        private void OnDisable() => Unsubscribe();

        private void TryResolveAndSubscribe()
        {
            if (_subscribed)
            {
                return;
            }
            if (NetworkManager.Singleton == null)
            {
                return;
            }
            _messageManager = CompositionRoot.For(NetworkManager.Singleton).MessageManager;
            if (_messageManager == null)
            {
                return; // manager not spawned yet — retried from the other entry point / on next enable
            }
            _messageManager.turnStats.OnListChanged += OnStatsChanged;
            _messageManager.revealedMessages.OnListChanged += OnRevealedChanged;
            _subscribed = true;
            // No OnChanged here: this can be called lazily from within GetTurns (a build), and notifying
            // mid-build would re-enter Rebuild. The caller (controller Open/init) already builds; later
            // NetworkList changes drive OnChanged.
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _messageManager == null)
            {
                return;
            }
            _messageManager.turnStats.OnListChanged -= OnStatsChanged;
            _messageManager.revealedMessages.OnListChanged -= OnRevealedChanged;
            _subscribed = false;
        }

        private void OnStatsChanged(NetworkListEvent<TurnStat> _event) => OnChanged?.Invoke();

        private void OnRevealedChanged(NetworkListEvent<MessageInfo> _event) => OnChanged?.Invoke();

        public IReadOnlyList<JournalTurnView> GetTurns()
        {
            if (!_subscribed)
            {
                TryResolveAndSubscribe(); // lazy: MessageManager may network-spawn after this component's Start
            }
            if (_messageManager == null)
            {
                return Array.Empty<JournalTurnView>();
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
            foreach (MessageInfo _info in _messageManager.revealedMessages)
            {
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
