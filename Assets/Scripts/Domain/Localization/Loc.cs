using System;
using System.Collections.Generic;
using System.Text;

namespace CorruptionDuPortail.Domain.Localization
{
    /// <summary>
    /// The game's texts in the player's language (T36). French is the source: code and assets keep their French
    /// text, and that text is the key of the translation tables (gettext style). A text with no translation is shown
    /// in French and recorded in <see cref="MissingTexts"/>.
    ///
    /// <para><b>Local text</b> (built and shown on the same machine): <see cref="Tr(string)"/>,
    /// <see cref="Tr(string, object[])"/>, <see cref="Plural"/>.</para>
    ///
    /// <para><b>Text that travels</b> (the server builds it, every client shows it in ITS language): the server sends a
    /// <see cref="Token(string, object[])"/>, never a translated text, and whoever displays it calls
    /// <see cref="Resolve"/>. A token is a plain string (it fits the existing FixedString fields and RPCs), it may sit
    /// inside a larger string and its arguments may be tokens themselves (a role name inside a sentence).</para>
    ///
    /// Pure and engine-free: the tables are handed over by the game side (<c>LocalizationRuntime</c>).
    /// </summary>
    public static class Loc
    {
        /// <summary>Opens a token. Control characters: never typed by a player (<see cref="StripMarks"/>).</summary>
        public const char TokenStart = '\u0001';
        /// <summary>Separates a token's source text from its arguments, and the arguments from each other.</summary>
        public const char TokenSeparator = '\u0002';
        /// <summary>Closes a token.</summary>
        public const char TokenEnd = '\u0003';

        private static readonly Dictionary<GameLanguage, Dictionary<string, string>> s_tables =
            new Dictionary<GameLanguage, Dictionary<string, string>>();
        private static readonly HashSet<string> s_missing = new HashSet<string>();
        private static GameLanguage s_language = GameLanguage.French;

        /// <summary>The language texts are shown in on this machine.</summary>
        public static GameLanguage Language => s_language;

        /// <summary>Raised after the language changed: whatever shows text re-reads it.</summary>
        public static event Action LanguageChanged;

        /// <summary>Raised the first time a text has no translation in the current language (playtest log).</summary>
        public static event Action<GameLanguage, string> MissingText;

        /// <summary>The French texts asked for in the current language that had no translation.</summary>
        public static IReadOnlyCollection<string> MissingTexts => s_missing;

        public static void SetLanguage(GameLanguage language)
        {
            if (language == s_language)
            {
                return;
            }
            s_language = language;
            s_missing.Clear();
            LanguageChanged?.Invoke();
        }

        /// <summary>Installs (replaces) the translation table of a language: French text → translated text.</summary>
        public static void SetTable(GameLanguage language, IEnumerable<KeyValuePair<string, string>> translations)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            if (translations != null)
            {
                foreach (KeyValuePair<string, string> pair in translations)
                {
                    if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value))
                    {
                        continue;
                    }
                    table[Normalize(pair.Key)] = pair.Value;
                }
            }
            s_tables[language] = table;
            s_missing.Clear();
        }

        /// <summary>Back to the state of a fresh process (French, no table, no listener). Tests and domain-reload-less play.</summary>
        public static void Reset()
        {
            s_tables.Clear();
            s_missing.Clear();
            s_language = GameLanguage.French;
            LanguageChanged = null;
            MissingText = null;
        }

        /// <summary>True when <paramref name="source"/> can be shown in <paramref name="language"/> (always for French).</summary>
        public static bool HasTranslation(GameLanguage language, string source)
        {
            if (language == GameLanguage.French || !IsTranslatable(source))
            {
                return true;
            }
            return s_tables.TryGetValue(language, out Dictionary<string, string> table) && table.ContainsKey(Normalize(source));
        }

        /// <summary>The French <paramref name="source"/> in the current language (itself when there is no translation).</summary>
        public static string Tr(string source) => Translate(source, true);

        /// <summary>Same as <see cref="Tr(string)"/> without recording a missing translation: for a text that may not
        /// be a game text at all (a label some code may have filled with a player's name).</summary>
        public static string TrQuiet(string source) => Translate(source, false);

        /// <summary>The French <paramref name="source"/> in the current language, its <c>{0}</c>, <c>{1}</c>… replaced
        /// by <paramref name="args"/>. Other braces (<c>{var:x}</c>) are left alone.</summary>
        public static string Tr(string source, params object[] args) => Format(Translate(source, true), args);

        /// <summary>
        /// Picks the singular or the plural source by the current language's rule (French: 0 and 1 are singular;
        /// English: only 1), translates it and replaces <c>{0}</c> by <paramref name="count"/>.
        /// </summary>
        public static string Plural(int count, string singularSource, string pluralSource)
        {
            return Tr(IsSingular(s_language, count) ? singularSource : pluralSource, count);
        }

        public static bool IsSingular(GameLanguage language, int count)
        {
            int magnitude = Math.Abs(count);
            return language == GameLanguage.French ? magnitude < 2 : magnitude == 1;
        }

        /// <summary>
        /// A text to be translated by whoever displays it (<see cref="Resolve"/>): what the server sends instead of a
        /// sentence. <paramref name="args"/> fill <c>{0}</c>, <c>{1}</c>…; an argument that must be translated too (a
        /// role name) is itself a <see cref="Token(string, object[])"/>, anything else (a pseudo, a number) is sent as is.
        /// </summary>
        public static string Token(string source, params object[] args)
        {
            var builder = new StringBuilder();
            builder.Append(TokenStart).Append(source);
            if (args != null)
            {
                foreach (object arg in args)
                {
                    builder.Append(TokenSeparator).Append(arg);
                }
            }
            builder.Append(TokenEnd);
            return builder.ToString();
        }

        /// <summary>True when <paramref name="text"/> carries at least one token.</summary>
        public static bool HasToken(string text) => !string.IsNullOrEmpty(text) && text.IndexOf(TokenStart) >= 0;

        /// <summary>
        /// Replaces every token of <paramref name="text"/> by its text in the current language. A text without token
        /// comes back untouched, so every display point can call this on whatever it shows. A token cut short (a
        /// truncated network string) is read up to the end of the text.
        /// </summary>
        public static string Resolve(string text)
        {
            if (!HasToken(text))
            {
                return text;
            }
            var builder = new StringBuilder(text.Length);
            int index = 0;
            ResolveInto(text, ref index, builder, false);
            return builder.ToString();
        }

        /// <summary>
        /// For a game text that reaches a display point either as a token (it travelled) or as a plain French source
        /// (it was authored or built locally): resolves the first, translates the second. Never for a text a player
        /// may have typed.
        /// </summary>
        public static string Show(string text) => HasToken(text) ? Resolve(text) : Tr(text);

        /// <summary>
        /// For a text received from another machine that may be a game text or not (the server's reason for a refused
        /// connection, which the network layer may also fill with its own words): a token is resolved, a known French
        /// game text is translated, anything else comes back untouched and unrecorded. Such reasons travel as plain
        /// French on purpose: a player on another build of the game still reads them.
        /// </summary>
        public static string ShowForeign(string text) => HasToken(text) ? Resolve(text) : TrQuiet(text);

        /// <summary>
        /// <see cref="Resolve"/> in French whatever the current language is: what a log or an autoplay journal records,
        /// so a report reads the same from every machine and never shows a raw token.
        /// </summary>
        public static string ResolveSource(string text)
        {
            if (!HasToken(text))
            {
                return text;
            }
            GameLanguage current = s_language;
            s_language = GameLanguage.French;
            try
            {
                return Resolve(text);
            }
            finally
            {
                s_language = current;
            }
        }

        /// <summary>Removes the token characters from a text a player typed: a player cannot forge a token.</summary>
        public static string StripMarks(string playerText)
        {
            if (string.IsNullOrEmpty(playerText))
            {
                return playerText;
            }
            if (playerText.IndexOf(TokenStart) < 0 && playerText.IndexOf(TokenSeparator) < 0 && playerText.IndexOf(TokenEnd) < 0)
            {
                return playerText;
            }
            var builder = new StringBuilder(playerText.Length);
            foreach (char c in playerText)
            {
                if (c != TokenStart && c != TokenSeparator && c != TokenEnd)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        // Copies text[index..] into builder, resolving tokens. Inside a token (insideToken) it stops, without consuming
        // it, at the separator or end mark that belongs to that token.
        private static void ResolveInto(string text, ref int index, StringBuilder builder, bool insideToken)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == TokenStart)
                {
                    index++;
                    builder.Append(ReadToken(text, ref index));
                    continue;
                }
                if (insideToken && (c == TokenSeparator || c == TokenEnd))
                {
                    return;
                }
                builder.Append(c);
                index++;
            }
        }

        // index is just after a TokenStart. Reads the source and the arguments, consumes the TokenEnd.
        private static string ReadToken(string text, ref int index)
        {
            var part = new StringBuilder();
            ResolveInto(text, ref index, part, true);
            string source = part.ToString();
            List<object> args = null;
            while (index < text.Length && text[index] == TokenSeparator)
            {
                index++;
                part.Length = 0;
                ResolveInto(text, ref index, part, true);
                if (args == null)
                {
                    args = new List<object>();
                }
                args.Add(part.ToString());
            }
            if (index < text.Length && text[index] == TokenEnd)
            {
                index++;
            }
            string translated = Translate(source, true);
            return args == null ? translated : Format(translated, args.ToArray());
        }

        /// <summary>
        /// False for a text with nothing to translate: empty, or only signs, digits and placeholders ("{0} ×{1}",
        /// "{var:effectDescription}", "‹", "∞"). Such a text needs no table entry: without one it is shown as is.
        /// </summary>
        public static bool IsTranslatable(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }
            int depth = 0;
            foreach (char c in source)
            {
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (depth == 0 && char.IsLetter(c))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Translate(string source, bool reportMissing)
        {
            if (s_language == GameLanguage.French || string.IsNullOrWhiteSpace(source))
            {
                return source;
            }
            string key = Normalize(source);
            if (s_tables.TryGetValue(s_language, out Dictionary<string, string> table) &&
                table.TryGetValue(key, out string translated))
            {
                return translated;
            }
            // A text of signs only (« {0} ») may have an entry (“{0}”), but lacking one is no gap.
            if (reportMissing && IsTranslatable(source) && s_missing.Add(key))
            {
                MissingText?.Invoke(s_language, key);
            }
            return source;
        }

        // Asset texts come with either line ending and stray spaces around: one key for all of them.
        private static string Normalize(string source) => source.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        // Replaces {0}..{n} only: game texts carry other braces ({var:damage}) that string.Format would choke on.
        private static string Format(string text, object[] args)
        {
            if (string.IsNullOrEmpty(text) || args == null || args.Length == 0 || text.IndexOf('{') < 0)
            {
                return text;
            }
            var builder = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i + 1 && TryParseIndex(text, i + 1, close, out int argIndex) && argIndex < args.Length)
                    {
                        builder.Append(args[argIndex]);
                        i = close + 1;
                        continue;
                    }
                }
                builder.Append(c);
                i++;
            }
            return builder.ToString();
        }

        private static bool TryParseIndex(string text, int start, int end, out int value)
        {
            value = 0;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
                value = value * 10 + (c - '0');
            }
            return end - start <= 2;
        }
    }
}
