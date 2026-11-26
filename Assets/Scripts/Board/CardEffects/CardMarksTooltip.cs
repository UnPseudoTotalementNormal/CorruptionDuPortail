using System.Collections.Generic;
using TooltipSystem;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Board
{
    /// <summary>
    /// T9 (maquettes placeholders v3, H2): hovering a card anywhere opens one bubble listing every private mark this
    /// peer sees on it (Vision maudite, the Orpheline's bear, the Messager's phone, healed, blessed, beacon, the public
    /// Élu logo, chained, hacked). No mark,
    /// no bubble. Added to every card by <see cref="CardEffectManager"/> when the card spawns.
    /// </summary>
    public class CardMarksTooltip : MonoBehaviour, IPointerEnterHandler
    {
        private Card card;
        private CardEffectManager effects;
        private HoverTooltipComponent tooltip;
        private readonly List<MarksTooltipText.Mark> marks = new();

        public void Initialize(Card _card, CardEffectManager _effects)
        {
            card = _card;
            effects = _effects;
            tooltip = gameObject.AddComponent<HoverTooltipComponent>();
            tooltip.SetTooltipDescription(BuildText);
            // Beside the card face, not around every canvas the card carries (vote panel, « Moi », overlays).
            tooltip.SetBoundsOverride(_card.FaceRect);
        }

        private List<MarksTooltipText.Mark> CurrentMarks()
        {
            marks.Clear();
            if (card == null || effects == null || card.characterInfo == null) return marks;
            ulong _owner = card.characterInfo.ownerClientId.Value;
            foreach (CardEffectID _id in effects.GetCardEffects(_owner))
            {
                MarksTooltipText.Mark? _mark = MarksTooltipText.ForCardEffect(_id, effects.GetEffectData(_id, _owner));
                if (_mark.HasValue) marks.Add(_mark.Value);
            }
            // Marks read from the player's state rather than from a card effect (fil « Joueur perdu »).
            Characters.Character _character = card.characterInfo;
            if (_character.isKnownChosen.Value) marks.Add(MarksTooltipText.KnownChosen());
            if (_character.isChained.Value) marks.Add(MarksTooltipText.Chained());
            GameLogic.GameInfoRevealer _revealer = card.GameInfoRevealer;
            if (_revealer != null && _revealer.GetCharacterInfo(_owner).isHacked > GameLogic.RevealLevel.False)
                marks.Add(MarksTooltipText.Hacked());
            return marks;
        }

        private string BuildText() => MarksTooltipText.Rows(CurrentMarks());

        public void OnPointerEnter(PointerEventData _eventData)
        {
            List<MarksTooltipText.Mark> _marks = CurrentMarks();
            if (_marks.Count == 0 || card == null || card.characterInfo == null) return;
            Diagnostics.Logs.GameLog.Info("ui", "ui.marks.hover", "where", "card", "player", card.characterInfo.ownerClientId.Value,
                "marks", MarksTooltipText.Names(_marks));
        }
    }
}
