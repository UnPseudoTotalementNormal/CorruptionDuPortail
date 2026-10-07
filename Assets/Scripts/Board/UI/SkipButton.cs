#region

using Characters;
using CorruptionDuPortail.Domain;
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
    /// The board's single skip button: "Arrêter l'éveil" at night, "Passer le vote" during the vote (it replaced the
    /// vote screen's own Skip button). Red while it can be used, grey and inert once used or when it has nothing to
    /// do (rule in <see cref="SkipButtonPolicy"/>).
    /// </summary>
    public class SkipButton : MonoBehaviour
    {
        private const string StopAwakeningLabel = "Arrêter l'éveil";
        private const string SkipVoteLabel = "Passer le vote";

        private CustomButton customButton;
        private TMP_Text label;
        private SkipButtonState? shownState;

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
            if (label != null) label.text = state.Mode == SkipButtonMode.SkipVote ? SkipVoteLabel : StopAwakeningLabel;
            customButton.SetInteractable(state.Interactable);
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
            if (local == null) return;

            VoteState vote = CurrentVoteState();
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
