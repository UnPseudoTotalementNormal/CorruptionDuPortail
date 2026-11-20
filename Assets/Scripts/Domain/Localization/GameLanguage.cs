namespace CorruptionDuPortail.Domain.Localization
{
    /// <summary>
    /// The languages the game can be shown in (T36). French is the source language: every text is written in French
    /// in the code and the assets, the other languages are translation tables (<see cref="Loc"/>).
    /// The numeric values are saved in PlayerPrefs: append, never reorder.
    /// </summary>
    public enum GameLanguage
    {
        French = 0,
        English = 1,
    }
}
