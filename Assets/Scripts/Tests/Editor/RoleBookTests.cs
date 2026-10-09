using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using NUnit.Framework;
using UI.LobbyRoles;
using UI.RoleCard;
using UnityEditor;

namespace Tests.Editor
{
    /// <summary>
    /// The main menu's role book: page order / navigation (pure <see cref="RoleBookPages"/>) and, on the real authored
    /// pool, that every page states how its role wins (victory lines read from the role's WinningConditions).
    /// </summary>
    [Category("RoleBook")]
    public class RoleBookTests
    {
        private const string PoolPath = "Assets/ScriptableObjects/GameStates/RoleAttributionState.asset";

        [Test]
        public void Order_GroupsByChapter_KeepingPoolOrder_AndDropsUnknown()
        {
            var roles = new[]
            {
                ("Robot", FactionType.marginal), ("Gloubi", FactionType.chosen), ("Mage", FactionType.anomaly),
                ("Ghost", FactionType.unknown), ("Dryade", FactionType.chosen), ("Abyss", FactionType.anomaly),
            };

            List<(string, FactionType)> pages = RoleBookPages.Order(roles, r => r.Item2);

            Assert.That(pages.Select(p => p.Item1), Is.EqualTo(new[] { "Mage", "Abyss", "Gloubi", "Dryade", "Robot" }));
        }

        [Test]
        public void FirstPageOf_OpensTheChapterAtItsFirstRole()
        {
            var pages = new[] { FactionType.anomaly, FactionType.anomaly, FactionType.chosen, FactionType.marginal };

            Assert.AreEqual(0, RoleBookPages.FirstPageOf(pages, f => f, FactionType.anomaly));
            Assert.AreEqual(2, RoleBookPages.FirstPageOf(pages, f => f, FactionType.chosen));
            Assert.AreEqual(3, RoleBookPages.FirstPageOf(pages, f => f, FactionType.marginal));
            Assert.AreEqual(-1, RoleBookPages.FirstPageOf(pages, f => f, FactionType.unknown));
        }

        [Test]
        public void Turn_StopsAtTheCovers()
        {
            Assert.AreEqual(1, RoleBookPages.Turn(0, 1, 3));
            Assert.AreEqual(2, RoleBookPages.Turn(2, 1, 3));
            Assert.AreEqual(0, RoleBookPages.Turn(0, -1, 3));
            Assert.AreEqual(-1, RoleBookPages.Turn(0, 1, 0));
        }

        [Test]
        public void EveryPoolRole_HasAVictoryLine_FromItsWinningConditions()
        {
            List<Role> pages = PoolPages();
            Assert.IsNotEmpty(pages, "role pool asset empty or missing");

            foreach (Role role in pages)
                Assert.IsNotEmpty(RoleSheet.VictoryLines(role), $"{role.roleName}: no victory line");

            Role chosen = pages.First(r => r.factionType == FactionType.chosen);
            Assert.That(RoleSheet.VictoryLines(chosen),
                Is.EqualTo(new[] { "Les Élus gagnent quand toutes les Anomalies sont enchaînées." }));
        }

        [Test]
        public void Robot_ShowsBothOfItsConditions_AndMage_ItsPortalObjective()
        {
            List<Role> pages = PoolPages();

            Role robot = pages.Single(r => r.roleID == RoleID.Robot);
            Assert.AreEqual(2, RoleSheet.VictoryLines(robot).Count);

            Role mage = pages.Single(r => r.roleID == RoleID.MageOcculte);
            List<string> mageLines = RoleSheet.VictoryLines(mage);
            Assert.AreEqual("Les Anomalies gagnent quand tous les joueurs sont corrompus.", mageLines[0]);
            Assert.That(mageLines.Any(l => l.Contains("portail")), "the hidden portal objective is the Mage's second way to win");
        }

        [Test]
        public void PoolPages_StartWithTheAnomalies_AndEndWithTheMarginals()
        {
            List<Role> pages = PoolPages();

            Assert.AreEqual(FactionType.anomaly, pages.First().factionType);
            Assert.AreEqual(FactionType.marginal, pages.Last().factionType);
        }

        [Test]
        public void FactionInk_StaysDarkEnoughToReadOnParchment()
        {
            var factions = AssetDatabase.LoadAssetAtPath<FactionDatabase>("Assets/ScriptableObjects/FactionDatabase.asset");
            Assert.IsNotNull(factions);
            foreach (FactionType faction in RoleBookPages.ChapterOrder)
            {
                UnityEngine.Color ink = RoleSheet.InkOf(factions.Get(faction).color);
                float luma = 0.2126f * ink.r + 0.7152f * ink.g + 0.0722f * ink.b;
                Assert.LessOrEqual(luma, 0.30f, $"{faction} ink too light on parchment");
            }
        }

        private static List<Role> PoolPages()
        {
            var pool = AssetDatabase.LoadAssetAtPath<RoleAttributionState>(PoolPath);
            Assert.IsNotNull(pool, PoolPath);
            return RoleBookPages.Order(pool.roleAttributionDictionary.Keys.Select(LobbyRoleDetail.From), r => r.factionType);
        }
    }
}
