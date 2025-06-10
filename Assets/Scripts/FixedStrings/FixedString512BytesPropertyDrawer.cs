#if UNITY_EDITOR

using Unity.Collections;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FixedString512Bytes))]
public class FixedString512BytesPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginChangeCheck();
        string fixedStringValue = EditorGUI.TextField(position, label, property.boxedValue.ToString());

        if (EditorGUI.EndChangeCheck())
        {
            property.boxedValue = new FixedString512Bytes(fixedStringValue);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}
#endif