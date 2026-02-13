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
    
    private struct RotationCache
    {
        public Vector3 displayedEulerAngles;  // What we show in the Inspector
        public Quaternion lastQuaternion;     // To detect real changes
    }
    private static Dictionary<string, RotationCache> rotationCache = new();
    private const float ROTATION_CHANGE_THRESHOLD = 0.1f;
    
    private static bool scaleLinked = true;
    
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
            
            GUIStyle indentedStyle = new GUIStyle();
            indentedStyle.margin.left = EditorGUI.indentLevel * 30;
            EditorGUILayout.BeginVertical(indentedStyle);
            
            newLayerName = GUILayout.TextField(newLayerName);

            if (GUILayout.Button("Add New Layer") && !string.IsNullOrEmpty(newLayerName) && !component.HasLayer(newLayerName))
            {
                Undo.RecordObject(component, "Add New Layer");
                component.GetLayer(newLayerName);
                EditorUtility.SetDirty(component);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        if (!component.autoUpdate)
        {
            if (GUILayout.Button("Apply Composed Transform"))
            {
                component.ApplyComposedTransform();
            }
        }
    }

    // Displays the UI for a single layer. Returns true if the layer was removed.
    private static bool DisplayLayer(TransformCompositorComponent component, string layerName, bool showLayerName, bool showRemoveLayerButton, bool displayInsideBox = true)
    {
        TransformLayer layer = component.Compositor.GetLayer(layerName);
        
        string cacheKey = $"{component.GetInstanceID()}_{layerName}";
        
        if (!rotationCache.ContainsKey(cacheKey))
        {
            rotationCache[cacheKey] = new RotationCache
            {
                displayedEulerAngles = layer.localEulerAngles,
                lastQuaternion = layer.localRotation
            };
        }
        
        // Detect real rotation changes by comparing Quaternions
        RotationCache cache = rotationCache[cacheKey];
        float angleDifference = Quaternion.Angle(cache.lastQuaternion, layer.localRotation);
        
        // If the rotation actually changed (external modification via code, gizmo, animation, etc.)
        if (angleDifference > ROTATION_CHANGE_THRESHOLD)
        {
            cache.displayedEulerAngles = layer.localEulerAngles;
            cache.lastQuaternion = layer.localRotation;
            rotationCache[cacheKey] = cache;
        }
        
        if (displayInsideBox)
        {
            GUIStyle indentedBox = new GUIStyle("box");
            indentedBox.margin.left = EditorGUI.indentLevel * 30;
            EditorGUILayout.BeginVertical(indentedBox);
        }
        else
        {
            GUIStyle indentedStyle = new GUIStyle();
            indentedStyle.margin.left = EditorGUI.indentLevel * 30;
            EditorGUILayout.BeginVertical(indentedStyle);
        }
        if (showLayerName)
        {
            EditorGUILayout.LabelField(layerName, EditorStyles.boldLabel);
        }
                
        EditorGUI.BeginChangeCheck();
        Vector3 newPosition = EditorGUILayout.Vector3Field("Position", layer.localPosition);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Position");
            layer.localPosition = newPosition;
            EditorUtility.SetDirty(component);
        }
                
        EditorGUI.BeginChangeCheck();
        Vector3 newRotation = EditorGUILayout.Vector3Field("Rotation", cache.displayedEulerAngles);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Rotation");
            layer.localEulerAngles = newRotation;
            cache.displayedEulerAngles = newRotation;
            cache.lastQuaternion = layer.localRotation;
            rotationCache[cacheKey] = cache;
            EditorUtility.SetDirty(component);
        }
        
        EditorGUI.BeginChangeCheck();
        Vector3 newScale = EditorGUILayout.Vector3Field("Scale", layer.localScale);
        
        Rect lastRect = GUILayoutUtility.GetLastRect();
        float labelWidth = EditorGUIUtility.labelWidth;
        Rect linkRect = new Rect(
            lastRect.x + labelWidth - 21,
            lastRect.y + 0,
            16,
            16
        );
        GUIContent linkIcon = EditorGUIUtility.IconContent(scaleLinked ? "d_Linked" : "d_UnLinked");
        if (GUI.Button(linkRect, linkIcon, GUIStyle.none))
        {
            scaleLinked = !scaleLinked;
        }
        
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Scale");
            
            if (scaleLinked)
            {
                Vector3 oldScale = layer.localScale;
                Vector3 scaleDelta = newScale - oldScale;
                
                float delta = Mathf.Max(Mathf.Abs(scaleDelta.x), Mathf.Abs(scaleDelta.y), Mathf.Abs(scaleDelta.z));
                
                if (delta > 0.0001f)
                {
                    newScale = oldScale + Vector3.one * (delta * Mathf.Sign(scaleDelta.x + scaleDelta.y + scaleDelta.z));
                }
            }
            
            layer.localScale = newScale;
            EditorUtility.SetDirty(component);
        }
                
        if (showRemoveLayerButton && GUILayout.Button("Remove Layer"))
        {
            Undo.RecordObject(component, "Remove Layer");
            component.RemoveLayer(layerName);
            rotationCache.Remove(cacheKey);
            EditorUtility.SetDirty(component);
            
            EditorGUILayout.EndVertical();
            return true; 
        }

        EditorGUILayout.EndVertical();
        return false;
    }
}
