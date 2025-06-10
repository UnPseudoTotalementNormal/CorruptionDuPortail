#if UNITY_EDITOR

#region

using Unity.Collections;
using UnityEditor;
using UnityEngine;

#endregion

/// <summary>
/// PropertyDrawer for a FixedString64Bytes
/// </summary>
[CustomPropertyDrawer(typeof(FixedString64Bytes))]
public class FixedString64BytesPropertyDrawer : PropertyDrawer
{
    #region Public methods

    /// <summary>
    /// Called when the UI is drawn
    /// </summary>
    /// <param name="position">The position of the field</param>
    /// <param name="property">The property to serialize</param>
    /// <param name="label">The text</param>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        float minHeight = EditorGUIUtility.singleLineHeight + 4f;
        float height = EditorPrefs.GetFloat($"FixedString64Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f);
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
            property.boxedValue = new FixedString64Bytes(newValue);
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
                EditorPrefs.SetFloat($"FixedString64Bytes_Height_{property.propertyPath}", height);
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                Event.current.Use();
            }
        }
    }

    /// <summary>
    /// Ensures the field will stay at the proper position
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="label">The text</param>
    /// <returns>The proper height of the field</returns>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorPrefs.GetFloat($"FixedString64Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f);
        return Mathf.Max(height, EditorGUIUtility.singleLineHeight) + 7f;
    }

    #endregion
}
#endif