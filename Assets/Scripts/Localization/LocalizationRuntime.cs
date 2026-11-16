using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain.Localization;
using Diagnostics.Logs;
using UnityEngine;

namespace Localization
{
    /// <summary>
    /// Game side of the language system (T36): loads the translation table into <see cref="Loc"/> before the first
    /// scene, picks the language of this machine and saves the player's choice.
    ///
    /// <para>Table: <c>Assets/Resources/Localization/texts.json</c>, one entry per French text
    /// (<c>{"fr": "...", "en": "..."}</c>). Adding a text = writing it in French where it is used
    /// (<c>Loc.Tr("…")</c>, a <see cref="LocalizedLabel"/>, an asset field) and adding its entry; the EditMode
    /// <c>LocalizationCoverageTests</c> list every French text that has no entry.</para>
    ///
    /// <para>Language at start, first match wins: <c>-lang fr|en</c> on the command line (this run only), French in an
    /// autoplay run (bots find the interface by its French labels), the player's saved choice, the system language
    /// (French → French, anything else → English).</para>
    /// </summary>
    public static class LocalizationRuntime
    {
        public const string TableResourcePath = "Localization/texts";
        private const string LanguagePrefKey = "Language";
        private const string LanguageArg = "-lang";

        [Serializable]
        private sealed class TextTable
        {
            public List<TextEntry> entries = new List<TextEntry>();
        }

        [Serializable]
        private sealed class TextEntry
        {
            public string fr;
            public string en;
        }

        // Domain reload may be disabled: start every play session from a clean Loc (no listener of the last session).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Loc.Reset();
            LoadTables();
            Loc.MissingText += OnMissingText;
            LocalizedText.Reset();
            Loc.LanguageChanged += LocalizedText.OnLanguageChanged;
            LocalizedUitk.Reset();
            Loc.LanguageChanged += LocalizedUitk.OnLanguageChanged;
            GameLanguage _language = StartLanguage(Environment.GetCommandLineArgs(), out string _source);
            Loc.SetLanguage(_language);
            GameLog.Info("loc", "loc.start", "language", Code(_language), "source", _source);
        }

        /// <summary>Reads the table from Resources into <see cref="Loc"/>. Also called by the tests and editor tools.</summary>
        public static void LoadTables()
        {
            TextAsset _asset = Resources.Load<TextAsset>(TableResourcePath);
            if (_asset == null)
            {
                Debug.LogError($"[LOC] Translation table missing: Resources/{TableResourcePath}.json. Every text stays in French.");
                Loc.SetTable(GameLanguage.English, null);
                return;
            }
            Loc.SetTable(GameLanguage.English, ParseEnglish(_asset.text));
        }

        /// <summary>The French → English pairs of a table file.</summary>
        public static Dictionary<string, string> ParseEnglish(string _json)
        {
            var _result = new Dictionary<string, string>(StringComparer.Ordinal);
            TextTable _table = JsonUtility.FromJson<TextTable>(_json);
            if (_table?.entries == null)
            {
                return _result;
            }
            foreach (TextEntry _entry in _table.entries)
            {
                if (!string.IsNullOrEmpty(_entry.fr) && !string.IsNullOrEmpty(_entry.en))
                {
                    _result[_entry.fr] = _entry.en;
                }
            }
            return _result;
        }

        /// <summary>The player picked a language in the options: applied now and kept for the next launches.</summary>
        public static void SetLanguage(GameLanguage _language)
        {
            GameLog.Info("ui", "ui.setting", "name", "language", "value", Code(_language));
            PlayerPrefs.SetInt(LanguagePrefKey, (int)_language);
            PlayerPrefs.Save();
            Loc.SetLanguage(_language);
        }

        public static string Code(GameLanguage _language) => _language == GameLanguage.French ? "fr" : "en";

        /// <summary>The name of a language, written in that language (what a language picker shows).</summary>
        public static string NativeName(GameLanguage _language) => _language == GameLanguage.French ? "Français" : "English";

        private static GameLanguage StartLanguage(string[] _args, out string _source)
        {
            bool _autoplay = false;
            for (int _i = 0; _i < _args.Length; _i++)
            {
                if (_args[_i] == LanguageArg && _i + 1 < _args.Length && TryParseCode(_args[_i + 1], out GameLanguage _forced))
                {
                    _source = "command-line";
                    return _forced;
                }
                if (_args[_i].StartsWith("-autoplay", StringComparison.OrdinalIgnoreCase))
                {
                    _autoplay = true;
                }
            }
            if (_autoplay)
            {
                _source = "autoplay";
                return GameLanguage.French;
            }
            if (PlayerPrefs.HasKey(LanguagePrefKey))
            {
                int _saved = PlayerPrefs.GetInt(LanguagePrefKey);
                if (Enum.IsDefined(typeof(GameLanguage), _saved))
                {
                    _source = "saved";
                    return (GameLanguage)_saved;
                }
            }
            _source = "system";
            return Application.systemLanguage == SystemLanguage.French ? GameLanguage.French : GameLanguage.English;
        }

        private static bool TryParseCode(string _code, out GameLanguage _language)
        {
            switch (_code.ToLowerInvariant())
            {
                case "fr":
                    _language = GameLanguage.French;
                    return true;
                case "en":
                    _language = GameLanguage.English;
                    return true;
                default:
                    _language = GameLanguage.French;
                    return false;
            }
        }

        private static void OnMissingText(GameLanguage _language, string _text)
        {
            Debug.LogWarning($"[LOC] No {Code(_language)} translation for: {_text}");
            GameLog.Warn("loc", "loc.missing", "language", Code(_language), "text", _text);
        }
    }
}
