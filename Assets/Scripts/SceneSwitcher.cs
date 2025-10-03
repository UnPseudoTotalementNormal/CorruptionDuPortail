using System;
using UnityEngine;

public class SceneSwitcher : MonoBehaviour
{
    public int sceneIndex;

    private void Start()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneIndex);
    }
}
