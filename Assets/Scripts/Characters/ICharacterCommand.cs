using Characters.Powers;

namespace Characters
{
    /// <summary>
    /// Story 9.2 (Epic 9 / D3) — the COMMAND slice of CharacterManager's public surface
    /// (refactor-architecture-despaghetti.md §2.3). Server-authority spawn/mutation operations,
    /// lifted VERBATIM from CharacterManager (the 9.1 usage census) — no signature "improvement".
    /// Read-only access lives on <see cref="ICharacterQuery"/>; the NFR5 network-authority internals
    /// (GetSafeRpcTarget / IsLocalOrSimulated / clientId &gt;= 100 routing) stay OFF every interface and
    /// are accessed concretely by the few network-critical consumers that need them (§3(d), AC2).
    /// Lives in the Game assembly (it exposes the engine Character / Role / Power types), not Domain.
    /// </summary>
    public interface ICharacterCommand
    {
        /// <summary>Spawns + registers a character for the given client id (server). Null if already present.</summary>
        Character AddNewCharacter(ulong _clientId);

        /// <summary>Despawns + removes the character owned by the given client id (server).</summary>
        void RemoveCharacter(ulong _clientId);

        /// <summary>Creates a fake character occupying a FAKE_CLIENT_ID slot (server).</summary>
        Character CreateNewFakeCharacter();

        /// <summary>Spawns + reparents a power onto the given character (server). Optional <paramref name="_onReady"/>
        /// fires on the server once the copy is reparented + registered, so the caller can configure it.</summary>
        void GivePowerToCharacter(ulong _characterId, Power _power, System.Action<Power> _onReady = null);

        /// <summary>Despawns + removes a power from the given character (server).</summary>
        void RemovePowerFromCharacter(ulong _characterId, Power _power);

        /// <summary>Replicates a role onto the given character (owner RPC).</summary>
        void GiveRoleToCharacterRpc(ulong _characterId, Role _role);

        /// <summary>Asks the server to push a full character-list refresh (server RPC).</summary>
        void AskForUpdateAllCharactersRpc();

        /// <summary>Spawns a simulated (clientId &gt;= 100) player for debug/bot flows (server).</summary>
        void SpawnSimulatedPlayer();

        /// <summary>Sets / clears the debug-possessed local identity.</summary>
        void SetPossessedIdentity(ulong? _id);

        /// <summary>Resolves the spawn promise for a freshly spawned character.</summary>
        void RegisterSpawnedCharacter(Character _character);
    }
}
