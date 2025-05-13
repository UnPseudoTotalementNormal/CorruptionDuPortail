#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(PolymorphicAttribute), true)]
public class PolymorphicPropertyDrawer : PropertyDrawer
{
	static Type[] objectiveTypes;

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
	{
		return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property);
	}

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		EditorGUI.BeginProperty(position, label, property);

		Type parentType;

		if (fieldInfo.FieldType.IsGenericType && fieldInfo.FieldType.GetGenericTypeDefinition() == typeof(List<>))
			parentType = fieldInfo.FieldType.GetGenericArguments()[0];
		else if (fieldInfo.FieldType.IsArray)
			parentType = fieldInfo.FieldType.GetElementType();
		else
			parentType = fieldInfo.FieldType;

		if (!Attribute.IsDefined(fieldInfo, typeof(SerializeReference)))
			throw new InvalidOperationException("Cannot use polymorphic serialization attribute on a field without the SerializeReference attribute!");

		objectiveTypes ??= AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(a => a.GetTypes())
			.Where(t => !t.IsAbstract && parentType.IsAssignableFrom(t))
			.ToArray();

		string[] typeNames = objectiveTypes.Select(t => t.Name).ToArray();
		int index = Mathf.Max(0, Array.IndexOf(objectiveTypes, property.managedReferenceValue?.GetType()));

		const string noTypeStr = "(no objective)";

		if (property.managedReferenceValue is null)
			typeNames = typeNames.Prepend(noTypeStr).ToArray();

		Rect typeSelectRect = position;
		typeSelectRect.height = EditorGUIUtility.singleLineHeight;

		int newIndex = EditorGUI.Popup(typeSelectRect, label.text, index, typeNames);

		if (typeNames[0] == noTypeStr)
		{
			if (newIndex == 0)
			{
				EditorGUI.EndProperty();
				return;
			}

			newIndex--;
		}

		Type newType = objectiveTypes[newIndex];

		if (newType != property.managedReferenceValue?.GetType())
			property.managedReferenceValue = Activator.CreateInstance(newType);

		Rect fieldRect = position;
		float deltaVert = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

		fieldRect.y += deltaVert;
		fieldRect.height -= deltaVert;

		EditorGUI.PropertyField(fieldRect, property, label, true);
		EditorGUI.EndProperty();
	}
}
#endif