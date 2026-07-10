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
        /// <summary>True if the slot's character has the Robot role (HighPriorityBounty).</summary>
        bool IsRobot(int slot);
        /// <summary>Whether the slot's character is currently healed (Blessing skips re-heal).</summary>
        bool IsHealed(int slot);
        /// <summary>The slot's character's role display name (message composition).</summary>
        string RoleNameOf(int slot);
    }
}
