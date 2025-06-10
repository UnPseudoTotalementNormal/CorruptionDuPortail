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
        // Récupère la hauteur sauvegardée ou valeur par défaut
        float height = EditorPrefs.GetFloat($"FixedString64Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f);
        var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        // Affiche le label au-dessus
        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, label);
        position.y += EditorGUIUtility.singleLineHeight + 2f;
        position.height = height;

        EditorGUI.BeginChangeCheck();
        var oldValue = property.boxedValue.ToString();
        var newValue = EditorGUI.TextArea(position, oldValue, style);
        if (EditorGUI.EndChangeCheck())
        {
            property.boxedValue = new FixedString64Bytes(newValue);
        }

        // Permet le redimensionnement avec un handle
        var resizeRect = new Rect(position.x, position.yMax, position.width, 5f);
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
                height = Mathf.Max(EditorGUIUtility.singleLineHeight * 2f, height);
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
        // Hauteur dynamique sauvegardée
        return EditorPrefs.GetFloat($"FixedString64Bytes_Height_{property.propertyPath}", EditorGUIUtility.singleLineHeight * 3f) + EditorGUIUtility.singleLineHeight + 7f;
    }

    #endregion
}
#endif