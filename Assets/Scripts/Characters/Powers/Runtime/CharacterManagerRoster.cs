using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain.Powers;
using Network;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Live <see cref="IRosterView"/> over the server's <see cref="CharacterManager"/> — the production
    /// counterpart of the EditMode FakeRoster. Read-only, no side effects (GetCharacters(false)). Slots are
    /// the characters' client ids narrowed to int: real ids are small positives; fake ids (ulong.MaxValue-n)
    /// truncate to small negatives and sign-extend back losslessly, so the Domain's int-slot round-trip is
    /// exact for both clusters. Null-tolerant per slot (returns neutral values) to never crash the server on
    /// a stale id.
    /// </summary>
    public sealed class CharacterManagerRoster : IRosterView
    {
        private readonly CharacterManager _characters;
        private readonly LobbyPlayerInfoHolder _lobby;

        public CharacterManagerRoster(CharacterManager characters, LobbyPlayerInfoHolder lobby)
        {
            _characters = characters;
            _lobby = lobby;
        }

        public IReadOnlyList<int> Slots =>
            _characters.GetCharacters(false).Select(c => (int)c.ownerClientId.Value).ToList();

        public FactionType FactionOf(int slot) =>
            CharacterAt(slot)?.role.factionType ?? default;

        public string PseudoOf(int slot) =>
            _lobby != null ? _lobby.GetPlayerInfo((ulong)slot).playerName.ToString() : string.Empty;

        public bool SameRole(int slotA, int slotB)
        {
            Character a = CharacterAt(slotA);
            Character b = CharacterAt(slotB);
            return a != null && b != null && a.role.IsTheSameRole(b.role);
        }

        public bool IsRobot(int slot) =>
            CharacterAt(slot)?.role.roleID == RoleID.Robot;

        public bool IsHealed(int slot) =>
            CharacterAt(slot)?.isHealed.Value ?? false;

        public bool IsCorrupted(int slot) =>
            CharacterAt(slot)?.isCorrupted.Value ?? false;

        public bool IsChained(int slot) =>
            CharacterAt(slot)?.isChained.Value ?? false;

        public bool IsEliminated(int slot) =>
            CharacterAt(slot)?.isEliminated.Value ?? false;

        public string RoleNameOf(int slot) =>
            CharacterAt(slot)?.role.roleName.ToString() ?? string.Empty;

        private Character CharacterAt(int slot) => _characters.GetCharacter((ulong)slot, false);
    }
}
