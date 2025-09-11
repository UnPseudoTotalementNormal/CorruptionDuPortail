#region

using System.Collections.Generic;
using GameLogic;
using UnityEditor;

#endregion

[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    private SerializedProperty gameStatesProperty;
    private Dictionary<GameState, bool> foldouts = new();

    private void OnEnable()
    {
        gameStatesProperty = serializedObject.FindProperty("gameStates");
        EditorApplication.update += UpdateInspector;
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdateInspector;
    }

    private void UpdateInspector()
    {
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        serializedObject.Update();

        EditorGUILayout.LabelField("GameStates properties", EditorStyles.boldLabel);

        foreach (var gameState in ((GameManager)target).gameStates.Keys)
        {
            if (!foldouts.ContainsKey(gameState))
            {
                foldouts[gameState] = false;
            }

            foldouts[gameState] = EditorGUILayout.Foldout(foldouts[gameState], gameState.name, true);

            if (foldouts[gameState])
            {
                SerializedObject serializedGameState = new SerializedObject(gameState);
                SerializedProperty property = serializedGameState.GetIterator();
                property.NextVisible(true);

                EditorGUI.indentLevel++;
                while (property.NextVisible(false))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}