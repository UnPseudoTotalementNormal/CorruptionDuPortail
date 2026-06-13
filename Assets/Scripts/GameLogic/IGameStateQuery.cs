using System;
using Unity.Netcode;

namespace GameLogic
{
    /// <summary>
    /// Story 8.1 (Epic 8 / D2) — the game-state QUERY (read) slice of GameManager's public surface
    /// (refactor-architecture-despaghetti.md §8). Presentation/subscriber consumers depend on this
    /// narrow read intent instead of the whole GameManager God Object. Signatures are lifted verbatim
    /// from GameManager (the 7.5 public-member census) — no signature "improvement". Lives in the Game
    /// assembly, NOT Domain: it exposes engine/NGO types (GameState, NetworkVariable&lt;int&gt;), which
    /// Domain's noEngineReferences purity forbids (AC2, recorded).
    /// </summary>
    public interface IGameStateQuery
    {
        /// <summary>The game state at the given index (dictionary order).</summary>
        GameState GetGameState(int index);

        /// <summary>All game states of the given type.</summary>
        GameState[] GetGameStates(Type _gameStateType);

        /// <summary>The index of the given game state, or -1 if not found.</summary>
        int GetGameStateIndex(GameState _gameState);

        /// <summary>The closest previous game state of type T before the current one (handles the circular loop).</summary>
        T GetClosestPreviousState<T>() where T : GameState;

        /// <summary>The current game-state index — read access + OnValueChanged subscription (NetworkVariable, server-written).</summary>
        NetworkVariable<int> currentGameStateIndex { get; }
    }
}
