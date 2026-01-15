using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

namespace Controllers.Inputs
{
    public static class InputSprites
    {
        private static Dictionary<InputID, Sprite> inputSprites = new();

        public static void SetSprites(Dictionary<InputID, Sprite> _sprites)
        {
            inputSprites = _sprites;
        }
        
        public static Sprite GetSprite(InputID _inputID)
        {
            return inputSprites.GetValueOrDefault(_inputID);
        }
    }
}