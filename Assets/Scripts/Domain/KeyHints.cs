using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>What a key hint in the corner of the screen offers, in the order they are laid out.</summary>
    public enum KeyHintAction
    {
        OpenTablet,
        CloseTablet,
        PreviousScreen,
        NextScreen,
        Speak,
        Emotes,
        Menu,
        ReportBug,
        // T23, the day's keys (mockups jour-pov): the players' row (1), the roles' row (2), the tablet's table (T).
        PlayersRow,
        RolesRow,
        Spreadsheet,
        SkipVote,
    }

    /// <summary>What the player can do with a key right now, read off the live game by the presenter.</summary>
    public readonly struct KeyHintContext
    {
        public readonly bool TabletAvailable;
        public readonly bool TabletOpen;
        public readonly bool HasPreviousScreen;
        public readonly bool HasNextScreen;
        public readonly bool CanSpeak;
        public readonly bool EmotesReachable;
        public readonly bool MenuAvailable;
        public readonly bool BugReportAvailable;
        /// <summary>By day: the two screen rows answer their keys (1 and 2).</summary>
        public readonly bool DayRowsAvailable;
        /// <summary>By day: T opens the tablet on the players' table.</summary>
        public readonly bool SpreadsheetAvailable;
        /// <summary>By day, during the vote, before voting: the skip key (the board's skip button went with the table).</summary>
        public readonly bool SkipVoteAvailable;

        public KeyHintContext(bool tabletAvailable, bool tabletOpen, bool hasPreviousScreen, bool hasNextScreen,
            bool canSpeak, bool emotesReachable, bool menuAvailable, bool bugReportAvailable,
            bool dayRowsAvailable = false, bool spreadsheetAvailable = false, bool skipVoteAvailable = false)
        {
            TabletAvailable = tabletAvailable;
            TabletOpen = tabletOpen;
            HasPreviousScreen = hasPreviousScreen;
            HasNextScreen = hasNextScreen;
            CanSpeak = canSpeak;
            EmotesReachable = emotesReachable;
            MenuAvailable = menuAvailable;
            BugReportAvailable = bugReportAvailable;
            DayRowsAvailable = dayRowsAvailable;
            SpreadsheetAvailable = spreadsheetAvailable;
            SkipVoteAvailable = skipVoteAvailable;
        }
    }

    /// <summary>
    /// The keyboard keys are invisible to the player (2026-10-21): small drawings with their key in the bottom left
    /// corner tell what a key does. Mockups touches v1 A3 + C1, v2 L2 + B1 + K5: only what works at this moment (the
    /// tablet's key, its arrows once it is open, the voice key when the player may speak, the emote wheel when it can
    /// open), the menu and the bug report always; they never go away. Pure: the presenter feeds what the game allows.
    /// </summary>
    public static class KeyHints
    {
        /// <summary>Fills <paramref name="hints"/> with the hints to show, in layout order (left to right).</summary>
        public static void For(KeyHintContext context, List<KeyHintAction> hints)
        {
            hints.Clear();
            // The day's own keys come first (mockups jour-pov: 1, 2, T, then the others).
            if (context.DayRowsAvailable)
            {
                hints.Add(KeyHintAction.PlayersRow);
                hints.Add(KeyHintAction.RolesRow);
            }
            if (context.SpreadsheetAvailable && !context.TabletOpen) hints.Add(KeyHintAction.Spreadsheet);
            if (context.SkipVoteAvailable) hints.Add(KeyHintAction.SkipVote);
            if (context.TabletAvailable)
            {
                if (!context.TabletOpen)
                {
                    hints.Add(KeyHintAction.OpenTablet);
                }
                else
                {
                    hints.Add(KeyHintAction.CloseTablet);
                    if (context.HasPreviousScreen) hints.Add(KeyHintAction.PreviousScreen);
                    if (context.HasNextScreen) hints.Add(KeyHintAction.NextScreen);
                }
            }
            if (context.CanSpeak) hints.Add(KeyHintAction.Speak);
            if (context.EmotesReachable) hints.Add(KeyHintAction.Emotes);
            if (context.MenuAvailable) hints.Add(KeyHintAction.Menu);
            if (context.BugReportAvailable) hints.Add(KeyHintAction.ReportBug);
        }

        /// <summary>
        /// The text under a hint's drawing. <paramref name="keyName"/> is the Input System key name ("Tab", "Escape",
        /// "RightArrow", "F8", "V"), <paramref name="displayName"/> what the player's keyboard layout prints on it (an
        /// AZERTY "Z" where a QWERTY has "W"). Special keys get a French name, letters follow the layout.
        /// </summary>
        public static string Label(string keyName, string displayName)
        {
            switch (keyName)
            {
                case "Tab": return "Tab";
                case "Escape": return "Échap";
                case "Digit1": case "Numpad1": return "1";
                case "Digit2": case "Numpad2": return "2";
                case "Enter": return "Entrée";
                case "Space": return "Espace";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
            }
            if (keyName.Length >= 2 && keyName[0] == 'F' && char.IsDigit(keyName[1]))
            {
                return keyName;
            }
            string printed = string.IsNullOrWhiteSpace(displayName) ? keyName : displayName.Trim();
            return printed.Length == 1 ? printed.ToUpperInvariant() : printed;
        }
    }
}
