using Characters;
using GameLogic;
using UI;
using UnityEngine;

namespace Board
{
    /// <summary>
    /// Owner-only hack glitch driver. Reads the <see cref="CharacterInfoReveal.isHacked"/> knowledge
    /// flag — set Personal to the Robot alone by <c>OmniscienceDecision</c> — and toggles the
    /// <see cref="UIGlitchGroup"/> on this card accordingly. On every other client the flag stays
    /// False, so nothing glitches: the "who is hacked" information stays robot-only.
    ///
    /// Reads with the LOCAL observer (GetCharacterInfo default), so the Host reading a bot-Robot's
    /// knowledge never sees the flag — correct, a simulated Robot has no human viewer. Idempotent:
    /// Apply/Remove early-return when already in the target state, so Refresh can fire freely
    /// (reveal-changed, card re-info, re-enable on respawn/flip).
    /// </summary>
    [RequireComponent(typeof(Card))]
    public class CardHackGlitch : MonoBehaviour
    {
        [Tooltip("UIGlitchGroup covering the card's front visuals (on FrontCanvas). Toggled on/off " +
                 "with the hacked state.")]
        [SerializeField] private UIGlitchGroup glitchGroup;

        private Card card;
        private GameInfoRevealer revealer;
        private bool revealerBound;

        private void Awake()
        {
            card = GetComponent<Card>();
        }

        private void OnEnable()
        {
            if (card != null)
            {
                card.onCardSetInfo += OnCardSetInfo;
            }
            BindRevealer();
            Refresh();
        }

        private void OnDisable()
        {
            if (card != null)
            {
                card.onCardSetInfo -= OnCardSetInfo;
            }
            if (revealerBound && revealer != null)
            {
                revealer.onCharacterInfoRevealedChanged -= Refresh;
            }
            revealerBound = false;
        }

        // The GameInfoRevealer is injected into the Card after Awake (BoardManager lane-B), so it can
        // be null on the first OnEnable. Bind lazily once it is available (also retried on SetInfo).
        private void BindRevealer()
        {
            if (revealerBound || card == null)
            {
                return;
            }
            revealer = card.GameInfoRevealer;
            if (revealer == null)
            {
                return;
            }
            revealer.onCharacterInfoRevealedChanged += Refresh;
            revealerBound = true;
        }

        private void OnCardSetInfo(Character _)
        {
            BindRevealer();
            Refresh();
        }

        private void Refresh()
        {
            BindRevealer();
            if (glitchGroup == null || card == null || card.characterInfo == null || revealer == null)
            {
                return;
            }

            bool hacked = revealer.GetCharacterInfo(card.characterInfo.ownerClientId.Value).isHacked
                          >= RevealLevel.Personal;
            if (hacked)
            {
                glitchGroup.Apply();
            }
            else
            {
                glitchGroup.Remove();
            }
        }
    }
}
