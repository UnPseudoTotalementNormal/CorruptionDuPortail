using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Localization
{
    /// <summary>
    /// Writes a game text into a label FROM CODE, in the player's language, and keeps it right when the language
    /// changes (T36): <c>votesText.SetLoc("Votes : {0}", count)</c> instead of <c>votesText.text = $"Votes: {count}"</c>.
    /// The label remembers its French source and its arguments; when <see cref="Loc.LanguageChanged"/> fires, every
    /// label still showing what was written here is written again. A label that some code overwrote since
    /// (<c>.text = …</c>) is forgotten.
    ///
    /// <para>An argument that is itself a game text (a role name) is passed as a <see cref="Loc.Token(string, object[])"/>,
    /// so it follows the language too; a pseudo or a number is passed as is.</para>
    /// </summary>
    public static class LocalizedText
    {
        private sealed class Entry
        {
            public UnityEngine.Object Target;
            public Func<string> Read;
            public Action<string> Write;
            public string Source;
            public object[] Args;
            public string Applied;
        }

        private static readonly Dictionary<UnityEngine.Object, Entry> s_entries = new Dictionary<UnityEngine.Object, Entry>();
        private static readonly List<UnityEngine.Object> s_dead = new List<UnityEngine.Object>();

        /// <summary>Writes the French <paramref name="_source"/> (its {0}, {1}… filled) in the player's language.</summary>
        public static void SetLoc(this TMP_Text _label, string _source, params object[] _args)
        {
            if (_label == null)
            {
                return;
            }
            Set(_label, () => _label.text, _value => _label.text = _value, _source, _args);
        }

        /// <summary>Same for a legacy uGUI <see cref="Text"/>.</summary>
        public static void SetLoc(this Text _label, string _source, params object[] _args)
        {
            if (_label == null)
            {
                return;
            }
            Set(_label, () => _label.text, _value => _label.text = _value, _source, _args);
        }

        /// <summary>
        /// Writes "{0} joueur" or "{0} joueurs" by the language's own plural rule, {0} being <paramref name="_count"/>
        /// (further arguments fill {1}, {2}…).
        /// </summary>
        public static void SetLocPlural(this TMP_Text _label, int _count, string _singularSource, string _pluralSource,
            params object[] _moreArgs)
        {
            if (_label == null)
            {
                return;
            }
            var _args = new object[1 + (_moreArgs?.Length ?? 0)];
            _args[0] = new PluralCount(_count, _singularSource, _pluralSource);
            _moreArgs?.CopyTo(_args, 1);
            Set(_label, () => _label.text, _value => _label.text = _value, _singularSource, _args);
        }

        // First argument of a plural text: carries both sources so the right one is picked again in another language.
        private sealed class PluralCount
        {
            public readonly int Count;
            public readonly string Singular;
            public readonly string Plural;

            public PluralCount(int _count, string _singular, string _plural)
            {
                Count = _count;
                Singular = _singular;
                Plural = _plural;
            }

            public override string ToString() => Count.ToString();
        }

        private static void Set(UnityEngine.Object _target, Func<string> _read, Action<string> _write, string _source, object[] _args)
        {
            var _entry = new Entry { Target = _target, Read = _read, Write = _write, Source = _source, Args = _args };
            s_entries[_target] = _entry;
            Apply(_entry);
            if (s_entries.Count >= s_pruneAt)
            {
                PruneDestroyed();
            }
        }

        // Labels of cards, rows and lists come and go all game long: drop the destroyed ones now and then.
        private static int s_pruneAt = 256;

        private static void PruneDestroyed()
        {
            s_dead.Clear();
            foreach (KeyValuePair<UnityEngine.Object, Entry> _pair in s_entries)
            {
                if (_pair.Value.Target == null)
                {
                    s_dead.Add(_pair.Key);
                }
            }
            foreach (UnityEngine.Object _id in s_dead)
            {
                s_entries.Remove(_id);
            }
            s_pruneAt = Mathf.Max(256, s_entries.Count * 2);
        }

        private static void Apply(Entry _entry)
        {
            _entry.Applied = Compose(_entry.Source, _entry.Args);
            _entry.Write(_entry.Applied);
        }

        /// <summary>The text of a source and its arguments in the current language (shared with <see cref="LocalizedUitk"/>).</summary>
        internal static string Compose(string _source, object[] _args)
        {
            object[] _shown = null;
            if (_args != null && _args.Length > 0)
            {
                _shown = new object[_args.Length];
                for (int _i = 0; _i < _args.Length; _i++)
                {
                    object _arg = _args[_i];
                    if (_arg is PluralCount _plural)
                    {
                        _source = Loc.IsSingular(Loc.Language, _plural.Count) ? _plural.Singular : _plural.Plural;
                    }
                    _shown[_i] = _arg is string _text ? Loc.Resolve(_text) : _arg;
                }
            }
            return _shown == null ? Loc.Tr(_source) : Loc.Tr(_source, _shown);
        }

        /// <summary>Hooked on <see cref="Loc.LanguageChanged"/> by <see cref="LocalizationRuntime"/>.</summary>
        internal static void OnLanguageChanged()
        {
            s_dead.Clear();
            foreach (KeyValuePair<UnityEngine.Object, Entry> _pair in s_entries)
            {
                Entry _entry = _pair.Value;
                // Destroyed, or overwritten by code since: not ours any more.
                if (_entry.Target == null || _entry.Read() != _entry.Applied)
                {
                    s_dead.Add(_pair.Key);
                    continue;
                }
                Apply(_entry);
            }
            foreach (UnityEngine.Object _id in s_dead)
            {
                s_entries.Remove(_id);
            }
        }

        /// <summary>A new play session starts (domain reload may be off): forget the labels of the last one.</summary>
        internal static void Reset() => s_entries.Clear();
    }
}
