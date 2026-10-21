using System.Collections.Generic;

namespace Board
{
    /// <summary>
    /// Takes over where the board's cards go (T23: by day they leave the table for a row at the bottom of the screen).
    /// <see cref="BoardManager.PlaceAllCardsToPosition"/> asks it first; cards dealt while it is set fly straight to it.
    /// </summary>
    public interface ICardLayoutOverride
    {
        /// <summary>Places <paramref name="_cards"/> (the board's visible cards, in order). False = the board lays them out.</summary>
        bool PlaceCards(IReadOnlyList<Card> _cards);
    }
}
