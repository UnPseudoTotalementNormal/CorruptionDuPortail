using System.Collections.Generic;
using TransformComposition;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TransformCompositorComponent))]
public class TransformCompositorComponentEditor : Editor
{
    private string newLayerName = "New Layer";
    private bool showSettings = false;
    private static bool showLayers = false;
    private bool showComposedTransform = false;
    private bool showLayerGizmos = false;
    private float gizmoSize = 1.0f;
    
    // Scene editing tracking
    private static string activeLayerForSceneEdit = null;
    private static TransformCompositorComponent activeComponent = null;
    
    private struct RotationCache
    {
        public Vector3 displayedEulerAngles;  // What we show in the Inspector
        public Quaternion lastQuaternion;     // To detect real changes
    }
    private static Dictionary<string, RotationCache> rotationCache = new();
    private const float ROTATION_CHANGE_THRESHOLD = 0.1f;
    
    private static bool scaleLinked = true;
    
    private const string SHOW_COMPOSED_PREF_KEY = "TransformCompositor_ShowComposed";
    private const string SHOW_LAYER_GIZMOS_PREF_KEY = "TransformCompositor_ShowLayerGizmos";
    private const string GIZMO_SIZE_PREF_KEY = "TransformCompositor_GizmoSize";
    
    private void OnEnable()
    {
        showComposedTransform = EditorPrefs.GetBool(SHOW_COMPOSED_PREF_KEY, false);
        showLayerGizmos = EditorPrefs.GetBool(SHOW_LAYER_GIZMOS_PREF_KEY, false);
        gizmoSize = EditorPrefs.GetFloat(GIZMO_SIZE_PREF_KEY, 2.5f);
    }
    
    private void OnDisable()
    {
        TransformCompositorComponent component = (TransformCompositorComponent)target;
        if (activeComponent == component)
        {
            activeLayerForSceneEdit = null;
            activeComponent = null;
            if (component != null)
            {
                component.ActiveLayerForSceneEdit = null;
            }
        }
    }
    
    private void OnSceneGUI()
    {
        TransformCompositorComponent component = (TransformCompositorComponent)target;
        
        if (component != null && !string.IsNullOrEmpty(component.ActiveLayerForSceneEdit))
        {
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = new Color(0.3f, 0.7f, 1f);
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            
            Handles.Label(
                component.transform.position + Vector3.up * 2f,
                $"✏ Editing Layer: {component.ActiveLayerForSceneEdit}",
                labelStyle
            );
        }
        
        if (showLayerGizmos && component != null)
        {
            DrawLayerGizmos(component);
        }
    }
    
    private void DrawLayerGizmos(TransformCompositorComponent component)
    {
        var layerNames = new List<string>(component.Compositor.GetLayerNames());
        int layerCount = layerNames.Count;
        
        if (layerCount == 0) return;
        
        for (int i = 0; i < layerCount; i++)
        {
            string layerName = layerNames[i];
            
            TransformCompositor.ComposedTransform composedTransform = 
                component.Compositor.GetComposedTransformIncluding(layerName);
            
            Vector3 worldPosition;
            Quaternion worldRotation;
            
            if (component.transform.parent != null)
            {
                worldPosition = component.transform.parent.TransformPoint(composedTransform.localPosition);
                worldRotation = component.transform.parent.rotation * composedTransform.localRotation;
            }
            else
            {
                worldPosition = composedTransform.localPosition;
                worldRotation = composedTransform.localRotation;
            }
            
            Color layerColor = GetLayerColor(i, layerCount, layerName);
            float handleSize = HandleUtility.GetHandleSize(worldPosition) * 0.15f * gizmoSize;
            
            Handles.color = layerColor;
            Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, handleSize, EventType.Repaint);
            
            Vector3 forwardDirection = worldRotation * Vector3.forward;
            float arrowLength = handleSize * 2f;
            Handles.color = layerColor;
            Handles.ArrowHandleCap(0, worldPosition, Quaternion.LookRotation(forwardDirection), 
                arrowLength, EventType.Repaint);
            
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = layerColor;
            labelStyle.fontSize = 10;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.UpperCenter;
            
            Vector3 labelOffset = Vector3.down * (handleSize * 1.5f);
            Handles.Label(worldPosition + labelOffset, layerName, labelStyle);
            
            Handles.color = new Color(layerColor.r, layerColor.g, layerColor.b, 0.3f);
            if (Handles.Button(worldPosition, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap))
            {
                if (activeComponent != null && activeComponent != component)
                {
                    activeComponent.ActiveLayerForSceneEdit = null;
                }
                
                activeLayerForSceneEdit = layerName;
                activeComponent = component;
                component.ActiveLayerForSceneEdit = layerName;
                
                if (!component.detectExternalChanges)
                {
                    component.detectExternalChanges = true;
                }
                
                EditorUtility.SetDirty(component);
                Repaint();
            }
        }
    }
    
    private Color GetLayerColor(int index, int totalLayers, string layerName)
    {
        if (layerName == TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME)
        {
            return new Color(0.8f, 0.8f, 0.8f, 1f);
        }
        
        float hue = (float)index / Mathf.Max(1, totalLayers - 1);
        Color color = Color.HSVToRGB(hue, 0.8f, 1f);
        return color;
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

            EditorGUI.BeginChangeCheck();
            showLayerGizmos = EditorGUILayout.Toggle(
                new GUIContent("Show Layer Gizmos", 
                    "Display visual gizmos in the Scene view showing the composed position and rotation of each layer."), 
                showLayerGizmos);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(SHOW_LAYER_GIZMOS_PREF_KEY, showLayerGizmos);
                SceneView.RepaintAll();
            }

            if (showLayerGizmos)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                gizmoSize = EditorGUILayout.Slider(
                    new GUIContent("Gizmo Size", 
                        "Adjusts the size of the layer gizmos in the Scene view."), 
                    gizmoSize, 0.1f, 5f);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetFloat(GIZMO_SIZE_PREF_KEY, gizmoSize);
                    SceneView.RepaintAll();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        showLayers = EditorGUILayout.Foldout(showLayers, "Layers");
        if (showLayers)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.HelpBox("Layers are evaluated in order from top to bottom. Use ↑↓ buttons to reorder.", MessageType.Info);
            
            var layerNamesList = new List<string>(component.Compositor.GetLayerNames());
            
            int index = 0;
            foreach (var layerName in layerNamesList)
            {
                if (layerName == TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME)
                {
                    if (showComposedTransform)
                    {
                        DisplayLayerWithOrder(component, layerName, index, false, false);
                    }
                    index++;
                    continue;
                }
                
                bool canMoveUp = index > 1; // Can't move above base layer (index 0)
                bool canMoveDown = index < component.Compositor.GetLayerCount() - 1;
                
                if (DisplayLayerWithOrder(component, layerName, index, canMoveUp, canMoveDown))
                {
                    break;
                }
                index++;
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

    /// <summary>
    /// Core function that displays a layer with all possible UI options.
    /// Returns true if the layer was removed.
    /// </summary>
    private static bool DisplayLayerCore(
        TransformCompositorComponent component,
        string layerName,
        bool showLayerName = false,
        bool showOrderNumber = false,
        int orderIndex = 0,
        bool showReorderButtons = false,
        bool canMoveUp = false,
        bool canMoveDown = false,
        bool showRemoveButton = false,
        bool displayInsideBox = true,
        bool showCompositeModeField = true)
    {
        TransformLayer layer = component.Compositor.GetLayer(layerName);
        bool isBaseLayer = layerName == TransformCompositorComponent.BASE_TRANSFORM_LAYER_NAME;
        
        // Initialize rotation cache
        string cacheKey = $"{component.GetInstanceID()}_{layerName}";
        if (!rotationCache.ContainsKey(cacheKey))
        {
            rotationCache[cacheKey] = new RotationCache
            {
                displayedEulerAngles = layer.localEulerAngles,
                lastQuaternion = layer.localRotation
            };
        }
        
        RotationCache cache = rotationCache[cacheKey];
        float angleDifference = Quaternion.Angle(cache.lastQuaternion, layer.localRotation);
        
        if (angleDifference > ROTATION_CHANGE_THRESHOLD)
        {
            cache.displayedEulerAngles = layer.localEulerAngles;
            cache.lastQuaternion = layer.localRotation;
            rotationCache[cacheKey] = cache;
        }
        
        // Begin vertical container
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
        
        // Header
        if (showOrderNumber || showReorderButtons)
        {
            EditorGUILayout.BeginHorizontal();
            
            string displayName = isBaseLayer ? $"[{orderIndex}] {layerName} (BASE)" : $"[{orderIndex}] {layerName}";
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            if (layer.compositeMode == CompositeMode.Local)
            {
                headerStyle.normal.textColor = new Color(0.3f, 0.7f, 1f);
            }
            EditorGUILayout.LabelField(displayName, headerStyle);
            
            GUILayout.FlexibleSpace();
            
            // Scene Edit Toggle Button
            if (showReorderButtons && !isBaseLayer)
            {
                bool isActiveForSceneEdit = (activeComponent == component && activeLayerForSceneEdit == layerName);
                
                GUIContent iconContent = EditorGUIUtility.IconContent("Transform Icon");
                iconContent.tooltip = isActiveForSceneEdit 
                    ? "Click to stop editing this layer in Scene view" 
                    : "Click to edit this layer using Scene view gizmos";
                
                Color originalColor = GUI.backgroundColor;
                if (isActiveForSceneEdit)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 1f); // Highlight in blue
                }
                
                if (GUILayout.Button(iconContent, GUILayout.Width(25), GUILayout.Height(18)))
                {
                    if (isActiveForSceneEdit)
                    {
                        // Deactivate
                        activeLayerForSceneEdit = null;
                        activeComponent = null;
                        component.ActiveLayerForSceneEdit = null;
                    }
                    else
                    {
                        // Activate this layer for scene editing
                        // First deactivate previous if any
                        if (activeComponent != null)
                        {
                            activeComponent.ActiveLayerForSceneEdit = null;
                        }
                        
                        activeLayerForSceneEdit = layerName;
                        activeComponent = component;
                        component.ActiveLayerForSceneEdit = layerName;
                        
                        // Ensure detectExternalChanges is enabled
                        if (!component.detectExternalChanges)
                        {
                            component.detectExternalChanges = true;
                        }
                        

                        // Focus Scene view
                        SceneView sceneView = SceneView.lastActiveSceneView;
                        if (sceneView != null)
                        {
                            sceneView.Focus();
                            Selection.activeGameObject = component.gameObject;
                        }
                    }
                    EditorUtility.SetDirty(component);
                }
                
                GUI.backgroundColor = originalColor;
            }
            
            // Reorder buttons
            if (showReorderButtons && !isBaseLayer)
            {
                GUI.enabled = canMoveUp;
                if (GUILayout.Button("↑", GUILayout.Width(25)))
                {
                    Undo.RecordObject(component, "Move Layer Up");
                    component.MoveLayerUp(layerName);
                    EditorUtility.SetDirty(component);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return false;
                }
                GUI.enabled = canMoveDown;
                if (GUILayout.Button("↓", GUILayout.Width(25)))
                {
                    Undo.RecordObject(component, "Move Layer Down");
                    component.MoveLayerDown(layerName);
                    EditorUtility.SetDirty(component);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return false;
                }
                GUI.enabled = true;
            }
            
            EditorGUILayout.EndHorizontal();
        }
        else if (showLayerName)
        {
            EditorGUILayout.LabelField(layerName, EditorStyles.boldLabel);
        }
        
        // Composite Mode
        if (showCompositeModeField && !isBaseLayer)
        {
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
        }
        
        // Position
        EditorGUI.BeginChangeCheck();
        Vector3 newPosition = EditorGUILayout.Vector3Field("Position", layer.localPosition);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Layer Position");
            layer.localPosition = newPosition;
            EditorUtility.SetDirty(component);
        }
        
        // Rotation
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
        
        // Scale
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
        
        if (showRemoveButton && !isBaseLayer && GUILayout.Button("Remove Layer"))
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

    /// <summary>
    /// Displays a layer with order number and reorder buttons.
    /// Returns true if the layer was removed.
    /// </summary>
    private static bool DisplayLayerWithOrder(TransformCompositorComponent component, string layerName, int index, bool canMoveUp, bool canMoveDown)
    {
        return DisplayLayerCore(
            component: component,
            layerName: layerName,
            showOrderNumber: true,
            orderIndex: index,
            showReorderButtons: true,
            canMoveUp: canMoveUp,
            canMoveDown: canMoveDown,
            showRemoveButton: true,
            displayInsideBox: true,
            showCompositeModeField: true
        );
    }

    /// <summary>
    /// Displays the UI for a single layer.
    /// Returns true if the layer was removed.
    /// </summary>
    private static bool DisplayLayer(TransformCompositorComponent component, string layerName, bool showLayerName, bool showRemoveLayerButton, bool displayInsideBox = true)
    {
        return DisplayLayerCore(
            component: component,
            layerName: layerName,
            showLayerName: showLayerName,
            showRemoveButton: showRemoveLayerButton,
            displayInsideBox: displayInsideBox,
            showCompositeModeField: true
        );
    }
}
