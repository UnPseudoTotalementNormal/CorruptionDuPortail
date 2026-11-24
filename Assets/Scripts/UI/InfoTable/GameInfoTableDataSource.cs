using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Diagnostics.Logs;
using UnityEngine;

namespace UI.InfoTable
{
    /// <summary>
    /// Real-game <see cref="IInfoTableDataSource"/>: the only piece that touches NGO types. Adapts
    /// <see cref="CharacterManager"/> / <see cref="GameInfoRevealer"/> / <see cref="GameManager"/> into the
    /// engine-free DTOs the UITK InfoTable consumes. Presentation-only — reads replicated state, issues no
    /// RPCs and mutates nothing, exactly like the retired <c>InfoTableSystem</c>. Scene-wired, null-tolerant.
    /// </summary>
    public class GameInfoTableDataSource : MonoBehaviour, IInfoTableDataSource
    {
        [SerializeField] private CharacterManager characterManager;
        [SerializeField] private GameInfoRevealer gameInfoRevealer;
        [SerializeField] private GameManager gameManager;

        public event Action OnRebuildRequested;
        public event Action OnRevealChanged;

        private void OnEnable()
        {
            if (gameManager != null) gameManager.onGameStarted += RaiseRebuild;
            if (gameInfoRevealer != null) gameInfoRevealer.onCharacterInfoRevealedChanged += RaiseReveal;
        }

        private void OnDisable()
        {
            if (gameManager != null) gameManager.onGameStarted -= RaiseRebuild;
            if (gameInfoRevealer != null) gameInfoRevealer.onCharacterInfoRevealedChanged -= RaiseReveal;
        }

        private void RaiseRebuild() => OnRebuildRequested?.Invoke();
        private void RaiseReveal() => OnRevealChanged?.Invoke();

        public IReadOnlyList<InfoTablePlayer> GetPlayers()
        {
            var result = new List<InfoTablePlayer>();
            if (characterManager == null) return result;

            var rows = new List<Character>();
            foreach (Character c in characterManager.GetCharacters(false))
            {
                if (c == null || c.role == null || c.isFake) continue;
                result.Add(new InfoTablePlayer(c.ownerClientId.Value, c.GetOwnerPseudo()));
                rows.Add(c);
            }
            TrackNameSources(rows);
            return result;
        }

        public IReadOnlyList<InfoTableRole> GetRoles()
        {
            var caps = new List<KeyValuePair<Role, int>>();
            if (characterManager == null) return Array.Empty<InfoTableRole>();

            // Capacity counts ALL characters incl. fakes; identity keys on roleName (Role.IsTheSameRole).
            foreach (Character c in characterManager.GetCharacters(false))
            {
                if (c == null || c.role == null) continue;
                int idx = caps.FindIndex(rc => rc.Key.IsTheSameRole(c.role));
                if (idx >= 0) caps[idx] = new KeyValuePair<Role, int>(caps[idx].Key, caps[idx].Value + 1);
                else caps.Add(new KeyValuePair<Role, int>(c.role, 1));
            }
            // Columns in a fixed order (night call order, then role id), never in the characters' order: rows follow
            // the arrival order and the characters list is that same order, so deriving the columns from it put every
            // player's own role on the diagonal and gave every role away (task « Randomiser le tableau »).
            AwakeningState state = GetAwakeningState();
            List<InfoTableRole> ordered = caps
                .OrderBy(rc => state != null ? state.GetAwakeningLayerIndex(rc.Key) : int.MaxValue)
                .ThenBy(rc => (int)rc.Key.roleID)
                .Select(rc => new InfoTableRole(rc.Key.roleName.ToString(), rc.Value, rc.Key.factionType))
                .ToList();
            GameLog.Guarded(() => GameLog.Info("infotable", "infotable.columns",
                "order", string.Join(",", ordered.Select(r => r.RoleName)), "awakening", state != null));
            return ordered;
        }

        private AwakeningState _awakeningState;

        private AwakeningState GetAwakeningState()
        {
            if (_awakeningState == null && gameManager != null)
            {
                _awakeningState = (AwakeningState)((IGameStateQuery)gameManager).GetGameStates(typeof(AwakeningState)).FirstOrDefault();
            }
            return _awakeningState;
        }

        public string GetRevealedRoleName(ulong clientId)
        {
            if (gameInfoRevealer == null || characterManager == null) return null;

            CharacterInfoReveal info = gameInfoRevealer.GetCharacterInfo(clientId);
            if ((int)info.isRoleRevealed <= 0) return null;

            Character c = characterManager.GetCharacters(false)
                .FirstOrDefault(x => x != null && x.ownerClientId.Value == clientId);
            return c != null && c.role != null ? c.role.roleName.ToString() : null;
        }

        // ---- NET-03: pseudo refresh without a rebuild ----

        public event Action OnNamesChanged;

        // Characters whose roster change we listen to (the rows of the last GetPlayers()).
        private readonly List<Character> _nameSources = new();

        public string GetPseudo(ulong clientId)
        {
            if (characterManager == null) return string.Empty;
            Character c = characterManager.GetCharacters(false)
                .FirstOrDefault(x => x != null && x.ownerClientId.Value == clientId);
            return c != null ? c.GetOwnerPseudo() : string.Empty;
        }

        private void TrackNameSources(IEnumerable<Character> characters)
        {
            UntrackNameSources();
            foreach (Character c in characters)
            {
                c.onOwnerPseudoChanged += RaiseNamesChanged;
                _nameSources.Add(c);
            }
        }

        private void UntrackNameSources()
        {
            foreach (Character c in _nameSources)
            {
                if (c != null) c.onOwnerPseudoChanged -= RaiseNamesChanged;
            }
            _nameSources.Clear();
        }

        private void RaiseNamesChanged() => OnNamesChanged?.Invoke();

        private void OnDestroy() => UntrackNameSources();
    }
}
