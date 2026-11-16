using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CorruptionDuPortail.Domain.Localization;
using UnityEngine.UIElements;

namespace Localization
{
    /// <summary>
    /// The UI Toolkit side of the language system (T36).
    ///
    /// <para><see cref="SetLoc"/>: writes a game text into a label from code and keeps it right when the language
    /// changes, like <see cref="LocalizedText"/> does for uGUI: <c>label.SetLoc("TOUR {0}", day)</c>. For the screens
    /// that cannot simply be rebuilt on a language change (a rebuild would lose what the player entered).</para>
    ///
    /// <para><see cref="Localize"/>: shows the French texts written in a UXML file in the player's language. A screen
    /// calls it on its root once its tree is built, and again when <see cref="Loc.LanguageChanged"/> fires. A label
    /// that code has written since the last pass is taken as it is now.</para>
    /// </summary>
    public static class LocalizedUitk
    {
        private sealed class Record
        {
            public string Source;
            public object[] Args;
            public string Applied;
        }

        // Weak on both sides: an element that left its tree for good takes its record with it.
        private static readonly ConditionalWeakTable<TextElement, Record> s_bound =
            new ConditionalWeakTable<TextElement, Record>();
        private static readonly List<WeakReference<TextElement>> s_boundElements = new List<WeakReference<TextElement>>();
        private static int s_pruneAt = 256;

        private static readonly ConditionalWeakTable<TextElement, Record> s_authored =
            new ConditionalWeakTable<TextElement, Record>();

        /// <summary>
        /// Writes the French <paramref name="_source"/> (its {0}, {1}… filled) in the player's language. An argument
        /// that is itself a game text (a role name) is passed as a <c>Loc.Token</c>, so it follows the language too.
        /// </summary>
        public static T SetLoc<T>(this T _element, string _source, params object[] _args) where T : TextElement
        {
            if (_element == null)
            {
                return null;
            }
            if (!s_bound.TryGetValue(_element, out Record _record))
            {
                _record = new Record();
                s_bound.Add(_element, _record);
                s_boundElements.Add(new WeakReference<TextElement>(_element));
                if (s_boundElements.Count >= s_pruneAt)
                {
                    Prune();
                }
            }
            _record.Source = _source;
            _record.Args = _args;
            _record.Applied = LocalizedText.Compose(_source, _args);
            _element.text = _record.Applied;
            return _element;
        }

        /// <summary>Translates the text authored in the UXML of every text element under <paramref name="_root"/> (itself included).</summary>
        public static void Localize(VisualElement _root)
        {
            if (_root == null)
            {
                return;
            }
            if (_root is TextElement _self)
            {
                ApplyAuthored(_self);
            }
            _root.Query<TextElement>().ForEach(ApplyAuthored);
        }

        /// <summary>Hooked on <see cref="Loc.LanguageChanged"/> by <see cref="LocalizationRuntime"/>.</summary>
        internal static void OnLanguageChanged()
        {
            for (int _i = s_boundElements.Count - 1; _i >= 0; _i--)
            {
                if (!s_boundElements[_i].TryGetTarget(out TextElement _element) ||
                    !s_bound.TryGetValue(_element, out Record _record))
                {
                    s_boundElements.RemoveAt(_i);
                    continue;
                }
                // Overwritten by code since: not ours any more.
                if (_element.text != _record.Applied)
                {
                    s_bound.Remove(_element);
                    s_boundElements.RemoveAt(_i);
                    continue;
                }
                _record.Applied = LocalizedText.Compose(_record.Source, _record.Args);
                _element.text = _record.Applied;
            }
        }

        /// <summary>A new play session starts (domain reload may be off): forget the labels of the last one.</summary>
        internal static void Reset()
        {
            s_boundElements.Clear();
            s_pruneAt = 256;
        }

        private static void Prune()
        {
            s_boundElements.RemoveAll(_reference => !_reference.TryGetTarget(out _));
            s_pruneAt = Math.Max(256, s_boundElements.Count * 2);
        }

        private static void ApplyAuthored(TextElement _element)
        {
            // Written with SetLoc: already handled, and its text is no table key.
            if (s_bound.TryGetValue(_element, out _))
            {
                return;
            }
            string _current = _element.text;
            if (!s_authored.TryGetValue(_element, out Record _record))
            {
                if (string.IsNullOrWhiteSpace(_current))
                {
                    return;
                }
                _record = new Record { Source = _current };
                s_authored.Add(_element, _record);
            }
            else if (_current != _record.Applied)
            {
                // Code wrote over it: what is there now is the text to show.
                _record.Source = _current;
            }
            // Quiet: the element may hold a text that is no game text (a pseudo written by code). The texts written
            // in the UXML files are checked by LocalizationCoverageTests.
            _record.Applied = Loc.TrQuiet(_record.Source);
            if (_record.Applied != _current)
            {
                _element.text = _record.Applied;
            }
        }
    }
}
