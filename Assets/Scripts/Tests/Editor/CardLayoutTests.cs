using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode pins of <see cref="CardLayout"/>: the grid per table size and preset, the placement arithmetic, and the
    /// board's fit constraints measured in game by the autoplay card visibility probe (2026-10-08, 1600 × 900), so a
    /// retune that would hide a card, a vote button or a vote count again fails here before it reaches a playtest.
    /// </summary>
    [Category("CardLayout")]
    public class CardLayoutTests
    {
        private const float Tol = 1e-4f;

        // Board anchors of GameScene (BoardManager.spawnCardPosition / maxCardPosition, board units).
        private const float OriginX = -19f;
        private const float MaxX = 19f;
        private const float CentreX = 0f;

        // Card geometry (Card.prefab) and what the probe measured around it.
        private const float HalfDepth = 4.4f;
        private const float HoveredTextBelowCentre = 6.89f * 1.1f; // hovered vote count text, hovered card enlarged
        private const float MoiTagAboveCentre = 4.4f + 2.6f;
        private const float CharactersBarZ = 9.8f; // top of the free band (top view)
        private const float BottomHudZ = -13.6f; // bottom of the free band (bottom HUD, skip button)
        private const float BoardHalfWidth = 19f + CardLayout.CardWidth / 2f;

        private readonly CardLayout _layout = new();

        private static readonly CardGridPreset[] Presets =
            { CardGridPreset.RaisedLeft, CardGridPreset.Centred, CardGridPreset.CentredTop };

        private CardPlacement Place(int index, int count, CardGridPreset preset)
            => _layout.Place(index, count, _layout.GridFor(count, preset), OriginX, 0.09f, 0f, CentreX);

        private static int Lines(int count, CardGrid grid) => (count + grid.Columns - 1) / grid.Columns;

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(6)]
        public void OneLine_KeepsFullSize(int count)
        {
            foreach (CardGridPreset _preset in Presets)
            {
                CardGrid _grid = _layout.GridFor(count, _preset);
                Assert.AreEqual(1f, _grid.Scale, Tol, $"{_preset}");
                Assert.AreEqual(1, Lines(count, _grid), $"{_preset}");
            }
        }

        [TestCase(7)]
        [TestCase(12)]
        public void TwoLinesOfSix_UpToTwelveSeats(int count)
        {
            CardGrid _grid = _layout.GridFor(count, CardGridPreset.Centred);
            Assert.AreEqual(6, _grid.Columns);
            Assert.AreEqual(2, Lines(count, _grid));
            Assert.Less(_grid.Scale, 1f, "two lines with every hovered vote text visible need smaller cards");
        }

        [TestCase(13)]
        [TestCase(14)]
        public void TwoLinesOfSeven_AtThirteenAndFourteen(int count)
        {
            CardGrid _grid = _layout.GridFor(count, CardGridPreset.RaisedLeft);
            Assert.AreEqual(7, _grid.Columns);
            Assert.AreEqual(2, Lines(count, _grid));
        }

        [Test]
        public void OnlyRaisedLeft_IsLeftAligned()
        {
            Assert.IsFalse(_layout.GridFor(8, CardGridPreset.RaisedLeft).Centred);
            Assert.IsTrue(_layout.GridFor(8, CardGridPreset.Centred).Centred);
            Assert.IsTrue(_layout.GridFor(8, CardGridPreset.CentredTop).Centred);
        }

        [Test]
        public void LeftAligned_FirstCardKeepsTheBoardsLeftEdge_AndColumnsAdvance()
        {
            CardGrid _grid = _layout.GridFor(8, CardGridPreset.RaisedLeft);
            CardPlacement _first = Place(0, 8, CardGridPreset.RaisedLeft);
            CardPlacement _second = Place(1, 8, CardGridPreset.RaisedLeft);
            Assert.AreEqual(OriginX - CardLayout.CardWidth / 2f, _first.X - CardLayout.CardWidth / 2f * _grid.Scale, Tol);
            Assert.AreEqual(CardLayout.ColumnSpacing * _grid.Scale, _second.X - _first.X, Tol);
        }

        [Test]
        public void Wrap_StartsTheNextLineOneLineSpacingFurther()
        {
            CardGrid _grid = _layout.GridFor(8, CardGridPreset.RaisedLeft);
            CardPlacement _first = Place(0, 8, CardGridPreset.RaisedLeft);
            CardPlacement _seventh = Place(6, 8, CardGridPreset.RaisedLeft);
            Assert.AreEqual(_first.X, _seventh.X, Tol, "back to the first column");
            Assert.AreEqual(_first.Z - _grid.LineSpacing, _seventh.Z, Tol);
            Assert.AreEqual(_grid.FirstLineZ, _first.Z, Tol);
        }

        [Test]
        public void Centred_EveryLineIsCentredOnTheBoard()
        {
            // 8 cards: a full line of 6 then a line of 2, each centred.
            float _full = (Place(0, 8, CardGridPreset.Centred).X + Place(5, 8, CardGridPreset.Centred).X) / 2f;
            float _partial = (Place(6, 8, CardGridPreset.Centred).X + Place(7, 8, CardGridPreset.Centred).X) / 2f;
            Assert.AreEqual(CentreX, _full, Tol);
            Assert.AreEqual(CentreX, _partial, Tol);
            Assert.AreEqual(CentreX, (Place(0, 7, CardGridPreset.Centred).X + Place(5, 7, CardGridPreset.Centred).X) / 2f, Tol);
            Assert.AreEqual(CentreX, Place(6, 7, CardGridPreset.Centred).X, Tol, "a lone card on its line sits in the middle");
        }

        [Test]
        public void YNeverChanges_AndNegativeIndexIsTheFirstCard()
        {
            Assert.AreEqual(0.09f, Place(9, 12, CardGridPreset.RaisedLeft).Y, Tol);
            Assert.AreEqual(Place(0, 5, CardGridPreset.Centred).X, Place(-3, 5, CardGridPreset.Centred).X, Tol);
        }

        // The in-game constraints (probe, every view, hovered or not), for every table size the presets ship and more.
        [Test]
        public void EveryCardAndVotePanel_FitsTheFreeBand([Values(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)] int count,
            [Values(CardGridPreset.RaisedLeft, CardGridPreset.Centred, CardGridPreset.CentredTop)] CardGridPreset preset)
        {
            CardGrid _grid = _layout.GridFor(count, preset);
            int _lines = Lines(count, _grid);
            float _s = _grid.Scale;
            float _nearZ = _grid.FirstLineZ - (_lines - 1) * _grid.LineSpacing;
            Assert.LessOrEqual(_grid.FirstLineZ + MoiTagAboveCentre * _s, CharactersBarZ + Tol, "the Moi tag stays under the characters bar");
            Assert.GreaterOrEqual(_nearZ - HoveredTextBelowCentre * _s, BottomHudZ - Tol, "the near line's hovered vote text stays above the bottom HUD");
            if (_lines > 1)
            {
                Assert.GreaterOrEqual(_grid.LineSpacing - (HalfDepth + HoveredTextBelowCentre) * _s, 0.2f,
                    "a far line's hovered vote text clears the next line");
            }
            for (int _i = 0; _i < count; _i++)
            {
                float _x = _layout.Place(_i, count, _grid, OriginX, 0f, 0f, CentreX).X;
                Assert.LessOrEqual(System.Math.Abs(_x) + CardLayout.CardWidth / 2f * _s, BoardHalfWidth + Tol, $"card {_i} on the board");
            }
        }
    }
}
