using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.MessageJournal
{
    // Play-harness data source: canned per-turn data so the journal overlay renders without a live
    // networked game. Mirrors DemoLobbyRolesDataSource — Start() kicks the first build via OnChanged.
    public class DemoMessageJournalDataSource : MonoBehaviour, IMessageJournalDataSource
    {
        public event Action OnChanged;

        private readonly List<JournalTurnView> _turns = new()
        {
            new JournalTurnView(1, 1, 6, 2, new List<string> { "Uptn est sus" }),
            new JournalTurnView(2, 2, 6, 0, new List<string> { "Le messager est trop con", "Surveillez le portail nord" }),
            new JournalTurnView(3, 3, 6, 1, new List<string> { "Je crois que Léo ment" }),
        };

        public IReadOnlyList<JournalTurnView> GetTurns() => _turns;

        private void Start() => OnChanged?.Invoke();
    }
}
