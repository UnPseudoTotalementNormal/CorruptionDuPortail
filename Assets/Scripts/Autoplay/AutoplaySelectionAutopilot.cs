#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic.Validation;
using UI.BoardUI.Selection;
using Unpseudo.Autoplay;
using static Characters.Powers.Target.TargetUtils;

namespace Autoplay
{
    /// <summary>
    /// Answers every <see cref="SelectionFlowService"/> request with a legal target chosen by the policy, using the same
    /// candidate sets as the on-screen pickers (CardPickerManager): any character the validator accepts for a character
    /// pick; one representative per role id for a role pick. Answers are queued and delivered by <see cref="Pump"/>
    /// on a later frame, never inside the power's own Start* call.
    /// <para>Scenario lever <c>target-focus</c>: when a valid candidate matches <see cref="TargetFocus"/> (and the
    /// power being used matches <see cref="TargetFocusPower"/>, if set), the pick is made among those candidates only.</para>
    /// </summary>
    public sealed class AutoplaySelectionAutopilot : ISelectionAutopilot
    {
        private readonly ICharacterQuery characterQuery;
        private readonly IAutoplayPolicy policy;
        private readonly AutoplayJournal journal;
        private readonly Queue<Action> pending = new();

        /// <summary>Who to target when valid: <c>client</c>, <c>host</c>, <c>bot</c>, <c>fake</c> or a role-name fragment.</summary>
        public string TargetFocus { get; set; }
        /// <summary>Restricts <see cref="TargetFocus"/> to picks made while this power (name fragment) is used.</summary>
        public string TargetFocusPower { get; set; }
        /// <summary>The power the driver is starting, set around its StartUse (picks are requested inside it).</summary>
        public string CurrentPowerName { get; set; }
        /// <summary>Per-power focus (power-name fragment → focus), checked before <see cref="TargetFocus"/>.</summary>
        public List<KeyValuePair<string, string>> TargetMap { get; set; } = new();

        /// <summary>Parses "power=focus;power=focus" (lever <c>target-map</c>).</summary>
        public static List<KeyValuePair<string, string>> ParseTargetMap(string _map)
        {
            var _entries = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(_map))
            {
                return _entries;
            }
            foreach (string _pair in _map.Split(';'))
            {
                int _eq = _pair.IndexOf('=');
                if (_eq > 0 && _eq < _pair.Length - 1)
                {
                    _entries.Add(new KeyValuePair<string, string>(_pair.Substring(0, _eq).Trim(), _pair.Substring(_eq + 1).Trim()));
                }
            }
            return _entries;
        }

        // The focus that applies to the current pick: the first target-map entry whose power matches, else target-focus.
        private string ActiveFocus
        {
            get
            {
                string _power = CurrentPowerName ?? string.Empty;
                foreach (KeyValuePair<string, string> _entry in TargetMap)
                {
                    if (_power.IndexOf(_entry.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return _entry.Value;
                    }
                }
                return string.IsNullOrEmpty(TargetFocus) || (!string.IsNullOrEmpty(TargetFocusPower) &&
                       _power.IndexOf(TargetFocusPower, StringComparison.OrdinalIgnoreCase) < 0)
                    ? null
                    : TargetFocus;
            }
        }

        public AutoplaySelectionAutopilot(ICharacterQuery _characterQuery, IAutoplayPolicy _policy, AutoplayJournal _journal)
        {
            characterQuery = _characterQuery;
            policy = _policy;
            journal = _journal;
        }

        /// <summary>Is the focus lever active for the current pick?</summary>
        public bool FocusApplies => !string.IsNullOrEmpty(ActiveFocus);

        /// <summary>Keeps the candidates matching the focus when the lever applies and at least one matches.</summary>
        public List<T> Focus<T>(List<T> _candidates, Func<T, Character> _characterOf)
        {
            if (!FocusApplies)
            {
                return _candidates;
            }

            string _focus = ActiveFocus;
            List<T> _focused = _candidates.Where(_c => AutoplayFocus.Matches(_characterOf(_c), _focus)).ToList();
            return _focused.Count > 0 ? _focused : _candidates;
        }

        public void PickCharacter(Validator<(ulong targetId, TargetType targetType)> _validator,
            IReadOnlyCollection<ulong> _excludedIds, Action<Character> _onPicked, Action _onCanceled)
        {
            List<Character> _candidates = characterQuery.GetCharacters(false)
                .Where(_c => _c && !_excludedIds.Contains(_c.ownerClientId.Value) &&
                             IsValid(_validator, _c.ownerClientId.Value, TargetType.Character))
                .OrderBy(_c => _c.ownerClientId.Value)
                .ToList();

            if (_candidates.Count == 0)
            {
                journal.Record("select.character", "no valid candidate -> canceled");
                pending.Enqueue(() => _onCanceled?.Invoke());
                return;
            }

            List<Character> _pool = Focus(_candidates, _c => _c);
            Character _choice = policy.Choose(_pool, "select.character");
            journal.Record("select.character",
                $"picked {_choice.ownerClientId.Value} among [{string.Join(",", _candidates.Select(_c => _c.ownerClientId.Value))}]" +
                (_pool.Count != _candidates.Count ? $" focus={ActiveFocus}" : string.Empty));
            pending.Enqueue(() => _onPicked?.Invoke(_choice));
        }

        public void PickRole(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _onPicked,
            Action _onCanceled)
        {
            // Same candidate set as CardPickerManager.GetValidRoleCandidates: one valid representative per role id.
            List<Character> _representatives = characterQuery.GetCharacters(false)
                .Where(_c => _c && _c.role != null)
                .OrderBy(_c => _c.role.roleID)
                .ThenBy(_c => _c.ownerClientId.Value)
                .GroupBy(_c => _c.role.roleID)
                .Select(_group => _group.FirstOrDefault(_c => IsValid(_validator, _c.ownerClientId.Value, TargetType.Role)))
                .Where(_c => _c)
                .ToList();

            if (_representatives.Count == 0)
            {
                journal.Record("select.role", "no valid candidate -> canceled");
                pending.Enqueue(() => _onCanceled?.Invoke());
                return;
            }

            List<Character> _pool = Focus(_representatives, _c => _c);
            Character _choice = policy.Choose(_pool, "select.role");
            Role _role = _choice.role;
            journal.Record("select.role",
                $"picked {_role.roleName} among [{string.Join(",", _representatives.Select(_c => _c.role.roleName.ToString()))}]" +
                (_pool.Count != _representatives.Count ? $" focus={ActiveFocus}" : string.Empty));
            pending.Enqueue(() => _onPicked?.Invoke(_role));
        }

        public void Cancel() => pending.Clear();

        /// <summary>Delivers the answers queued before this call (answers queued while delivering wait for the next pump).</summary>
        public void Pump()
        {
            int _count = pending.Count;
            for (int _i = 0; _i < _count && pending.Count > 0; _i++)
            {
                Action _answer = pending.Dequeue();
                try
                {
                    _answer();
                }
                catch (Exception _exception)
                {
                    journal.Record("select.error", _exception.ToString());
                    UnityEngine.Debug.LogException(_exception);
                }
            }
        }

        private static bool IsValid(Validator<(ulong targetId, TargetType targetType)> _validator, ulong _id, TargetType _type)
            => _validator == null || _validator.Evaluate((_id, _type));
    }

    /// <summary>Target focus matching shared by the scenario levers (same meaning on every process).</summary>
    public static class AutoplayFocus
    {
        public static bool Matches(Character _character, string _focus)
        {
            if (!_character || string.IsNullOrEmpty(_focus))
            {
                return false;
            }

            ulong _id = _character.ownerClientId.Value;
            switch (_focus.Trim().ToLowerInvariant())
            {
                case "fake": return _character.isFake;
                case "host": return !_character.isFake && _id == Unity.Netcode.NetworkManager.ServerClientId;
                case "bot": return !_character.isFake && _id >= 100;
                case "client": return !_character.isFake && _id != Unity.Netcode.NetworkManager.ServerClientId && _id < 100;
                default:
                    return _character.role != null &&
                           _character.role.roleName.ToString().IndexOf(_focus.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
#endif
