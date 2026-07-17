using System;
using System.Collections.Generic;

namespace UI.MessageJournal
{
    // Presentation-only seam (no NGO) so the journal overlay renders standalone in a Play harness.
    // The live implementation reads MessageManager (turnStats + revealedMessages); the demo feeds canned data.
    public interface IMessageJournalDataSource
    {
        event Action OnChanged;
        IReadOnlyList<JournalTurnView> GetTurns();
    }

    // One turn's row in the journal: the per-turn stat line + that turn's anonymous messages.
    // robotTargetCount < 0 means there is no Robot in the game → the targeting clause is omitted.
    public readonly struct JournalTurnView
    {
        public readonly int day;
        public readonly int corruptedCount;
        public readonly int nonAnomalyTotal;
        public readonly int robotTargetCount;
        public readonly IReadOnlyList<string> messages;

        public JournalTurnView(int _day, int _corruptedCount, int _nonAnomalyTotal, int _robotTargetCount, IReadOnlyList<string> _messages)
        {
            day = _day;
            corruptedCount = _corruptedCount;
            nonAnomalyTotal = _nonAnomalyTotal;
            robotTargetCount = _robotTargetCount;
            messages = _messages ?? System.Array.Empty<string>();
        }

        public bool hasRobot => robotTargetCount >= 0;
    }
}
