using System.Collections.Generic;
using System.Linq;
using Localization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// T36: puts a <see cref="LocalizedLabel"/> on every uGUI label of the prefabs and the shipped scenes whose authored
/// text has an entry in the translation table and no component yet. Run it after writing a new label and its
/// translation (<c>Tools &gt; Localization</c>); <c>LocalizationCoverageTests</c> fails until it has been run.
///
/// <para>A label that belongs to a nested prefab is handled in that prefab's own asset; it gets a component on the
/// outer prefab or the scene only when its text is written there (an override the inner prefab knows nothing of).</para>
/// </summary>
public static class LocalizedLabelTool
{
    [MenuItem("Tools/Localization/Add LocalizedLabel to translated labels (opens the scenes)")]
    public static void AddToTranslatedLabels()
    {
        int added = Run(LocalizationSources.Scenes);
        Debug.Log($"[LOC] LocalizedLabel added to {added} label(s).");
    }

    /// <summary>Adds the missing components and saves the assets it changed. Returns how many it added.</summary>
    public static int Run(IEnumerable<string> scenePaths)
    {
        var translated = new HashSet<string>(LocalizationSources.EnglishTable().Keys.Select(LocalizationSources.Normalize));
        int added = 0;

        foreach (string path in LocalizationSources.PrefabPaths())
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || !LocalizationSources.LabelsUnder(asset).Any(label => Needs(label, translated)))
            {
                continue;
            }
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int inPrefab = AddUnder(contents, translated, path);
            if (inPrefab > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                added += inPrefab;
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        AssetDatabase.SaveAssets();

        foreach (string scenePath in scenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int inScene = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                inScene += AddUnder(root, translated, scenePath);
            }
            if (inScene > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                added += inScene;
            }
        }
        return added;
    }

    private static int AddUnder(GameObject root, HashSet<string> translated, string origin)
    {
        int added = 0;
        foreach (Component label in LocalizationSources.LabelsUnder(root).ToList())
        {
            if (!Needs(label, translated))
            {
                continue;
            }
            label.gameObject.AddComponent<LocalizedLabel>();
            Debug.Log($"[LOC] + LocalizedLabel: {origin} ({LocalizationSources.PathOf(label.transform)}) \"{LocalizationSources.TextOf(label)}\"");
            added++;
        }
        return added;
    }

    /// <summary>True for a label whose text is translated, that has no component, and that is this asset's to handle.</summary>
    public static bool Needs(Component label, HashSet<string> translated)
    {
        if (label.GetComponent<LocalizedLabel>() != null)
        {
            return false;
        }
        if (!translated.Contains(LocalizationSources.Normalize(LocalizationSources.TextOf(label))))
        {
            return false;
        }
        // Part of a nested prefab (or of a prefab instance in a scene) whose own text is translated too: that
        // prefab's asset carries the component, and this instance inherits it.
        Component inner = PrefabUtility.GetCorrespondingObjectFromSource(label);
        return inner == null || !translated.Contains(LocalizationSources.Normalize(LocalizationSources.TextOf(inner)));
    }
}
