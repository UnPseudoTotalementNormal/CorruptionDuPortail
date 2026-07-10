using System;
using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PClandestineObservation : Power, IClandestineReport
    {
        public RoleID targetRoleID;

        // Powers-POCO v2: the announcement text is composed by ClandestineObservationDecision (pure) from
        // this report port; the roleTargetSystem/characterManager reads that feed it stay power-local here.
        private readonly ClandestineObservationDecision _decision = new();

        private IEnumerable<Character> ObservedRoleCharacters =>
            characterManager.GetCharacters(false).Where(_c => _c.role.roleID == targetRoleID);

        bool IClandestineReport.HasCharacters => ObservedRoleCharacters.Any();

        string IClandestineReport.RoleLabel =>
            ObservedRoleCharacters.FirstOrDefault()?.role.roleName.ToString() ?? targetRoleID.ToString();

        int IClandestineReport.DistinctTargetingCount
        {
            get
            {
                List<TargetingData> _targetingDataList = new();
                foreach (var _observed in ObservedRoleCharacters)
                {
                    _targetingDataList.AddRange(roleTargetSystem.GetAllTargetingDataForTarget(_observed.ownerClientId.Value));
                }
                return _targetingDataList.Distinct().Count();
            }
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            
            characterManager.GetCharacter(ownerClientId.Value, false).onCharacterAwakened += DeclareAllTargetFocusServer;
        }
        

        public void DeclareAllTargetFocusServer()
        {
            if (!CanUse())
            {
                return;
            }
            // State goes into BOTH the context (this decision READS ctx.State<IClandestineReport>()) and the
            // runtime (for any state-write executors) — the report is computed by this power's own port.
            var _selfState = SelfState;
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, state: _selfState), _selfState);
        }
    }
}