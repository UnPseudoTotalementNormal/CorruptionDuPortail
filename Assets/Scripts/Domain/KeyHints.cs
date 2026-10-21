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

        public KeyHintContext(bool tabletAvailable, bool tabletOpen, bool hasPreviousScreen, bool hasNextScreen,
            bool canSpeak, bool emotesReachable, bool menuAvailable, bool bugReportAvailable)
        {
            TabletAvailable = tabletAvailable;
            TabletOpen = tabletOpen;
            HasPreviousScreen = hasPreviousScreen;
            HasNextScreen = hasNextScreen;
            CanSpeak = canSpeak;
            EmotesReachable = emotesReachable;
            MenuAvailable = menuAvailable;
            BugReportAvailable = bugReportAvailable;
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
