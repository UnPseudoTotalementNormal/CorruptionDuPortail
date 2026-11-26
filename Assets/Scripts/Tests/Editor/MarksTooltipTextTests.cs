using Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>T9 (maquettes placeholders v3 H2): the bubble that lists the marks of a card or a role portrait.</summary>
    public class MarksTooltipTextTests
    {
        [Test]
        public void CursedVision_TrueIsNotAChosen_FalseIsAChosen()
        {
            Assert.AreEqual(MarksTooltipText.MarkIcon.non_elu, MarksTooltipText.ForCardEffect(CardEffectID.CursedVision, true)?.Icon);
            Assert.AreEqual(MarksTooltipText.MarkIcon.elu, MarksTooltipText.ForCardEffect(CardEffectID.CursedVision, false)?.Icon);
        }

        [Test]
        public void EveryCardEffect_HasAMeaning()
        {
            foreach (CardEffectID _id in System.Enum.GetValues(typeof(CardEffectID)))
            {
                Assert.IsTrue(MarksTooltipText.ForCardEffect(_id, false).HasValue, $"{_id} has no line in the marks bubble");
            }
        }

        [Test]
        public void Rows_OneSpriteTagPerMark_EmptyWhenNone()
        {
            Assert.AreEqual(string.Empty, MarksTooltipText.Rows(new MarksTooltipText.Mark[0]));
            string _text = MarksTooltipText.Rows(new[]
            {
                MarksTooltipText.ForCardEffect(CardEffectID.OrphelineVisit, null).Value,
                MarksTooltipText.ForCardEffect(CardEffectID.MessagerLink, null).Value,
            });
            StringAssert.Contains("<sprite=\"Marques\" name=\"ours\">", _text);
            StringAssert.Contains("<sprite=\"Marques\" name=\"telephone\">", _text);
        }

        [Test]
        public void PortraitIcons_FoundBySpriteName_UnknownIgnored()
        {
            Sprite _bear = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            _bear.name = "Icon_Orpheline_Ours";
            Sprite _other = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            _other.name = "Something";
            Assert.AreEqual(MarksTooltipText.MarkIcon.ours, MarksTooltipText.ForPortraitIcon(_bear)?.Icon);
            Assert.IsFalse(MarksTooltipText.ForPortraitIcon(_other).HasValue);
            Assert.IsFalse(MarksTooltipText.ForPortraitIcon(null).HasValue);
        }

        [Test]
        public void StateMarks_PublicElu_Chained_Hacked_HaveTheirOwnIcons()
        {
            // Fil « Joueur perdu »: marks read from the player's state, not from a card effect.
            Assert.AreEqual(MarksTooltipText.MarkIcon.logo_elu, MarksTooltipText.KnownChosen().Icon);
            Assert.AreEqual(MarksTooltipText.MarkIcon.chaine, MarksTooltipText.Chained().Icon);
            Assert.AreEqual(MarksTooltipText.MarkIcon.robot, MarksTooltipText.Hacked().Icon);
        }

        [Test]
        public void SpriteAsset_HasEveryIcon()
        {
            var _asset = Resources.Load<TMPro.TMP_SpriteAsset>("Sprite Assets/Marques");
            Assert.IsNotNull(_asset, "Resources/Sprite Assets/Marques is missing");
            foreach (MarksTooltipText.MarkIcon _icon in System.Enum.GetValues(typeof(MarksTooltipText.MarkIcon)))
            {
                Assert.GreaterOrEqual(_asset.GetSpriteIndexFromName(_icon.ToString()), 0, $"no sprite {_icon} in Marques");
            }
        }
    }
}
