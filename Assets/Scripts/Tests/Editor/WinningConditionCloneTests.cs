using System.Collections.Generic;
using Characters;
using Characters.WinningConditions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Bug hunt 2026-10-08: <see cref="Role.Clone"/> shared the authored asset's winning-condition instances (the class
    /// was not cloneable), so every Robot of every game had one condition whose owner stayed the asset's 0 on the
    /// server: the Robot "won" when seat 0 was chained, and a stale owner made the victory check throw (game hung).
    /// Each character's role must own its conditions.
    /// </summary>
    public class WinningConditionCloneTests
    {
        [Test]
        public void RoleClone_GivesEachCharacterItsOwnConditions()
        {
            var _authored = new Role
            {
                roleID = RoleID.Robot,
                winningConditions = new List<WinningCondition> { new WMarginalIsChainedWin(), new WOmniscienceHackedCharacter() },
            };

            var _first = (Role)_authored.Clone();
            var _second = (Role)_authored.Clone();
            _first.winningConditions[0].ownerClientId = 7;
            _second.winningConditions[0].ownerClientId = 101;

            Assert.AreNotSame(_authored.winningConditions[0], _first.winningConditions[0], "a clone must not share the asset's instance");
            Assert.AreNotSame(_first.winningConditions[1], _second.winningConditions[1]);
            Assert.AreEqual(0UL, _authored.winningConditions[0].ownerClientId, "the authored asset is never written");
            Assert.AreEqual(7UL, _first.winningConditions[0].ownerClientId);
            Assert.AreEqual(101UL, _second.winningConditions[0].ownerClientId);
            Assert.IsInstanceOf<WOmniscienceHackedCharacter>(_first.winningConditions[1], "the concrete type is kept");
        }
    }
}
