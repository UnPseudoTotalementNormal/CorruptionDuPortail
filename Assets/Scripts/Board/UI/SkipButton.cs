using Localization;
#region

using Characters;
using CorruptionDuPortail.Domain;
using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace Board.UI
{
    /// <summary>
    /// The board's single skip button: "Fin de tour" at night, "Passer le vote" during the vote (it replaced the
    /// vote screen's own Skip button). Wouh's wooden plank (T9, maquettes placeholders v1 F1): on screen while it can
    /// be used, slid down out of the screen once used or when it has nothing to do (D2; rule in
    /// <see cref="SkipButtonPolicy"/>).
    /// </summary>
    public class SkipButton : MonoBehaviour
    {
        private const string StopAwakeningLabel = "Fin de tour"; // loc
        private const string SkipVoteLabel = "Passer le vote"; // loc

        private CustomButton customButton;
        private TMP_Text label;
        private SkipButtonState? shownState;
        private RectTransform rect;
        private float shownY;

        // D2: the plank slides out under the screen's bottom edge when it has nothing to do, and back up when it does.
        [SerializeField] private float slideDuration = 0.35f;
        [SerializeField] private Color labelOutlineColor = new(0.23f, 0.05f, 0.03f, 1f);
        [SerializeField, Range(0f, 1f)] private float labelOutlineWidth = 0.22f;

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;
        // Lane A: scene-wired GameManager, narrowed to the game-state read slice (current state = night or vote).
        [SerializeField] private GameManager gameManager;
        private IGameStateQuery GameStateQuery => gameManager;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "SkipButton.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "SkipButton.gameManager is not wired — wire it in GameScene (the composition root).");
            customButton = GetComponent<CustomButton>();
            label = GetComponentInChildren<TMP_Text>(true);
            customButton.onButtonClicked += OnSkipButtonClicked;
            rect = (RectTransform)transform;
            shownY = rect.anchoredPosition.y;
            // Cream letters need a dark rim to read on the red wood (a runtime material instance, never an asset).
            if (label != null)
            {
                label.outlineColor = labelOutlineColor;
                label.outlineWidth = labelOutlineWidth;
            }
        }

        private void OnDestroy()
        {
            if (customButton != null) customButton.onButtonClicked -= OnSkipButtonClicked;
        }

        // Polled: the local character, its awakening and the vote list all change through replication (and a rejoin
        // recreates the character), so reading them each frame is simpler and safer than tracking every source.
        private void Update()
        {
            if (customButton == null || !gameManager.IsSpawned) return;

            SkipButtonState state = CurrentState();
            if (shownState.HasValue && shownState.Value.Mode == state.Mode &&
                shownState.Value.Interactable == state.Interactable)
            {
                return;
            }

            shownState = state;
            if (label != null) label.SetLoc(state.Mode == SkipButtonMode.SkipVote ? SkipVoteLabel : StopAwakeningLabel);
            customButton.SetInteractable(state.Interactable);
            Slide(state.Interactable);
        }

        /// <summary>The plank is at rest (not sliding in or out): what a player or the autoplay can click.</summary>
        public bool IsSettled => rect != null && !DOTween.IsTweening(this);

        private void Slide(bool _shown)
        {
            if (rect == null) return;
            float _targetY = _shown ? shownY : shownY - rect.rect.height - 24f;
            // Own id: CustomButton's click punch tweens the same transform and must not be cut half-way.
            DOTween.Kill(this);
            rect.DOAnchorPosY(_targetY, slideDuration).SetEase(_shown ? Ease.OutBack : Ease.InQuad).SetId(this).SetLink(gameObject);
            Diagnostics.Logs.GameLog.Info("ui", _shown ? "ui.skip.show" : "ui.skip.hide", "mode",
                shownState.HasValue ? shownState.Value.Mode.ToString() : "none");
        }

        private SkipButtonState CurrentState()
        {
            Character local = CharacterQuery.GetLocalCharacter(false);
            VoteState vote = CurrentVoteState();
            bool isAwakened = local != null && local.isAwakened.Value;
            // Client-side mirror of VoteState.CanVote for the local player (a departed player has no button).
            bool canVote = local != null && !local.isEliminated.Value && !local.isFake;
            bool hasVoted = vote != null && local != null && HasVoted(vote, local.ownerClientId.Value);
            return SkipButtonPolicy.Resolve(vote != null, isAwakened, canVote, hasVoted);
        }

        private VoteState CurrentVoteState()
            => GameStateQuery.GetGameState(GameStateQuery.currentGameStateIndex.Value) as VoteState;

        // The vote lists are replicated to every client (OnRefreshPlayerVotesRpc), keyed by voted id.
        private static bool HasVoted(VoteState vote, ulong seat)
        {
            foreach (var voters in vote.votesForPlayer.Values)
            {
                if (voters.Contains(seat)) return true;
            }
            return false;
        }

        private void OnSkipButtonClicked()
        {
            Character local = CharacterQuery.GetLocalCharacter(false);
            if (local == null)
            {
                Diagnostics.Logs.GameLog.Warn("ui", "ui.skip.refused", "reason", "no-local-character");
                return;
            }

            VoteState vote = CurrentVoteState();
            Diagnostics.Logs.GameLog.Info("ui", "ui.skip.click", "kind", vote != null ? "vote-skip" : "sleep");
            if (vote != null)
            {
                // A skip is a vote for SKIP_VOTE_ID, through the working player-vote dispatch.
                vote.OnPlayerVoted(VoteState.SKIP_VOTE_ID);
                return;
            }

            local.SleepCharacterServerRpc();
        }
    }
}
