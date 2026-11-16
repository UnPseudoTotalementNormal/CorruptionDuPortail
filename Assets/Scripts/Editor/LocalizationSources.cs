using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Characters;
using Characters.Powers;
using Characters.Powers.PowerComponents;
using ChatSystem;
using CorruptionDuPortail.Domain.Localization;
using Localization;
using TMPro;
using TooltipSystem;
using UI;
using UI.BoardUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// T36: finds every French game text of the project, wherever it is written, so the translation table can be checked
/// against it (EditMode <c>LocalizationCoverageTests</c>, <c>Tools &gt; Localization</c>).
///
/// <para>Where a text may live, and how it is found:</para>
/// <list type="bullet">
/// <item>code: the string literals inside a <c>Loc.Tr / Plural / Token / Show…(</c> or <c>SetLoc…(</c> call, and
/// every literal of a line ending with a <c>// loc</c> comment (constants, serialized defaults);</item>
/// <item>assets: the text fields of the roles, powers, power components, factions, presets, cosmetics, emotes and
/// tooltips (ScriptableObjects and prefabs), and the <c>text="…"</c> of the UXML screens;</item>
/// <item>prefabs and scenes: the text of every label carrying a <see cref="LocalizedLabel"/>.</item>
/// </list>
/// </summary>
public static class LocalizationSources
{
    public const string TablePath = "Assets/Resources/Localization/texts.json";
    public const string PlaceholdersPath = "Assets/Scripts/Editor/LocalizationPlaceholders.json";

    /// <summary>The scenes the game ships (build settings, minus the old / backup ones).</summary>
    public static readonly string[] Scenes =
    {
        "Assets/Scenes/BootScene.unity",
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/GameScene.unity",
    };

    private static readonly string[] CodeRoot = { "Assets/Scripts" };
    private static readonly string[] CodeSkippedFolders = { "/Tests/", "/Editor/", "/Autoplay/", "/Spike/", "/Facepunch/", "/Domain/Localization/", "/Scripts/Localization/" };
    private static readonly string[] AssetSkippedFolders = { "Assets/Plugins/", "Assets/Samples/", "Assets/TextMesh Pro/", "Assets/Scenes/Spikes/", "Spike" };

    [Serializable]
    public sealed class Source
    {
        public string text;
        public string origin;
    }

    /// <summary>A label of a prefab or a scene: its text, and whether a <see cref="LocalizedLabel"/> translates it.</summary>
    [Serializable]
    public sealed class LabelText
    {
        public string text;
        public string origin;
        public bool localized;
    }

    [Serializable]
    private sealed class PlaceholderList
    {
        public List<string> texts = new List<string>();
    }

    // ------------------------------------------------------------------------------------------------ code

    private static readonly Regex Literal = new Regex("(?<!@)\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex LocCall = new Regex(@"\b(?:Loc\.(?:Tr|TrQuiet|Plural|Token|Show|ShowForeign)|SetLoc|SetLocPlural)\s*\(", RegexOptions.Compiled);
    // "// loc" marks a game text; "// loc-ok" (the loc_guard hook's other mark) a text that is NOT for players.
    private static readonly Regex MarkedLine = new Regex(@"//\s*loc\b(?!-ok)", RegexOptions.Compiled);

    public static List<Source> Code()
    {
        var result = new List<Source>();
        foreach (string path in Directory.GetFiles(CodeRoot[0], "*.cs", SearchOption.AllDirectories))
        {
            string normalized = path.Replace('\\', '/');
            if (CodeSkippedFolders.Any(normalized.Contains))
            {
                continue;
            }
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                {
                    continue;
                }
                foreach (string literal in LiteralsOf(line))
                {
                    if (Loc.IsTranslatable(literal))
                    {
                        result.Add(new Source { text = literal, origin = $"{normalized}:{i + 1}" });
                    }
                }
            }
        }
        return result;
    }

    // The literals of a line that are game texts: all of them on a "// loc" line, else those inside a Loc / SetLoc call.
    private static IEnumerable<string> LiteralsOf(string line)
    {
        bool marked = MarkedLine.IsMatch(line);
        var spans = new List<(int start, int end)>();
        if (!marked)
        {
            foreach (Match call in LocCall.Matches(line))
            {
                spans.Add((call.Index + call.Length, ClosingParenthesis(line, call.Index + call.Length)));
            }
            if (spans.Count == 0)
            {
                yield break;
            }
        }
        foreach (Match match in Literal.Matches(line))
        {
            if (marked || spans.Any(span => match.Index >= span.start && match.Index < span.end))
            {
                yield return Unescape(match.Groups[1].Value);
            }
        }
    }

    private static int ClosingParenthesis(string line, int from)
    {
        int depth = 1;
        bool inString = false;
        for (int i = from; i < line.Length; i++)
        {
            char c = line[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return line.Length; // the call goes on on the next lines: those need a "// loc" mark
    }

    private static string Unescape(string literal)
    {
        if (literal.IndexOf('\\') < 0)
        {
            return literal;
        }
        var builder = new StringBuilder(literal.Length);
        for (int i = 0; i < literal.Length; i++)
        {
            char c = literal[i];
            if (c != '\\' || i == literal.Length - 1)
            {
                builder.Append(c);
                continue;
            }
            char next = literal[++i];
            switch (next)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case 'u':
                    builder.Append((char)Convert.ToInt32(literal.Substring(i + 1, 4), 16));
                    i += 4;
                    break;
                default: builder.Append(next); break;
            }
        }
        return builder.ToString();
    }

    // Texts the code computes instead of writing (an enum name shown as a title): no literal to find.
    public static List<Source> Computed()
    {
        var result = new List<Source>();
        foreach (string name in Enum.GetNames(typeof(FactionType)))
        {
            result.Add(new Source { text = name.ToUpper(), origin = "CharactersBar (faction title = enum name)" });
        }
        foreach (string name in Enum.GetNames(typeof(ChatWindowIDs)))
        {
            result.Add(new Source { text = name, origin = "ChatManager (channel title = enum name)" });
        }
        return result;
    }

    // ------------------------------------------------------------------------------------------------ assets

    private static readonly Regex TextProperty = new Regex("(?i)(description|message|title|displayName|tagline|wakeText|sleepText|\\.lines\\.)", RegexOptions.Compiled);

    /// <summary>ScriptableObjects, the non-label text fields of prefabs, and the UXML screens.</summary>
    public static List<Source> Assets()
    {
        s_placeholders = null; // read the list again: it may have been edited since the last call
        var result = new List<Source>();
        foreach (Type type in new[] { typeof(RoleDataObject), typeof(RoleCardTexts), typeof(FactionDatabase),
                     typeof(GameLogic.GameSettings.RolePreset), typeof(Avatars.Cosmetics.CatCosmeticsCatalog), typeof(Avatars.EmoteSet) })
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Skipped(path))
                {
                    continue;
                }
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null)
                {
                    continue;
                }
                if (asset is RoleDataObject role)
                {
                    Add(result, role.role.roleName.ToString(), path + " (roleName)");
                    continue;
                }
                AddStringProperties(result, asset, path);
            }
        }
        foreach (string path in PrefabPaths())
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                AddComponentTexts(result, component, path);
            }
        }
        foreach (string path in Directory.GetFiles("Assets/UI", "*.uxml", SearchOption.AllDirectories))
        {
            string normalized = path.Replace('\\', '/');
            if (Skipped(normalized))
            {
                continue;
            }
            foreach (Match match in Regex.Matches(File.ReadAllText(path, Encoding.UTF8), "\\stext=\"([^\"]*)\""))
            {
                Add(result, System.Net.WebUtility.HtmlDecode(match.Groups[1].Value), normalized);
            }
        }
        return result;
    }

    /// <summary>The non-label text fields of the scene objects (tooltips, the picker's titles, the night banner).
    /// Opens each scene: the scene that was open is not restored.</summary>
    public static List<Source> SceneFields()
    {
        var result = new List<Source>();
        foreach (string scenePath in Scenes)
        {
            foreach (GameObject root in EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single).GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    AddComponentTexts(result, component, scenePath);
                }
            }
        }
        return result;
    }

    private static void AddComponentTexts(List<Source> result, Component component, string origin)
    {
        switch (component)
        {
            case Power power:
                Add(result, power.powerName.ToString(), $"{origin} ({power.name}.powerName)");
                Add(result, power.powerDescription.ToString(), $"{origin} ({power.name}.powerDescription)");
                AddStringProperties(result, power, origin);
                break;
            case PowerComponent powerComponent:
                Add(result, powerComponent.componentName.ToString(), $"{origin} ({powerComponent.name}.componentName)");
                Add(result, powerComponent.description.ToString(), $"{origin} ({powerComponent.name}.description)");
                break;
            case HoverTooltipComponent _:
            case TooltipLinkParser _:
            case CardPickerManager _:
            case NightTurnBanner _:
                AddStringProperties(result, component, origin);
                break;
        }
    }

    private static void AddStringProperties(List<Source> result, UnityEngine.Object target, string origin)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.GetIterator();
        while (property.NextVisible(true))
        {
            if (property.propertyType == SerializedPropertyType.String && TextProperty.IsMatch(property.propertyPath + "."))
            {
                Add(result, property.stringValue, $"{origin} ({target.name}.{property.propertyPath})");
            }
        }
    }

    private static HashSet<string> s_placeholders;

    // An asset field may hold a sample text too (the tooltip of a prefab that code fills): listed with the labels'.
    private static void Add(List<Source> result, string text, string origin)
    {
        s_placeholders ??= Placeholders();
        if (Loc.IsTranslatable(text) && !s_placeholders.Contains(Normalize(text)))
        {
            result.Add(new Source { text = text, origin = origin });
        }
    }

    // ------------------------------------------------------------------------------------------------ labels

    public static IEnumerable<string> PrefabPaths() =>
        AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(path => !Skipped(path)).OrderBy(path => path);

    /// <summary>Every uGUI label authored in a prefab, with the text it holds there.</summary>
    public static List<LabelText> PrefabLabels()
    {
        var result = new List<LabelText>();
        foreach (string path in PrefabPaths())
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                AddLabels(result, prefab, path);
            }
        }
        return result;
    }

    /// <summary>Every uGUI label of the shipped scenes. Opens each scene: the scene that was open is not restored.</summary>
    public static List<LabelText> SceneLabels()
    {
        var result = new List<LabelText>();
        foreach (string scenePath in Scenes)
        {
            foreach (GameObject root in EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single).GetRootGameObjects())
            {
                AddLabels(result, root, scenePath);
            }
        }
        return result;
    }

    private static void AddLabels(List<LabelText> result, GameObject root, string origin)
    {
        foreach (Component label in LabelsUnder(root))
        {
            string text = TextOf(label);
            if (!Loc.IsTranslatable(text))
            {
                continue;
            }
            result.Add(new LabelText
            {
                text = text,
                origin = $"{origin} ({PathOf(label.transform)})",
                localized = label.GetComponent<LocalizedLabel>() != null,
            });
        }
    }

    public static IEnumerable<Component> LabelsUnder(GameObject root)
    {
        foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            yield return label;
        }
        foreach (Text label in root.GetComponentsInChildren<Text>(true))
        {
            yield return label;
        }
    }

    public static string TextOf(Component label) => label is TMP_Text tmp ? tmp.text : label is Text legacy ? legacy.text : null;

    public static string PathOf(Transform transform)
    {
        var names = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
        {
            names.Add(current.name);
        }
        names.Reverse();
        return string.Join("/", names);
    }

    // ------------------------------------------------------------------------------------------------ table

    /// <summary>French → English, straight from the table file (not from the copy the editor may hold in memory).</summary>
    public static Dictionary<string, string> EnglishTable() =>
        LocalizationRuntime.ParseEnglish(File.ReadAllText(TablePath, Encoding.UTF8));

    /// <summary>Texts of labels that are NOT game texts: sample values shown in the editor, replaced by code at runtime.</summary>
    public static HashSet<string> Placeholders()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(PlaceholdersPath))
        {
            return result;
        }
        PlaceholderList list = JsonUtility.FromJson<PlaceholderList>(File.ReadAllText(PlaceholdersPath, Encoding.UTF8));
        foreach (string text in list?.texts ?? new List<string>())
        {
            result.Add(Normalize(text));
        }
        return result;
    }

    public static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static bool Skipped(string path) => AssetSkippedFolders.Any(path.Contains);

    // ------------------------------------------------------------------------------------------------ report

    [Serializable]
    private sealed class Report
    {
        public List<Source> missing = new List<Source>();
        public List<LabelText> undecidedLabels = new List<LabelText>();
        public List<LabelText> labels = new List<LabelText>();
        public List<Source> sources = new List<Source>();
    }

    /// <summary>
    /// Writes <c>Logs/localization-report.json</c>: every source, the ones with no translation, and the labels that are
    /// neither translated nor listed as placeholders. Opens the shipped scenes one after the other.
    /// </summary>
    [MenuItem("Tools/Localization/Write report (opens the scenes)")]
    public static void WriteReport()
    {
        var report = new Report();
        report.sources.AddRange(Code());
        report.sources.AddRange(Computed());
        report.sources.AddRange(Assets());
        report.sources.AddRange(SceneFields());
        report.labels.AddRange(PrefabLabels());
        report.labels.AddRange(SceneLabels());

        Dictionary<string, string> table = EnglishTable();
        var known = new HashSet<string>(table.Keys.Select(Normalize), StringComparer.Ordinal);
        HashSet<string> placeholders = Placeholders();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Source source in report.sources)
        {
            string key = Normalize(source.text);
            if (!known.Contains(key) && seen.Add(key))
            {
                report.missing.Add(source);
            }
        }
        foreach (LabelText label in report.labels)
        {
            string key = Normalize(label.text);
            if (!known.Contains(key) && !placeholders.Contains(key))
            {
                report.undecidedLabels.Add(label);
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/localization-report.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        Debug.Log($"[LOC] Report written: {report.sources.Count} sources, {report.missing.Count} without translation, " +
                  $"{report.labels.Count} labels, {report.undecidedLabels.Count} undecided → Logs/localization-report.json");
    }
}
