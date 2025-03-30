using Characters.Powers;
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;

[CustomEditor(typeof(PowerDataObject))]
public class PowerDataObjectEditor : Editor
{
    private Type[] powerTypes;
    private string[] powerTypeNames;
    private int selectedIndex;

    private void OnEnable()
    {
        powerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(Power)) && !type.IsAbstract)
            .ToArray();

        powerTypeNames = powerTypes.Select(type => type.Name).ToArray();
    }

    public override void OnInspectorGUI()
    {
        PowerDataObject powerDataObject = (PowerDataObject)target;

        if (powerDataObject.power == null)
        {
            selectedIndex = EditorGUILayout.Popup("Power Type", selectedIndex, powerTypeNames);

            if (GUILayout.Button("Create Power"))
            {
                powerDataObject.power = (Power)Activator.CreateInstance(powerTypes[selectedIndex]);
            }
        }
        else
        {
            EditorGUILayout.LabelField("Power Type", powerDataObject.power.GetType().Name);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("power"), true);
            serializedObject.ApplyModifiedProperties();
            
            if (GUILayout.Button("Remove Power"))
            {
                powerDataObject.power = null;
            }
        }

        EditorUtility.SetDirty(target);
    }
}
