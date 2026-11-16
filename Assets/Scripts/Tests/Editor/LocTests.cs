using System.Collections.Generic;
using CorruptionDuPortail.Domain.Localization;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>T36: the pure translation engine (<see cref="Loc"/>): lookups, plurals, travelling tokens.</summary>
    public class LocTests
    {
        [SetUp]
        public void SetUp()
        {
            Loc.Reset();
            Loc.SetTable(GameLanguage.English, new Dictionary<string, string>
            {
                { "Rôles", "Roles" },
                { "{0} est un élu.", "{0} is a chosen one." },
                { "{0} joueur", "{0} player" },
                { "{0} joueurs", "{0} players" },
                { "{0} était le robot et a été éliminé par {1}.", "{1} eliminated {0}, who was the robot." },
                { "Gardien", "Guardian" },
                { "Inflige {var:damage} dégâts à {0}.", "Deals {var:damage} damage to {0}." },
                { "Ligne un\nLigne deux", "Line one\nLine two" },
            });
        }

        [TearDown]
        public void TearDown() => Loc.Reset();

        [Test]
        public void Tr_InFrench_ReturnsTheSource()
        {
            Assert.AreEqual("Rôles", Loc.Tr("Rôles"));
            Assert.AreEqual("Alice est un élu.", Loc.Tr("{0} est un élu.", "Alice"));
        }

        [Test]
        public void Tr_InEnglish_ReturnsTheTranslation()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Roles", Loc.Tr("Rôles"));
            Assert.AreEqual("Alice is a chosen one.", Loc.Tr("{0} est un élu.", "Alice"));
        }

        [Test]
        public void Tr_WithoutTranslation_FallsBackToFrench_AndRecordsItOnce()
        {
            Loc.SetLanguage(GameLanguage.English);
            var _reported = new List<string>();
            Loc.MissingText += (_, _text) => _reported.Add(_text);

            Assert.AreEqual("Pas traduit", Loc.Tr("Pas traduit"));
            Assert.AreEqual("Pas traduit", Loc.Tr("Pas traduit"));

            CollectionAssert.AreEqual(new[] { "Pas traduit" }, _reported);
            CollectionAssert.Contains(Loc.MissingTexts, "Pas traduit");
        }

        [Test]
        public void TrQuiet_WithoutTranslation_RecordsNothing()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Bob", Loc.TrQuiet("Bob"));
            CollectionAssert.IsEmpty(Loc.MissingTexts);
        }

        [Test]
        public void Tr_EmptyOrNull_IsReturnedAsIs()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.IsNull(Loc.Tr(null));
            Assert.AreEqual("", Loc.Tr(""));
            Assert.AreEqual("  ", Loc.Tr("  "));
            CollectionAssert.IsEmpty(Loc.MissingTexts);
        }

        [Test]
        public void Tr_MatchesAssetTexts_WhateverTheLineEndingsAndOuterSpaces()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Line one\nLine two", Loc.Tr("Ligne un\r\nLigne deux "));
        }

        [Test]
        public void Tr_ArgumentsMayBeReordered_AndOtherBracesAreLeftAlone()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Guardian eliminated Bob, who was the robot.",
                Loc.Tr("{0} était le robot et a été éliminé par {1}.", "Bob", "Guardian"));
            Assert.AreEqual("Deals {var:damage} damage to Bob.", Loc.Tr("Inflige {var:damage} dégâts à {0}.", "Bob"));
        }

        [Test]
        public void Tr_AnArgumentWithBraces_IsNotFormattedAgain()
        {
            Assert.AreEqual("{1} est un élu.", Loc.Tr("{0} est un élu.", "{1}"));
        }

        [TestCase(0, GameLanguage.French, "0 joueur")]
        [TestCase(1, GameLanguage.French, "1 joueur")]
        [TestCase(2, GameLanguage.French, "2 joueurs")]
        [TestCase(0, GameLanguage.English, "0 players")]
        [TestCase(1, GameLanguage.English, "1 player")]
        [TestCase(2, GameLanguage.English, "2 players")]
        public void Plural_FollowsTheLanguageRule(int _count, GameLanguage _language, string _expected)
        {
            Loc.SetLanguage(_language);
            Assert.AreEqual(_expected, Loc.Plural(_count, "{0} joueur", "{0} joueurs"));
        }

        [Test]
        public void SetLanguage_RaisesLanguageChanged_OnlyOnAChange()
        {
            int _raised = 0;
            Loc.LanguageChanged += () => _raised++;
            Loc.SetLanguage(GameLanguage.French);
            Assert.AreEqual(0, _raised);
            Loc.SetLanguage(GameLanguage.English);
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual(1, _raised);
        }

        [Test]
        public void Token_IsResolvedInTheReadersLanguage()
        {
            // The server builds the token whatever its own language is…
            Loc.SetLanguage(GameLanguage.English);
            string _token = Loc.Token("{0} est un élu.", "Alice");

            // …and every reader shows it in its own.
            Assert.AreEqual("Alice is a chosen one.", Loc.Resolve(_token));
            Loc.SetLanguage(GameLanguage.French);
            Assert.AreEqual("Alice est un élu.", Loc.Resolve(_token));
        }

        [Test]
        public void Token_ArgumentsMayBeTokens()
        {
            string _token = Loc.Token("{0} était le robot et a été éliminé par {1}.", "Bob", Loc.Token("Gardien"));
            Assert.AreEqual("Bob était le robot et a été éliminé par Gardien.", Loc.Resolve(_token));
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Guardian eliminated Bob, who was the robot.", Loc.Resolve(_token));
        }

        [Test]
        public void Resolve_HandlesTokensInsideALargerText()
        {
            Loc.SetLanguage(GameLanguage.English);
            string _text = "<b>" + Loc.Token("Gardien") + "</b> / " + Loc.Token("Rôles");
            Assert.AreEqual("<b>Guardian</b> / Roles", Loc.Resolve(_text));
        }

        [Test]
        public void Resolve_PlainText_ComesBackUntouched()
        {
            Loc.SetLanguage(GameLanguage.English);
            Assert.AreEqual("Rôles", Loc.Resolve("Rôles"), "a text that is not a token is never translated");
            Assert.IsNull(Loc.Resolve(null));
            Assert.AreEqual("", Loc.Resolve(""));
        }

        [Test]
        public void Resolve_ATokenCutShort_IsReadToTheEnd()
        {
            Loc.SetLanguage(GameLanguage.English);
            string _token = Loc.Token("{0} est un élu.", "Alice");
            string _truncated = _token.Substring(0, _token.Length - 1);
            Assert.AreEqual("Alice is a chosen one.", Loc.Resolve(_truncated));
        }

        [Test]
        public void StripMarks_APlayerCannotForgeAToken()
        {
            Loc.SetLanguage(GameLanguage.English);
            string _forged = Loc.Token("Rôles");
            string _clean = Loc.StripMarks(_forged);
            Assert.AreEqual("Rôles", _clean);
            Assert.AreEqual("Rôles", Loc.Resolve(_clean));
            Assert.AreEqual("bonjour", Loc.StripMarks("bonjour"));
        }

        [Test]
        public void HasTranslation_FrenchAlwaysHasOne()
        {
            Assert.IsTrue(Loc.HasTranslation(GameLanguage.French, "N'importe quoi"));
            Assert.IsTrue(Loc.HasTranslation(GameLanguage.English, "Rôles"));
            Assert.IsFalse(Loc.HasTranslation(GameLanguage.English, "N'importe quoi"));
        }
    }
}
