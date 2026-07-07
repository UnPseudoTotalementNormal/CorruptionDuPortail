#region

using System;
using AYellowpaper.SerializedCollections;
using UnityEngine;

#endregion

namespace Characters
{
    /// <summary>
    /// Presentation data for one faction: display name, an optional narrative tagline, an icon and an accent
    /// colour. All design-owned (the tagline especially — it may be left empty until design fills it).
    /// </summary>
    [Serializable]
    public class FactionData
    {
        public string displayName;

        [Tooltip("Narrative sub-line shown after the name (e.g. \"The Evil Guys\"). Design-owned; may be empty.")]
        public string tagline;

        public Sprite icon;
        public Color color = Color.white;
    }

    /// <summary>
    /// Central table of faction presentation data (name / tagline / icon / colour), keyed by FactionType.
    /// The single source the role card (and, later, the character bar) reads, replacing scattered per-component
    /// faction icon/colour dictionaries.
    /// </summary>
    [CreateAssetMenu(fileName = "FactionDatabase", menuName = "Corruption/Faction Database")]
    public class FactionDatabase : ScriptableObject
    {
        [SerializeField] private SerializedDictionary<FactionType, FactionData> factions = new();

        public bool TryGet(FactionType faction, out FactionData data) => factions.TryGetValue(faction, out data);

        public FactionData Get(FactionType faction) => factions.TryGetValue(faction, out var data) ? data : null;
    }
}
