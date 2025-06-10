#if UNITY_EDITOR

using Unity.Collections;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FixedString4096Bytes))]
public class FixedString4096BytesPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginChangeCheck();
        string fixedStringValue = EditorGUI.TextField(position, label, property.boxedValue.ToString());

        if (EditorGUI.EndChangeCheck())
        {
            property.boxedValue = new FixedString4096Bytes(fixedStringValue);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}
#endif