using Characters.WinningConditions;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure structural contract for a winning-condition evaluator.
    /// Its signature is already satisfied by the 4 existing <c>WinningCondition</c> evaluators in <c>Game</c>;
    /// wiring <c>WinningCondition</c> to implement this interface (and cutting the old signature) is Epic 2 / Story 2.7.
    /// Lives in the pure Domain assembly (noEngineReferences) so the core cannot acquire an engine dependency.
    /// </summary>
    public interface IWinningConditionEvaluator
    {
        WinningTeam GetWinningTeam();

        bool CheckCondition();
    }
}
