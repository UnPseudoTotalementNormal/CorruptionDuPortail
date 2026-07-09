using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
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

            foreach (Character c in characterManager.GetCharacters(false))
            {
                if (c == null || c.role == null || c.isFake) continue;
                result.Add(new InfoTablePlayer(c.ownerClientId.Value, c.GetOwnerPseudo()));
            }
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
            return caps.Select(rc => new InfoTableRole(rc.Key.roleName.ToString(), rc.Value)).ToList();
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
    }
}
