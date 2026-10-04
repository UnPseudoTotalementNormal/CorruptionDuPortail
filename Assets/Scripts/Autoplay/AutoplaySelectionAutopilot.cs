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
    /// </summary>
    public sealed class AutoplaySelectionAutopilot : ISelectionAutopilot
    {
        private readonly ICharacterQuery characterQuery;
        private readonly IAutoplayPolicy policy;
        private readonly AutoplayJournal journal;
        private readonly Queue<Action> pending = new();

        public AutoplaySelectionAutopilot(ICharacterQuery _characterQuery, IAutoplayPolicy _policy, AutoplayJournal _journal)
        {
            characterQuery = _characterQuery;
            policy = _policy;
            journal = _journal;
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

            Character _choice = policy.Choose(_candidates, "select.character");
            journal.Record("select.character",
                $"picked {_choice.ownerClientId.Value} among [{string.Join(",", _candidates.Select(_c => _c.ownerClientId.Value))}]");
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

            Character _choice = policy.Choose(_representatives, "select.role");
            Role _role = _choice.role;
            journal.Record("select.role",
                $"picked {_role.roleName} among [{string.Join(",", _representatives.Select(_c => _c.role.roleName.ToString()))}]");
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
}
#endif
