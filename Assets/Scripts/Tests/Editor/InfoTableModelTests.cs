using System.Collections.Generic;
using Characters;
using NUnit.Framework;
using UI.InfoTable;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode characterization of <see cref="InfoTableModel"/> — the pure port of the uGUI board's cell,
    /// local-conflict, over-capacity and reveal-lock logic. Pins the I/O matrix from the spec so the UITK
    /// rebuild stays behaviour-preserving.
    /// </summary>
    [Category("InfoTable")]
    public class InfoTableModelTests
    {
        private static InfoTableModel Build(int players, params int[] roleCapacities)
        {
            var p = new List<InfoTablePlayer>();
            for (int i = 0; i < players; i++) p.Add(new InfoTablePlayer((ulong)i, $"P{i}"));

            var r = new List<InfoTableRole>();
            for (int i = 0; i < roleCapacities.Length; i++) r.Add(new InfoTableRole($"R{i}", roleCapacities[i]));

            var m = new InfoTableModel();
            m.Build(p, r);
            return m;
        }

        [Test]
        public void Build_StartsEmptyUnlockedConflictFree()
        {
            var m = Build(2, 1, 1);
            Assert.AreEqual(2, m.PlayerCount);
            Assert.AreEqual(2, m.RoleCount);
            for (int pi = 0; pi < 2; pi++)
            {
                Assert.IsFalse(m.IsLocked(pi));
                Assert.AreEqual(ConflictType.None, m.GetConflict(pi));
                for (int ri = 0; ri < 2; ri++) Assert.AreEqual(CellState.None, m.GetCell(pi, ri));
            }
        }

        [Test]
        public void SetCell_SelectsState()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            Assert.AreEqual(CellState.Sure, m.GetCell(0, 0));
        }

        [Test]
        public void SetCell_ReclickingActiveState_ClearsToNone()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Maybe);
            m.SetCell(0, 0, CellState.Maybe);
            Assert.AreEqual(CellState.None, m.GetCell(0, 0));
        }

        [Test]
        public void SetCell_HoldsAtMostOneStatePerCell()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(0, 0, CellState.SurelyNot);
            Assert.AreEqual(CellState.SurelyNot, m.GetCell(0, 0));
        }

        [Test]
        public void LocalConflict_RaisedWhenTwoSureInOneRow()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(0, 1, CellState.Sure);
            Assert.AreEqual(ConflictType.PlayerMultipleRoles, m.GetConflict(0));
            Assert.IsTrue(m.IsCellInConflict(0, 0));
            Assert.IsTrue(m.IsCellInConflict(0, 1));
        }

        [Test]
        public void LocalConflict_ClearsWhenBackToOneSure()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(0, 1, CellState.Sure);
            m.SetCell(0, 1, CellState.Sure); // toggle the second off
            Assert.AreEqual(ConflictType.None, m.GetConflict(0));
        }

        [Test]
        public void OverCapacity_RaisedWhenMoreSureThanCapacity()
        {
            var m = Build(2, 1); // one role, capacity 1
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(1, 0, CellState.Sure);
            Assert.AreEqual(ConflictType.RoleOverCapacity, m.GetConflict(0));
            Assert.AreEqual(ConflictType.RoleOverCapacity, m.GetConflict(1));
        }

        [Test]
        public void OverCapacity_NotRaisedWithinCapacity()
        {
            var m = Build(2, 2); // capacity 2
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(1, 0, CellState.Sure);
            Assert.AreEqual(ConflictType.None, m.GetConflict(0));
            Assert.AreEqual(ConflictType.None, m.GetConflict(1));
        }

        [Test]
        public void LocalConflict_TakesPrecedenceOverOverCapacity()
        {
            var m = Build(2, 1, 1);
            // Row 0 has two Sure (local conflict) AND is over capacity on role 0 with row 1.
            m.SetCell(0, 0, CellState.Sure);
            m.SetCell(0, 1, CellState.Sure);
            m.SetCell(1, 0, CellState.Sure);
            Assert.AreEqual(ConflictType.PlayerMultipleRoles, m.GetConflict(0));
            Assert.AreEqual(ConflictType.RoleOverCapacity, m.GetConflict(1));
        }

        [Test]
        public void LockRowToRole_ForcesCorrectSure_OthersSurelyNot_AndLocks()
        {
            var m = Build(1, 1, 1);
            m.LockRowToRole(0, "R0");
            Assert.IsTrue(m.IsLocked(0));
            Assert.AreEqual(CellState.Sure, m.GetCell(0, 0));
            Assert.AreEqual(CellState.SurelyNot, m.GetCell(0, 1));
            Assert.AreEqual(ConflictType.None, m.GetConflict(0));
        }

        [Test]
        public void LockedRow_IgnoresFurtherClicks()
        {
            var m = Build(1, 1, 1);
            m.LockRowToRole(0, "R0");
            m.SetCell(0, 1, CellState.Sure);
            Assert.AreEqual(CellState.SurelyNot, m.GetCell(0, 1));
        }

        [Test]
        public void LockedRow_ExcludedFromOverCapacity()
        {
            var m = Build(2, 1);
            m.LockRowToRole(0, "R0");   // locked Sure on role 0
            m.SetCell(1, 0, CellState.Sure); // second Sure would be over-capacity...
            Assert.AreEqual(ConflictType.None, m.GetConflict(0)); // ...but the locked row is never flagged
        }

        [Test]
        public void LockRowToRole_NoMatchingColumn_LeavesRowInteractive()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Maybe);
            m.LockRowToRole(0, "does-not-exist");   // must NOT seal the row into an all-SurelyNot dead state
            Assert.IsFalse(m.IsLocked(0));
            Assert.AreEqual(CellState.Maybe, m.GetCell(0, 0)); // untouched
            m.SetCell(0, 1, CellState.Sure);        // still interactive
            Assert.AreEqual(CellState.Sure, m.GetCell(0, 1));
        }

        [Test]
        public void LockRowToRole_DuplicateRoleName_LocksOnlyFirstColumn()
        {
            var players = new List<InfoTablePlayer> { new(0, "P0") };
            var roles = new List<InfoTableRole> { new("Dup", 1), new("Dup", 1) };
            var m = new InfoTableModel();
            m.Build(players, roles);

            m.LockRowToRole(0, "Dup");
            Assert.IsTrue(m.IsLocked(0));
            Assert.AreEqual(CellState.Sure, m.GetCell(0, 0));       // first match only
            Assert.AreEqual(CellState.SurelyNot, m.GetCell(0, 1));  // duplicate column NOT forced Sure
        }

        // ---- "Freed"/dimmed derived state (feedback item 2 — visual grey-out) ----

        [Test]
        public void IsRowFound_TrueOnlyWhenRowHasASure()
        {
            var m = Build(1, 1, 1);
            Assert.IsFalse(m.IsRowFound(0));
            m.SetCell(0, 1, CellState.Maybe);
            Assert.IsFalse(m.IsRowFound(0));   // Maybe is not a commitment
            m.SetCell(0, 0, CellState.Sure);
            Assert.IsTrue(m.IsRowFound(0));
        }

        [Test]
        public void IsColumnClaimed_TrueWhenSureCountReachesCapacity()
        {
            var m = Build(2, 2);               // one role, capacity 2
            m.SetCell(0, 0, CellState.Sure);
            Assert.IsFalse(m.IsColumnClaimed(0));   // 1 of 2 seats
            m.SetCell(1, 0, CellState.Sure);
            Assert.IsTrue(m.IsColumnClaimed(0));    // 2 of 2 seats
        }

        [Test]
        public void IsCellDimmed_FoundRowDimsItsOtherCells_ButNotTheSure()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            Assert.IsFalse(m.IsCellDimmed(0, 0));   // the Sure origin never dims
            Assert.IsTrue(m.IsCellDimmed(0, 1));    // the freed sibling cell dims
        }

        [Test]
        public void IsCellDimmed_ClaimedColumnDimsOtherPlayers_ButNotWhenSeatOpen()
        {
            var m = Build(2, 2);                    // capacity-2 role
            m.SetCell(0, 0, CellState.Sure);
            Assert.IsFalse(m.IsCellDimmed(1, 0));   // a seat is still open — not claimed, not dimmed
            m.SetCell(1, 0, CellState.Sure);        // second Sure was placed by player 1; both are Sure
            // Column now claimed; a THIRD player's cell would dim. With only 2 players both are Sure (never dim).
            Assert.IsFalse(m.IsCellDimmed(0, 0));
            Assert.IsFalse(m.IsCellDimmed(1, 0));
        }

        [Test]
        public void IsCellDimmed_ClaimedColumnDimsANonSureOtherPlayer()
        {
            var m = Build(2, 1, 1);                 // 2 players, 2 capacity-1 roles
            m.SetCell(0, 0, CellState.Sure);        // player 0 claims role 0 (capacity 1 → claimed)
            Assert.IsTrue(m.IsColumnClaimed(0));
            Assert.IsTrue(m.IsCellDimmed(1, 0));    // player 1's cell on the claimed role dims
            Assert.IsFalse(m.IsCellDimmed(1, 1));   // unrelated open column stays live
        }

        [Test]
        public void IsCellDimmed_StaysDerived_ClearingTheSureRestoresAllCells()
        {
            var m = Build(1, 1, 1);
            m.SetCell(0, 0, CellState.Sure);
            Assert.IsTrue(m.IsCellDimmed(0, 1));
            m.SetCell(0, 0, CellState.Sure);        // toggle the Sure back off
            Assert.IsFalse(m.IsCellDimmed(0, 1));   // dimming is purely derived — nothing latched
        }

        // ---- Camp guess (feedback item 1 — coarse faction column) ----

        [Test]
        public void CampGuess_DefaultsToUnknown()
        {
            var m = Build(1, 1);
            // Guards against the enum-default trap: default(FactionType) is anomaly (0), not unknown.
            Assert.AreEqual(FactionType.unknown, m.GetCampGuess(0));
        }

        [Test]
        public void CampGuess_CyclesUnknownChosenMarginalAnomaly_AndWraps()
        {
            var m = Build(1, 1);
            m.CycleCampGuess(0); Assert.AreEqual(FactionType.chosen, m.GetCampGuess(0));
            m.CycleCampGuess(0); Assert.AreEqual(FactionType.marginal, m.GetCampGuess(0));
            m.CycleCampGuess(0); Assert.AreEqual(FactionType.anomaly, m.GetCampGuess(0));
            m.CycleCampGuess(0); Assert.AreEqual(FactionType.unknown, m.GetCampGuess(0));   // wraps back
        }

        [Test]
        public void CampGuess_IsPerPlayer()
        {
            var m = Build(2, 1);
            m.CycleCampGuess(1);
            Assert.AreEqual(FactionType.unknown, m.GetCampGuess(0));
            Assert.AreEqual(FactionType.chosen, m.GetCampGuess(1));
        }

        [Test]
        public void Roles_CarryFactionThroughToTheModel()
        {
            var players = new List<InfoTablePlayer> { new(0, "P0") };
            var roles = new List<InfoTableRole> { new("Mage", 1, FactionType.anomaly) };
            var m = new InfoTableModel();
            m.Build(players, roles);
            Assert.AreEqual(FactionType.anomaly, m.Roles[0].Faction);
        }
    }
}
