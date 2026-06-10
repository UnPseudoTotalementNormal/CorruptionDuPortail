using Characters.WinningConditions;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, snapshot-facing winning-condition contract (Story 2.7). The production victory loop evaluates
    /// conditions through this interface off an immutable <see cref="GameSnapshot"/> — no live <c>GameManager</c>
    /// pull in the evaluation path. Implemented by <c>WinningCondition</c> in the Game assembly.
    /// References only Domain types (<see cref="WinningTeam"/>, <see cref="GameSnapshot"/>) so the core stays pure.
    /// </summary>
    public interface IWinningCondition
    {
        WinningTeam GetWinningTeam();

        bool CheckCondition(GameSnapshot snapshot);
    }
}
