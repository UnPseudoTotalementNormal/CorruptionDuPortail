using System;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// One selectable emote in the wheel: its label, wheel icon, and the <see cref="animatorEmoteId"/> handed
    /// to the avatar's Animator (the <c>EmoteId</c> integer parameter set alongside the <c>Emote</c> trigger,
    /// replicated by the body's NetworkAnimator). Data only — authored in an <see cref="EmoteSet"/> asset, no
    /// behaviour here. <c>animatorEmoteId</c> 0 is reserved for "no emote / idle"; real emotes start at 1
    /// (coucou = 1).
    /// </summary>
    [Serializable]
    public class EmoteDefinition
    {
        [Tooltip("Shown under the icon when this emote is highlighted in the wheel.")]
        public string displayName;

        [Tooltip("Icon drawn in the wheel sector.")]
        public Sprite icon;

        [Tooltip("Animator EmoteId integer set (with the Emote trigger) to play this emote. 0 = none/idle; " +
                 "coucou = 1. Keep in sync with the Cat_Avatar Animator's emote sub-states.")]
        public int animatorEmoteId = 1;
    }
}
