using System;
using AYellowpaper.SerializedCollections;
using UnityEngine;

namespace Controllers.Inputs
{
    public class InputUiVisualSetter : MonoBehaviour
    {
        [SerializeField] private SerializedDictionary<InputID, Sprite> inputSprites = new();

        private void Awake()
        {
            InputSprites.SetSprites(inputSprites);
        }
    }
}
