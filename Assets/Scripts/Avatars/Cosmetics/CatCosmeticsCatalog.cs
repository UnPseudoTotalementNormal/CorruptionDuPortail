using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// One cosmetic of the cat: a fur (a material for every body renderer) or a set of parts (renderers of the cat
    /// model or of the cosmetics model, shown together). The id is what travels on the network and in the player's
    /// preferences: never change or reuse it.
    /// </summary>
    [Serializable]
    public class CatCosmeticItem
    {
        [Tooltip("Stable id inside its slot (network + saved preferences). Never change or reuse one.")]
        public byte id;

        public CatSlot slot;

        [Tooltip("Name shown to the player (placeholder until the art team names them).")]
        public string displayName;

        [Tooltip("Vignette of the customization screen.")]
        public Texture2D icon;

        [Tooltip("Fur only: the material put on every fur renderer of the catalog.")]
        public Material furMaterial;

        [Tooltip("Fur only, optional: the material of the hair cards (catalog's hairRenderers); empty = furMaterial.")]
        public Material hairMaterial;

        [Tooltip("Eyes only, optional: the closed-eyes part shown while the cat blinks (CatBlink). Empty = no blink.")]
        public string blinkPart;

        [Tooltip("Outfit / eyes / mouth: names of the renderers this item shows (cat model or cosmetics model). " +
                 "Empty = nothing (« Sans tenue »).")]
        public string[] parts = Array.Empty<string>();
    }

    /// <summary>
    /// Every cosmetic of the cat, by slot. Read-only at runtime (an authored SO). The cosmetics model holds the parts
    /// that are not in the base cat model, rigged on the same skeleton (+ the cape bones): <see cref="CatAppearance"/>
    /// binds them onto the cat once, then switches them.
    /// </summary>
    [CreateAssetMenu(fileName = "CatCosmeticsCatalog", menuName = "Avatars/Cat Cosmetics Catalog")]
    public class CatCosmeticsCatalog : ScriptableObject
    {
        [Tooltip("Model (Cat_Cosmetics.fbx) holding the parts the base cat model does not have.")]
        public GameObject cosmeticsModel;

        [Tooltip("Renderers of the base cat model that wear the fur material.")]
        public string[] furRenderers = Array.Empty<string>();

        [Tooltip("Among the fur renderers, the hair cards: they wear the item's hairMaterial when it has one.")]
        public string[] hairRenderers = Array.Empty<string>();

        [Tooltip("Parts never shown by the dressing itself (replaced by a cosmetic, or shown by another component: " +
                 "blink, tears).")]
        public string[] hiddenParts = Array.Empty<string>();

        [Tooltip("Ids worn by a player who never customized (and the fallback of an unknown id).")]
        public byte defaultFur;
        public byte defaultOutfit;
        public byte defaultEyes;
        public byte defaultMouth;

        public List<CatCosmeticItem> items = new();

        public CatLook DefaultLook => new CatLook(defaultFur, defaultOutfit, defaultEyes, defaultMouth);

        public CatCosmeticItem Find(CatSlot slot, byte id)
        {
            foreach (CatCosmeticItem item in items)
                if (item.slot == slot && item.id == id) return item;
            return null;
        }

        public bool Contains(CatSlot slot, byte id) => Find(slot, id) != null;

        /// <summary>The items of a slot, in authored order (the order of the screen's grid).</summary>
        public List<CatCosmeticItem> ItemsOf(CatSlot slot)
        {
            var list = new List<CatCosmeticItem>();
            foreach (CatCosmeticItem item in items)
                if (item.slot == slot) list.Add(item);
            return list;
        }

        /// <summary>Unknown ids fall back to the defaults (see <see cref="CatLook.Sanitized"/>).</summary>
        public CatLook Sanitize(CatLook look) => look.Sanitized(Contains, DefaultLook);
    }
}
