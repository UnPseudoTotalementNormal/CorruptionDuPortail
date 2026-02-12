using System.Collections.Generic;
using TransformComposition;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TransformCompositorComponent))]
public class TransformCompositorComponentEditor : Editor
{
    private string newLayerName = "New Layer";
    private bool showSettings = false;
    private bool showLayers = false;
    
    private static Dictionary<string, Vector3> editorEulerAngles = new Dictionary<string, Vector3>();
    
    public override void OnInspectorGUI()
    {
        TransformCompositorComponent component = (TransformCompositorComponent)target;

        if (!component)
        {
            return;
        }
        
        // Base Transform Display & Editing
        DisplayLayer(component, TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME, false, false, false);

        showSettings = EditorGUILayout.Foldout(showSettings, "Settings");
        if (showSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            bool newAutoUpdate = EditorGUILayout.Toggle("Auto Update", component.autoUpdate);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Change Auto Update");
                component.autoUpdate = newAutoUpdate;
                EditorUtility.SetDirty(component);
            }

            EditorGUI.BeginChangeCheck();
            bool newDetectExternalChanges =
                EditorGUILayout.Toggle("Detect External Changes", component.detectExternalChanges);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Change Detect External Changes");
                component.detectExternalChanges = newDetectExternalChanges;
                EditorUtility.SetDirty(component);
            }

            EditorGUILayout.LabelField("Disable 'Detect External Changes' if you don't need it for better performance.",
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        showLayers = EditorGUILayout.Foldout(showLayers, "Layers");
        if (showLayers)
        {
            EditorGUI.indentLevel++;
            foreach (var layerName in component.Compositor.GetLayerNames())
            {
                if (layerName == TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME)
                {
                    continue;
                }
                if (DisplayLayer(component, layerName, true, true))
                {
                    break;
                }
            }
            
            newLayerName = GUILayout.TextField(newLayerName);

            if (GUILayout.Button("Add New Layer") && !string.IsNullOrEmpty(newLayerName) && !component.HasLayer(newLayerName))
            {
                Undo.RecordObject(component, "Add New Layer");
                component.GetLayer(newLayerName);
                EditorUtility.SetDirty(component);
            }
            EditorGUI.indentLevel--;
        }
    }

    // Displays the UI for a single layer. Returns true if the layer was removed.
    private static bool DisplayLayer(TransformCompositorComponent component, string layerName, bool showLayerName, bool showRemoveLayerButton, bool displayInsideBox = true)
    {
        TransformLayer layer = component.Compositor.GetLayer(layerName);
        
        string cacheKey = $"{component.GetInstanceID()}_{layerName}";
        if (!editorEulerAngles.ContainsKey(cacheKey))
        {
            editorEulerAngles[cacheKey] = layer.localEulerAngles;
        }
        
        if (displayInsideBox)
        {
            EditorGUILayout.BeginVertical("box");
        }
        if (showLayerName)
        {
            EditorGUILayout.LabelField(layerName, EditorStyles.boldLabel);
        }
                
        EditorGUI.BeginChangeCheck();
        Vector3 newPosition = EditorGUILayout.Vector3Field("Position Offset", layer.localPosition);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Position");
            layer.localPosition = newPosition;
            EditorUtility.SetDirty(component);
        }
                
        EditorGUI.BeginChangeCheck();
        // Display the cached Euler angles instead of reading from layer (avoids gimbal lock visual issues)
        Vector3 newRotation = EditorGUILayout.Vector3Field("Rotation Offset (Euler)", editorEulerAngles[cacheKey]);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Rotation");
            editorEulerAngles[cacheKey] = newRotation;
            layer.localEulerAngles = newRotation;
            EditorUtility.SetDirty(component);
        }
                
        EditorGUI.BeginChangeCheck();
        Vector3 newScale = EditorGUILayout.Vector3Field("Scale Multiplier", layer.localScale);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Scale");
            layer.localScale = newScale;
            EditorUtility.SetDirty(component);
        }
                
        if (showRemoveLayerButton && GUILayout.Button("Remove Layer"))
        {
            Undo.RecordObject(component, "Remove Layer");
            component.RemoveLayer(layerName);
            editorEulerAngles.Remove(cacheKey);
            EditorUtility.SetDirty(component);
            return true; 
        }

        if (displayInsideBox)
        {
            EditorGUILayout.EndVertical();
        }
        return false;
    }
}
