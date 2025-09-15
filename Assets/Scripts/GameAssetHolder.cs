using System.Collections.Generic;
using Characters;
using Characters.Powers;
using UnityEngine;

public class GameAssetHolder : MonoBehaviour
{
    public static GameAssetHolder instance { get; private set; }

    [SerializeField] private List<PowerDataObject> powerDataObjects = new();
    [SerializeField] private List<RoleDataObject> roleDataObjects = new();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public PowerDataObject GetPowerDataObject(string _powerName)
    {
        var _powerDataObject = powerDataObjects.Find(p => p.power.powerName.ToString().ToLower() == _powerName.ToLower());
        if (_powerDataObject != null)
        {
            return _powerDataObject;
        }
        Debug.LogWarning($"Power '{_powerName}' not found.");
        return null;
    }
}
