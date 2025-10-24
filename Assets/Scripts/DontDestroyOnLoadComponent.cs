using System.Collections.Generic;
using UnityEngine;

public class DontDestroyOnLoadComponent : MonoBehaviour
{
    public int id;
    public bool destroyIfDuplicateId = true;

    private static HashSet<int> existingIds = new();
    
    private void Awake()
    {
        if (existingIds.Add(id))
        {
            DontDestroyOnLoad(gameObject);
            return;
        }

        if (destroyIfDuplicateId)
        {
            DestroyImmediate(gameObject);
            return;
        }

        Debug.LogWarning(
            $"Duplicate ID {id} found in DontDestroyOnLoadComponent, but destroyIfDuplicateId is false.");
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        existingIds.Remove(id);
    }
}