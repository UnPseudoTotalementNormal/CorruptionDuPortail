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
    private bool showComposedTransform = false;
    
    private struct RotationCache
    {
        public Vector3 displayedEulerAngles;  // What we show in the Inspector
        public Quaternion lastQuaternion;     // To detect real changes
    }
    private static Dictionary<string, RotationCache> rotationCache = new();
    private const float ROTATION_CHANGE_THRESHOLD = 0.1f;
    
    private static bool scaleLinked = true;
    
    private const string SHOW_COMPOSED_PREF_KEY = "TransformCompositor_ShowComposed";
    
    private void OnEnable()
    {
        showComposedTransform = EditorPrefs.GetBool(SHOW_COMPOSED_PREF_KEY, false);
    }
    
    public override void OnInspectorGUI()
    {
        TransformCompositorComponent component = (TransformCompositorComponent)target;

        if (!component)
        {
            return;
        }
        
        // Base Transform Display & Editing
        if (showComposedTransform)
        {
            DisplayComposedTransform(component);
        }
        else
        {
            DisplayLayer(component, TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME, false, false, false);
        }

        showSettings = EditorGUILayout.Foldout(showSettings, "Settings");
        if (showSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            showComposedTransform = EditorGUILayout.Toggle(
                new GUIContent("Show Composed Transform", 
                    "If true the top section will show the composed transform, if false it will show the base 'Transform' layer only."), 
                showComposedTransform);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(SHOW_COMPOSED_PREF_KEY, showComposedTransform);
            }
            
            EditorGUI.BeginChangeCheck();
            bool newAutoUpdate = EditorGUILayout.Toggle(
                new GUIContent("Auto Update", 
                    "When enabled, the composed transform is automatically applied to the GameObject each frame. Disable if you want to manually control when the transform is updated."), 
                component.autoUpdate);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Change Auto Update");
                component.autoUpdate = newAutoUpdate;
                EditorUtility.SetDirty(component);
            }

            EditorGUI.BeginChangeCheck();
            bool newDetectExternalChanges = EditorGUILayout.Toggle(
                new GUIContent("Detect External Changes", 
                    "Disable 'Detect External Changes' if you don't need it for better performance."), 
                component.detectExternalChanges);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Change Detect External Changes");
                component.detectExternalChanges = newDetectExternalChanges;
                EditorUtility.SetDirty(component);
            }

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

    /// <summary>
    /// Displays and edits the composed transform (result of all layers combined).
    /// Changes are applied to the base "Transform" layer only.
    /// </summary>
    private static void DisplayComposedTransform(TransformCompositorComponent component)
    {
        TransformCompositor.ComposedTransform composed = component.GetComposedTransform();
        TransformLayer baseLayer = component.Compositor.GetLayer(TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME);
        
        string cacheKey = $"{component.GetInstanceID()}_ComposedTransform";
        
        if (!rotationCache.ContainsKey(cacheKey))
        {
            rotationCache[cacheKey] = new RotationCache
            {
                displayedEulerAngles = composed.localEulerAngles,
                lastQuaternion = composed.localRotation
            };
        }
        
        // Detect real rotation changes
        RotationCache cache = rotationCache[cacheKey];
        float angleDifference = Quaternion.Angle(cache.lastQuaternion, composed.localRotation);
        
        if (angleDifference > ROTATION_CHANGE_THRESHOLD)
        {
            cache.displayedEulerAngles = composed.localEulerAngles;
            cache.lastQuaternion = composed.localRotation;
            rotationCache[cacheKey] = cache;
        }
        
        GUIStyle indentedStyle = new GUIStyle();
        indentedStyle.margin.left = EditorGUI.indentLevel * 30;
        EditorGUILayout.BeginVertical(indentedStyle);
        
        // Position
        EditorGUI.BeginChangeCheck();
        Vector3 newComposedPosition = EditorGUILayout.Vector3Field("Position", composed.localPosition);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Composed Position");
            
            var otherContribution = GetOtherLayersContribution(component);
            Vector3 transformedOtherPosition = baseLayer.localRotation * Vector3.Scale(otherContribution.localPosition, baseLayer.localScale);
            
            baseLayer.localPosition = newComposedPosition - transformedOtherPosition;
            EditorUtility.SetDirty(component);
        }
        
        // Rotation
        EditorGUI.BeginChangeCheck();
        Vector3 newComposedRotation = EditorGUILayout.Vector3Field("Rotation", cache.displayedEulerAngles);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Composed Rotation");
            
            Quaternion desiredComposedRotation = Quaternion.Euler(newComposedRotation);
            Quaternion otherLayersRotation = GetOtherLayersContribution(component).localRotation;
            baseLayer.localRotation = desiredComposedRotation * Quaternion.Inverse(otherLayersRotation);
            
            cache.displayedEulerAngles = newComposedRotation;
            composed = component.GetComposedTransform(); // Recalculate
            cache.lastQuaternion = composed.localRotation;
            rotationCache[cacheKey] = cache;
            
            EditorUtility.SetDirty(component);
        }
        
        // Scale
        EditorGUI.BeginChangeCheck();
        Vector3 newComposedScale = EditorGUILayout.Vector3Field("Scale", composed.localScale);
        
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
            Undo.RecordObject(component, "Change Composed Scale");
            
            if (scaleLinked)
            {
                Vector3 oldScale = composed.localScale;
                Vector3 scaleDelta = newComposedScale - oldScale;
                
                float delta = Mathf.Max(Mathf.Abs(scaleDelta.x), Mathf.Abs(scaleDelta.y), Mathf.Abs(scaleDelta.z));
                
                if (delta > 0.0001f)
                {
                    newComposedScale = oldScale + Vector3.one * (delta * Mathf.Sign(scaleDelta.x + scaleDelta.y + scaleDelta.z));
                }
            }
            
            Vector3 otherLayersScale = GetOtherLayersContribution(component).localScale;
            baseLayer.localScale = new Vector3(
                otherLayersScale.x != 0 ? newComposedScale.x / otherLayersScale.x : 1f,
                otherLayersScale.y != 0 ? newComposedScale.y / otherLayersScale.y : 1f,
                otherLayersScale.z != 0 ? newComposedScale.z / otherLayersScale.z : 1f
            );
            
            EditorUtility.SetDirty(component);
        }
        
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Calculates the contribution of all layers except the base "Transform" layer.
    /// </summary>
    private static TransformCompositor.ComposedTransform GetOtherLayersContribution(TransformCompositorComponent component)
    {
        Vector3 compositePosition = Vector3.zero;
        Quaternion compositeRotation = Quaternion.identity;
        Vector3 compositeScale = Vector3.one;

        foreach (var (layerName, layer) in component.Compositor.GetAllLayersNamesAndTransforms())
        {
            if (layerName == TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME)
            {
                continue;
            }

            if (layer.compositeMode == CompositeMode.Global)
            {
                compositePosition += layer.localPosition;
                compositeRotation = layer.localRotation * compositeRotation;
                compositeScale.x *= layer.localScale.x;
                compositeScale.y *= layer.localScale.y;
                compositeScale.z *= layer.localScale.z;
            }
            else // CompositeMode.Local
            {
                Vector3 rotatedPosition = compositeRotation * layer.localPosition;
                Vector3 scaledPosition = Vector3.Scale(rotatedPosition, compositeScale);
                compositePosition += scaledPosition;
                
                compositeRotation *= layer.localRotation;
                
                compositeScale.x *= layer.localScale.x;
                compositeScale.y *= layer.localScale.y;
                compositeScale.z *= layer.localScale.z;
            }
        }

        return new TransformCompositor.ComposedTransform
        {
            localPosition = compositePosition,
            localRotation = compositeRotation,
            localScale = compositeScale
        };
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
        CompositeMode newCompositeMode = (CompositeMode)EditorGUILayout.EnumPopup(
            new GUIContent("Composite Mode", 
                "Global: Rotations applied around global axes (position added, rotation multiplied left, scale multiplied).\n" +
                "Local: Applied in local space of previous layers (position rotated & scaled, rotation multiplied right, like parent-child)."), 
            layer.compositeMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Composite Mode");
            layer.compositeMode = newCompositeMode;
            EditorUtility.SetDirty(component);
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
