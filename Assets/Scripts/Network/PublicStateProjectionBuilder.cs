#region

using System.Collections.Generic;
using System.Globalization;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic;
using Network.Player;

#endregion

namespace Network
{
    /// <summary>
    /// NET-00 (epic-network-sync-hardening): builds this peer's <see cref="PublicStateProjection"/> from its own
    /// replicas. Every component is PUBLIC replicated state that all peers must hold identically. Each source is
    /// null-tolerant (a missing manager yields an empty component on every peer alike, never a false mismatch).
    /// Later specs add components here (NET-07 Roles, NET-08 Powers).
    /// </summary>
    public static class PublicStateProjectionBuilder
    {
        public const string Roster = "Roster";
        public const string Characters = "Characters";
        public const string CharacterFlags = "CharacterFlags";
        public const string GameState = "GameState";

        public static PublicStateProjection Build(LobbyPlayerInfoHolder _roster, CharacterManager _characters, GameManager _gameManager)
        {
            var _projection = new PublicStateProjection();
            _projection.SetComponent(Roster, RosterLines(_roster), ordered: true);

            List<Character> _list = _characters != null && _characters.IsSpawned
                ? _characters.GetCharacters(false)
                : new List<Character>();
            _projection.SetComponent(Characters, CharacterLines(_list), ordered: true);
            _projection.SetComponent(CharacterFlags, FlagLines(_list), ordered: false);

            var _state = new List<string>();
            if (_gameManager != null && _gameManager.IsSpawned)
            {
                _state.Add(_gameManager.currentGameStateIndex.Value.ToString(CultureInfo.InvariantCulture));
            }
            _projection.SetComponent(GameState, _state, ordered: true);
            return _projection;
        }

        private static IEnumerable<string> RosterLines(LobbyPlayerInfoHolder _roster)
        {
            if (_roster == null || !_roster.IsSpawned || _roster.playerInfos == null)
            {
                yield break;
            }
            foreach (PlayerInfo _info in _roster.playerInfos)
            {
                yield return string.Concat(
                    _info.playerClientId.ToString(CultureInfo.InvariantCulture), "|",
                    _info.playerName.ToString(), "|",
                    _info.isReady ? "1" : "0", "|",
                    _info.hasLeft ? "1" : "0");
            }
        }

        private static IEnumerable<string> CharacterLines(List<Character> _list)
        {
            foreach (Character _character in _list)
            {
                if (_character == null) continue;
                yield return _character.ownerClientId.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static IEnumerable<string> FlagLines(List<Character> _list)
        {
            foreach (Character _character in _list)
            {
                if (_character == null) continue;
                yield return string.Concat(
                    _character.ownerClientId.Value.ToString(CultureInfo.InvariantCulture),
                    "|ch", Bit(_character.isChained.Value),
                    "|co", Bit(_character.isCorrupted.Value),
                    "|el", Bit(_character.isEliminated.Value),
                    "|aw", Bit(_character.isAwakened.Value),
                    "|bl", Bit(_character.isBlessed.Value),
                    "|he", Bit(_character.isHealed.Value));
            }
        }

        private static string Bit(bool _value) => _value ? "1" : "0";
    }
}
