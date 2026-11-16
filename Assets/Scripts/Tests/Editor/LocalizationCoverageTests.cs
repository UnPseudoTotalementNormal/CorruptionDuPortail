using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CorruptionDuPortail.Domain.Localization;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T36: the translation table (<c>Assets/Resources/Localization/texts.json</c>) covers the whole game. A French text
    /// added or reworded anywhere (code, role or power asset, UXML, label of a prefab or a scene) fails here until its
    /// English is written, so no text ships half translated. How each kind of text is found:
    /// <see cref="LocalizationSources"/>. Fixing a failure: add the entry to the table (and run
    /// <c>Tools &gt; Localization &gt; Add LocalizedLabel to translated labels</c> for a label).
    /// </summary>
    [Category("Localization")]
    public class LocalizationCoverageTests
    {
        private static Dictionary<string, string> s_table;
        private static HashSet<string> s_known;
        private static List<LocalizationSources.Source> s_sources;
        private static List<LocalizationSources.LabelText> s_labels;

        // Scanning the code, the assets and the three scenes takes a few seconds: done once for the fixture.
        [OneTimeSetUp]
        public void Collect()
        {
            s_table = LocalizationSources.EnglishTable();
            s_known = new HashSet<string>(s_table.Keys.Select(LocalizationSources.Normalize), StringComparer.Ordinal);
            s_sources = new List<LocalizationSources.Source>();
            s_sources.AddRange(LocalizationSources.Code());
            s_sources.AddRange(LocalizationSources.Computed());
            s_sources.AddRange(LocalizationSources.Assets());
            s_sources.AddRange(LocalizationSources.SceneFields());
            s_labels = new List<LocalizationSources.LabelText>();
            s_labels.AddRange(LocalizationSources.PrefabLabels());
            s_labels.AddRange(LocalizationSources.SceneLabels());
        }

        [Test]
        public void TheScan_FindsTheGameTexts()
        {
            // A scan that silently found nothing would make every other test pass.
            Assert.Greater(s_sources.Count, 300, "game texts found in the code and the assets");
            Assert.Greater(s_labels.Count, 100, "labels found in the prefabs and the scenes");
            Assert.Greater(s_table.Count, 300, "entries of the translation table");
            CollectionAssert.Contains(s_sources.Select(_s => _s.text).ToList(), "Le nom du lobby ne peut pas être vide", "a Loc.Tr literal");
            CollectionAssert.Contains(s_sources.Select(_s => _s.text).ToList(), "Vous vous éveillez", "a '// loc' serialized default");
            CollectionAssert.Contains(s_sources.Select(_s => _s.text).ToList(), "Bénédiction", "a power name on a prefab");
            CollectionAssert.Contains(s_sources.Select(_s => _s.text).ToList(), "L'Orpheline", "a role name on a ScriptableObject");
            CollectionAssert.Contains(s_sources.Select(_s => _s.text).ToList(), "Messages anonymes", "a UXML text");
        }

        [Test]
        public void EveryGameText_HasAnEnglishTranslation()
        {
            var _missing = new List<string>();
            var _seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocalizationSources.Source _source in s_sources)
            {
                string _key = LocalizationSources.Normalize(_source.text);
                if (!s_known.Contains(_key) && _seen.Add(_key))
                {
                    _missing.Add($"\"{_key}\"   <- {_source.origin}");
                }
            }
            Assert.IsEmpty(_missing,
                $"{_missing.Count} French text(s) with no entry in {LocalizationSources.TablePath}:\n" + string.Join("\n", _missing));
        }

        [Test]
        public void EveryLabel_IsTranslated_OrListedAsAPlaceholder()
        {
            HashSet<string> _placeholders = LocalizationSources.Placeholders();
            var _undecided = new List<string>();
            var _unlabelled = new List<string>();
            foreach (LocalizationSources.LabelText _label in s_labels)
            {
                string _key = LocalizationSources.Normalize(_label.text);
                if (s_known.Contains(_key))
                {
                    if (!_label.localized)
                    {
                        _unlabelled.Add($"\"{_key}\"   <- {_label.origin}");
                    }
                }
                else if (!_placeholders.Contains(_key))
                {
                    _undecided.Add($"\"{_key}\"   <- {_label.origin}");
                }
            }
            Assert.IsEmpty(_unlabelled,
                $"{_unlabelled.Count} translated label(s) without a LocalizedLabel: run Tools > Localization > Add LocalizedLabel " +
                "to translated labels.\n" + string.Join("\n", _unlabelled));
            Assert.IsEmpty(_undecided,
                $"{_undecided.Count} label(s) of a prefab or a scene are neither translated nor known as sample texts: add the " +
                $"entry to {LocalizationSources.TablePath} (a real label), or the text to {LocalizationSources.PlaceholdersPath} " +
                "(a sample value that code replaces).\n" + string.Join("\n", _undecided));
        }

        [Test]
        public void EveryTranslation_KeepsThePlaceholdersAndTagsOfItsSource()
        {
            var _broken = new List<string>();
            foreach (KeyValuePair<string, string> _entry in s_table)
            {
                if (!Marks(_entry.Key).SequenceEqual(Marks(_entry.Value)))
                {
                    _broken.Add($"\"{_entry.Key}\"\n   fr: {string.Join(" ", Marks(_entry.Key))}\n   en: {string.Join(" ", Marks(_entry.Value))}");
                }
            }
            Assert.IsEmpty(_broken,
                "A translation must carry the same {0} / {var:name} placeholders and the same rich-text tags as its French " +
                "source (their order may change):\n" + string.Join("\n", _broken));
        }

        [Test]
        public void NoEntry_IsLeftOverFromATextThatNoLongerExists()
        {
            var _used = new HashSet<string>(s_sources.Select(_s => LocalizationSources.Normalize(_s.text)), StringComparer.Ordinal);
            _used.UnionWith(s_labels.Select(_l => LocalizationSources.Normalize(_l.text)));
            // Entries for texts of signs only (the quotation marks of a language) are never listed by the scan.
            List<string> _orphans = s_known.Where(_key => Loc.IsTranslatable(_key) && !_used.Contains(_key)).ToList();
            Assert.IsEmpty(_orphans,
                $"{_orphans.Count} entr(y/ies) of {LocalizationSources.TablePath} match no text of the game any more (the French " +
                "was reworded or removed): update or delete them.\n" + string.Join("\n", _orphans));
        }

        [Test]
        public void NoText_IsBothTranslatedAndListedAsAPlaceholder()
        {
            List<string> _both = LocalizationSources.Placeholders().Where(s_known.Contains).ToList();
            Assert.IsEmpty(_both, "Listed as sample texts AND translated: " + string.Join(" | ", _both));
        }

        // {0}, {var:x}, <i>, </color>, <link=id>… sorted: a translation may move them, never add, drop or alter one.
        private static List<string> Marks(string _text) =>
            Regex.Matches(_text, @"\{[^}]*\}|</?[a-zA-Z][^>]*>").Cast<Match>().Select(_m => _m.Value).OrderBy(_m => _m, StringComparer.Ordinal).ToList();
    }
}
