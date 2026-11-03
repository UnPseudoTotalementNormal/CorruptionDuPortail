using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEditor;

namespace Tests.Editor
{
    /// <summary>
    /// T29 "Nuit rapide": the lobby option wakes the roles in 3 calls (the game designer's call-order sheet, one
    /// column per call). Every role of the classic order must wake in the compact one too, exactly once, or a role
    /// would silently never play its night.
    /// </summary>
    [Category("Awakening")]
    public class CompactNightOrderTests
    {
        private static AwakeningState LoadAsset() =>
            AssetDatabase.LoadAssetAtPath<AwakeningState>("Assets/ScriptableObjects/GameStates/AwakeningState.asset");

        private static List<string> Roles(IEnumerable<AwakeningLayerObject> _order) =>
            _order.SelectMany(_layer => _layer.awakeningCharacters).Select(_rdo => _rdo.name).ToList();

        [Test]
        public void CompactOrder_HasThreeCalls()
        {
            Assert.AreEqual(3, LoadAsset().compactAwakeningOrder.Count);
        }

        [Test]
        public void CompactOrder_WakesEveryClassicRole_ExactlyOnce()
        {
            AwakeningState _state = LoadAsset();
            List<string> _classic = Roles(_state.awakeningOrder);
            List<string> _compact = Roles(_state.compactAwakeningOrder);

            Assert.IsFalse(_compact.Any(string.IsNullOrEmpty), "No empty slot in the compact order.");
            CollectionAssert.AllItemsAreUnique(_compact, "A role wakes once a night.");
            CollectionAssert.AreEquivalent(_classic, _compact, "The compact night wakes the same roles as the classic one.");
        }

        [Test]
        public void CompactOrder_FollowsTheCallSheet()
        {
            List<AwakeningLayerObject> _order = LoadAsset().compactAwakeningOrder;
            string[][] _sheet =
            {
                new[] { "Messager", "Incomplet", "Uges", "Repenti", "Technomancien" },
                new[] { "Robot", "Abyss", "MageOcculte" },
                new[] { "Traqueuse", "Dryade", "DrGloubi", "Croupiere", "Orpheline" },
            };
            for (int i = 0; i < _sheet.Length; i++)
            {
                List<string> _call = Roles(new[] { _order[i] });
                foreach (string _role in _sheet[i])
                {
                    Assert.Contains(_role, _call, $"{_role} wakes on call {i + 1}.");
                }
            }
        }

        [Test]
        public void CompactFakeSleep_IsAboutOneChanceInThirtyPerSecond()
        {
            double _stillAwake = Math.Pow(1.0 - AwakeningState.FakeSleepChance(1f / 60f, AwakeningState.CompactFakeSleepRatePerSecond), 60);
            Assert.AreEqual(1.0 / 30.0, 1.0 - _stillAwake, 0.002, "A fake role sleeps with about 1 chance in 30 each second.");
        }
    }
}
