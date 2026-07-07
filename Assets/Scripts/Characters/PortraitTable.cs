#region

using AYellowpaper.SerializedCollections;
using Characters.Assets;
using UnityEngine;

#endregion

namespace Characters
{
    /// <summary>
    /// Maps each <see cref="CharacterPortraitsValues.CharacterPortraits"/> to its portrait Sprite via a
    /// direct asset reference. Replaces the old path-string + Addressables lookup: the enum stays the
    /// network-serialized identity on <see cref="Role"/>, and the VIEW layer (character bar, cards, role
    /// card) resolves the sprite locally and synchronously through this table.
    /// </summary>
    [CreateAssetMenu(fileName = "PortraitTable", menuName = "Corruption/Portrait Table")]
    public class PortraitTable : ScriptableObject
    {
        [SerializeField]
        private SerializedDictionary<CharacterPortraitsValues.CharacterPortraits, Sprite> portraits = new();

        public Sprite Get(CharacterPortraitsValues.CharacterPortraits portrait)
            => portraits.TryGetValue(portrait, out var sprite) ? sprite : null;
    }
}
