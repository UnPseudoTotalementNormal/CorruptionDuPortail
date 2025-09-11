#if UNITY_EDITOR

using Unity.Collections;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FixedString4096Bytes))]
public class FixedString4096BytesPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        float minHeight = EditorGUIUtility.singleLineHeight + 4f;
        float height = EditorPrefs.GetFloat($"FixedString4096Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f);
        var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        float labelWidth = EditorGUIUtility.labelWidth * 0.925f;
        Rect labelRect = new Rect(position.x, position.y, labelWidth, EditorGUIUtility.singleLineHeight);
        Rect textRect = new Rect(position.x + labelWidth, position.y, position.width - labelWidth, height);

        EditorGUI.LabelField(labelRect, label);

        EditorGUI.BeginChangeCheck();
        var oldValue = property.boxedValue.ToString();
        var newValue = EditorGUI.TextArea(textRect, oldValue, style);
        if (EditorGUI.EndChangeCheck())
        {
            property.boxedValue = new FixedString4096Bytes(newValue);
        }

        // Handle de redimensionnement sous la TextArea
        var resizeRect = new Rect(textRect.x, textRect.yMax, textRect.width, 5f);
        EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeVertical);
        if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
        {
            GUIUtility.hotControl = controlId;
            Event.current.Use();
        }
        if (GUIUtility.hotControl == controlId)
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                height += Event.current.delta.y;
                height = Mathf.Max(minHeight, height);
                EditorPrefs.SetFloat($"FixedString4096Bytes_Height_{property.propertyPath}", height);
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                Event.current.Use();
            }
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorPrefs.GetFloat($"FixedString4096Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f);
        return Mathf.Max(height, EditorGUIUtility.singleLineHeight) + 7f;
    }
}
#endif