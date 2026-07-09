using System.Collections.Generic;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// The ordered set of emotes offered by the wheel (currently just "coucou"). A ScriptableObject so new
    /// emotes are added in the asset — the wheel UI, selection math and input all read the SAME list, so the
    /// count and order stay consistent everywhere. Order = clockwise-from-top layout order in the wheel.
    /// </summary>
    [CreateAssetMenu(menuName = "Corruption/Emote Set", fileName = "EmoteSet")]
    public class EmoteSet : ScriptableObject
    {
        [SerializeField] private List<EmoteDefinition> _emotes = new();

        public IReadOnlyList<EmoteDefinition> Emotes => _emotes;
        public int Count => _emotes.Count;

        /// <summary>The emote at <paramref name="index"/>, or null if out of range.</summary>
        public EmoteDefinition Get(int index) =>
            index >= 0 && index < _emotes.Count ? _emotes[index] : null;
    }
}
