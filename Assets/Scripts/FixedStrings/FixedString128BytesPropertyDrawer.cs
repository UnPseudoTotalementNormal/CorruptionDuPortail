#if UNITY_EDITOR

using Unity.Collections;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FixedString128Bytes))]
public class FixedString128BytesPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginChangeCheck();
        string fixedStringValue = EditorGUI.TextField(position, label, property.boxedValue.ToString());

        if (EditorGUI.EndChangeCheck())
        {
            property.boxedValue = new FixedString128Bytes(fixedStringValue);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}
#endif