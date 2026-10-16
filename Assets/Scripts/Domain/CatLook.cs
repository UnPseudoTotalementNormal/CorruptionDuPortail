using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>The parts of the cat a player picks one by one in the customization screen.</summary>
    public enum CatSlot
    {
        Fur = 0,
        Outfit = 1,
        Eyes = 2,
        Mouth = 3,
    }

    /// <summary>
    /// A player's cat: one cosmetic id per <see cref="CatSlot"/> (ids belong to the cosmetics catalog, stable over
    /// versions: a removed item is never reused). Packed in 32 bits so one NetworkVariable carries it, and written as
    /// text in the player's preferences. Ids are bytes: 255 cosmetics per part leaves room.
    /// </summary>
    public struct CatLook : IEquatable<CatLook>
    {
        public static readonly CatSlot[] Slots = { CatSlot.Fur, CatSlot.Outfit, CatSlot.Eyes, CatSlot.Mouth };

        /// <summary>The cat everyone had before customization: black and white fur, red bow cape, its own eyes, no mouth.</summary>
        public static CatLook Default => new CatLook(0, 0, 0, 0);

        public byte Fur;
        public byte Outfit;
        public byte Eyes;
        public byte Mouth;

        public CatLook(byte fur, byte outfit, byte eyes, byte mouth)
        {
            Fur = fur;
            Outfit = outfit;
            Eyes = eyes;
            Mouth = mouth;
        }

        public byte Get(CatSlot slot)
        {
            switch (slot)
            {
                case CatSlot.Fur: return Fur;
                case CatSlot.Outfit: return Outfit;
                case CatSlot.Eyes: return Eyes;
                case CatSlot.Mouth: return Mouth;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        /// <summary>A copy with <paramref name="slot"/> set to <paramref name="id"/>.</summary>
        public CatLook With(CatSlot slot, byte id)
        {
            CatLook look = this;
            switch (slot)
            {
                case CatSlot.Fur: look.Fur = id; break;
                case CatSlot.Outfit: look.Outfit = id; break;
                case CatSlot.Eyes: look.Eyes = id; break;
                case CatSlot.Mouth: look.Mouth = id; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
            return look;
        }

        public uint Pack() => Fur | (uint)Outfit << 8 | (uint)Eyes << 16 | (uint)Mouth << 24;

        public static CatLook Unpack(uint packed) =>
            new CatLook((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));

        /// <summary>Text form for the preferences: "fur.outfit.eyes.mouth".</summary>
        public string Encode() => Fur + "." + Outfit + "." + Eyes + "." + Mouth;

        /// <summary>Reads <see cref="Encode"/>'s text; false (and <see cref="Default"/>) on anything else.</summary>
        public static bool TryDecode(string text, out CatLook look)
        {
            look = Default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('.');
            if (parts.Length != Slots.Length) return false;
            var ids = new byte[Slots.Length];
            for (var i = 0; i < parts.Length; i++)
                if (!byte.TryParse(parts[i], out ids[i])) return false;
            look = new CatLook(ids[0], ids[1], ids[2], ids[3]);
            return true;
        }

        /// <summary>
        /// Every part the catalog does not know (an id from a newer or modded version, a removed cosmetic, a forged
        /// network value) falls back to that slot's default, so a cat is always drawable and the server never relays
        /// an unknown id.
        /// </summary>
        public CatLook Sanitized(Func<CatSlot, byte, bool> exists, CatLook defaults)
        {
            CatLook look = this;
            foreach (CatSlot slot in Slots)
                if (!exists(slot, look.Get(slot))) look = look.With(slot, defaults.Get(slot));
            return look;
        }

        /// <summary>The next id after <paramref name="current"/> in <paramref name="ids"/> (wraps), for arrows and keys.</summary>
        public static byte Step(IReadOnlyList<byte> ids, byte current, int delta)
        {
            if (ids == null || ids.Count == 0) return current;
            int index = 0;
            for (var i = 0; i < ids.Count; i++)
                if (ids[i] == current) index = i;
            int next = ((index + delta) % ids.Count + ids.Count) % ids.Count;
            return ids[next];
        }

        public bool Equals(CatLook other) => Pack() == other.Pack();
        public override bool Equals(object obj) => obj is CatLook other && Equals(other);
        public override int GetHashCode() => (int)Pack();
        public static bool operator ==(CatLook a, CatLook b) => a.Equals(b);
        public static bool operator !=(CatLook a, CatLook b) => !a.Equals(b);
        public override string ToString() => Encode();
    }
}
