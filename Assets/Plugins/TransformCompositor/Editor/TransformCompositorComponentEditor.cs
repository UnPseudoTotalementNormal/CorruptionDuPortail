using System.Linq;
using TransformComposition;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TransformCompositorComponent))]
public class TransformCompositorComponentEditor : Editor
{
    private string newLayerName = "New Layer";
    
    public override void OnInspectorGUI()
    {
        TransformCompositorComponent component = (TransformCompositorComponent)target;

        EditorGUILayout.LabelField("Transform Compositor", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (!component)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        bool newAutoUpdate = EditorGUILayout.Toggle("Auto Update", component.autoUpdate);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(component, "Change Auto Update");
            component.autoUpdate = newAutoUpdate;
            EditorUtility.SetDirty(component);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);

        if (component.Compositor != null)
        {
            foreach (var layerName in component.Compositor.GetLayerNames())
            {
                TransformLayer layer = component.Compositor.GetLayer(layerName);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(layerName, EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = EditorGUILayout.Vector3Field("Position Offset", layer.localPosition);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Change Layer Position");
                    layer.localPosition = newPosition;
                    EditorUtility.SetDirty(component);
                }
                
                EditorGUI.BeginChangeCheck();
                Vector3 newRotation = EditorGUILayout.Vector3Field("Rotation Offset (Euler)", layer.localEulerAngles);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Change Layer Rotation");
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
                
                if (GUILayout.Button("Remove Layer"))
                {
                    Undo.RecordObject(component, "Remove Layer");
                    component.RemoveLayer(layerName);
                    EditorUtility.SetDirty(component);
                    break; 
                }
                EditorGUILayout.EndVertical();
            }


            newLayerName = GUILayout.TextField(newLayerName);

            if (GUILayout.Button("Add New Layer") && !string.IsNullOrEmpty(newLayerName) && !component.HasLayer(newLayerName))
            {
                Undo.RecordObject(component, "Add New Layer");
                component.GetLayer(newLayerName);
                EditorUtility.SetDirty(component);
            }
        }
    }
}
