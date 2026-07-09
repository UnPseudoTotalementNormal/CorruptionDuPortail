using System.Collections.Generic;
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
    }
}
