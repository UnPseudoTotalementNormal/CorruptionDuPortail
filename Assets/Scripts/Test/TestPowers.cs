#region

using Characters.Powers;
using UnityEngine;

#endregion

public class TestPowers : MonoBehaviour
{
    public PowerDataObject powerDataObject;

    private void Start()
    {
        Debug.Log(powerDataObject.name);
        Debug.Log(powerDataObject.power.powerName);
        Debug.Log(powerDataObject.power.maxWaitTime);
    }
}
