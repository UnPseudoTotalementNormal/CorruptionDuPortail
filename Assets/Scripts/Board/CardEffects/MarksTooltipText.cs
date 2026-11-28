using System.Collections.Generic;
using System.Text;
using CorruptionDuPortail.Domain.Localization;
using UnityEngine;

namespace Board
{
    /// <summary>
    /// What the private marks on a card or a role portrait mean, as the rows of one hover bubble (T9, maquettes
    /// placeholders v3 H2: hovering the card anywhere lists all its marks, each with its icon). Icons come from the
    /// TMP sprite asset Resources/Sprite Assets/Marques (built from Art/Sprites/T9/MarquesAtlas.png).
    /// Texts are a first proposal, to be reread by the game designer.
    /// </summary>
    public static class MarksTooltipText
    {
        private const string SpriteAsset = "Marques";

        /// <summary>The sprite names of the Marques sprite asset (an enum: no literal shares a "// loc" line).</summary>
        public enum MarkIcon { balise, ours, telephone, oeil, corrompu, soigne, elu, non_elu, benediction, logo_elu, chaine, robot }

        public readonly struct Mark
        {
            public readonly MarkIcon Icon;
            public readonly string Title;
            public readonly string Line;

            public Mark(MarkIcon _icon, string _title, string _line)
            {
                Icon = _icon;
                Title = _title;
                Line = _line;
            }
        }

        /// <summary>The meaning of a card effect on a player's card, or null when it has none to tell.</summary>
        public static Mark? ForCardEffect(CardEffectID _id, object _data)
        {
            switch (_id)
            {
                case CardEffectID.CursedVision:
                    // CardEffectBoolEnabler: true = not a Chosen (red), false = a Chosen (green).
                    return _data is true
                        ? new Mark(MarkIcon.non_elu, "Vision maudite", "Le Repenti a vu que ce joueur n'est pas un élu.") // loc slop-ok: the bubble's content (H2)
                        : new Mark(MarkIcon.elu, "Vision maudite", "Le Repenti a vu un élu."); // loc slop-ok: the bubble's content (H2)
                case CardEffectID.OrphelineVisit:
                    return new Mark(MarkIcon.ours, "Visite de l'Orpheline", "Tu as rendu visite à ce joueur."); // loc slop-ok: the bubble's content (H2)
                case CardEffectID.MessagerLink:
                    return new Mark(MarkIcon.telephone, "Lié par l'encre", "Le Messager a lié ce joueur par l'encre."); // loc slop-ok: the bubble's content (H2)
                case CardEffectID.Healed:
                    // Seen by Dr Gloubi and the Dryade only: their pickers skip a healed player, the corrupters do not.
                    return new Mark(MarkIcon.soigne, "Soigné", "Déjà soigné : il ne peut plus être soigné, mais peut encore être corrompu."); // loc slop-ok: the bubble's content (H2)
                case CardEffectID.Blessing:
                    // Both corrupting pickers skip a blessed player (TargetIncludeFlags without Blessed), Wouh's wording.
                    return new Mark(MarkIcon.benediction, "Bénédiction", "La Dryade protège ce joueur : il est immunisé à la Corruption Ciblée et à la Marque corruptrice."); // loc slop-ok: the bubble's content (H2)
                case CardEffectID.TechnoBeacon:
                    return new Mark(MarkIcon.balise, "Balise", "Le Technomancien a posé une balise sur ce joueur."); // loc slop-ok: the bubble's content (H2)
                default:
                    return null;
            }
        }

        /// <summary>Dr Gloubi's public heal (Character.isKnownChosen): every player sees the Élu logo on the card.</summary>
        public static Mark KnownChosen() =>
            new Mark(MarkIcon.logo_elu, "Élu", "Dr Gloubi l'a soigné : c'est un élu."); // loc slop-ok: the bubble's content (H2)

        /// <summary>A chained player (the chains drawn over the card), for everyone.</summary>
        public static Mark Chained() =>
            new Mark(MarkIcon.chaine, "Enchaîné", "Ce joueur a été enchaîné."); // loc slop-ok: the bubble's content (H2)

        /// <summary>The Robot's hack (CharacterInfoReveal.isHacked, the Robot alone sees it).</summary>
        public static Mark Hacked() =>
            new Mark(MarkIcon.robot, "Piratage", "Tu as piraté ce joueur : s'il se fait enchaîner aujourd'hui, tu gagnes."); // loc slop-ok: the bubble's content (H2)

        /// <summary>The meaning of an icon on a role portrait of the bar, found by its sprite, or null.</summary>
        public static Mark? ForPortraitIcon(Sprite _sprite)
        {
            if (_sprite == null) return null;
            switch (_sprite.name)
            {
                case "Icon_Orpheline_Ours":
                    return new Mark(MarkIcon.ours, "Visite de l'Orpheline", "L'Orpheline est venue te voir cette nuit."); // loc slop-ok: the bubble's content (H2)
                case "Icon_Messager_Telephone":
                    return new Mark(MarkIcon.telephone, "Lié par l'encre", "Le Messager t'a lié par l'encre : il peut t'écrire cette nuit."); // loc slop-ok: the bubble's content (H2)
                case "Icon_Orpheline_Oeil":
                    return new Mark(MarkIcon.oeil, "Ciblée", "Ce rôle t'a ciblée cette nuit."); // loc slop-ok: the bubble's content (H2)
                case "Icon_Corrompu":
                    return new Mark(MarkIcon.corrompu, "Corrompu", "Tu sais que ce rôle est corrompu."); // loc slop-ok: the bubble's content (H2)
                default:
                    return null;
            }
        }

        /// <summary>The bubble's text: one row per mark (icon, title, then its line), translated. Empty when none.</summary>
        public static string Rows(IEnumerable<Mark> _marks)
        {
            var _text = new StringBuilder();
            foreach (Mark _mark in _marks)
            {
                if (_text.Length > 0) _text.Append("\n<size=40%>\n</size>");
                _text.Append("<size=160%><voffset=0.05em><sprite=\"").Append(SpriteAsset).Append("\" name=\"").Append(_mark.Icon.ToString()).Append("\"></voffset></size> ")
                    .Append("<size=115%>").Append(Loc.Tr(_mark.Title)).Append("</size>\n")
                    .Append(Loc.Tr(_mark.Line));
            }
            return _text.ToString();
        }

        /// <summary>The marks' titles as authored (French), for a log line.</summary>
        public static string Names(IEnumerable<Mark> _marks)
        {
            var _names = new List<string>();
            foreach (Mark _mark in _marks) _names.Add(_mark.Icon.ToString());
            return string.Join("+", _names);
        }
    }
}
