using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// Pure read-only view of the character roster for roster-reading power decisions: the logical
    /// slots in play, plus per-slot faction and display pseudo. The runtime holder builds it from the
    /// live characters; EditMode tests supply a plain fake. No engine types leak (FactionType is a
    /// Domain enum).
    /// </summary>
    public interface IRosterView
    {
        IReadOnlyList<int> Slots { get; }
        FactionType FactionOf(int slot);
        string PseudoOf(int slot);
        /// <summary>True if the two slots' characters have the same role (role-match powers).</summary>
        bool SameRole(int slotA, int slotB);
    }
}
