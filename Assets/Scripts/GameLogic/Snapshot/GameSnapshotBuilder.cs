using System.Collections.Generic;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;

namespace GameLogic.Snapshot
{
    /// <summary>
    /// Server-side Humble-Object mapping: captures a complete, SYNCHRONOUS snapshot of live NGO state into an
    /// immutable Domain <see cref="GameSnapshot"/>. Runs before any await so no NetworkVariable can tear across a
    /// frame. Lives in Game (it reads live state — GameManager / Character NetworkVariables / POmniscience) and
    /// produces a pure Domain value object. Side-effect free: it reads, never writes, never fires an RPC.
    ///
    /// FIELD-READ INVENTORY (union of all 4 winning conditions, Story 2.1):
    ///   OwnerClientId              ← Character.ownerClientId.Value
    ///   IsFake                     ← Character.isFake (= ownerClientId.IsFakeClientId())
    ///   IsCorrupted                ← Character.isCorrupted.Value          (WAnomalyCorruption)
    ///   IsChained                  ← Character.isChained.Value            (WMarginal, WChosen, WOmniscience target)
    ///   FactionType                ← Character.role.factionType           (WChosen, WOmniscience target)
    ///   HackedByOmniscienceTarget  ← POmniscience.hackedCharacterClientId (live instance in role.powers — NOT serialized Role)
    /// GameSnapshot.Day / CurrentStateIndex are NOT read by any winning condition (GameLoop state, Story 2.11);
    /// CurrentStateIndex is captured from the live index, Day is a placeholder until the GameLoop extraction.
    /// </summary>
    public static class GameSnapshotBuilder
    {
        public static GameSnapshot FromLiveState(GameManager gameManager)
        {
            var characterSnapshots = new List<CharacterSnapshot>();

            // GetCharacters(false): same source the victory loop uses, with no update side effect.
            // Story 7.4: resolve CharacterManager by the passed manager's NetworkManager instead of the
            // GameManager.characterManager pass-through (removed in 7.5); fixture-correct (per-NM), and
            // behaviour-identical in production (single NM).
            // Story 9.3 (Epic 9 / D3): route through the CompositionRoot surface (read slice) instead of the
            // bare CharacterManager.For backbone, so the only direct CharacterManager.For callers left are the
            // root itself and the test fixtures (AC1). CompositionRoot.For needs no registered root instance —
            // the Services resolver delegates to CharacterManager.For(nm) — so this is resolution-identical.
            foreach (var character in CompositionRoot.For(gameManager.NetworkManager).CharacterQuery.GetCharacters(false))
            {
                characterSnapshots.Add(MapCharacter(character));
            }

            // Day has no live source yet (no winning condition reads it); placeholder until the GameLoop extraction (Story 2.11).
            const int day = 0;
            int currentStateIndex = gameManager.currentGameStateIndex.Value;

            return new GameSnapshot(characterSnapshots, day, currentStateIndex);
        }

        private static CharacterSnapshot MapCharacter(Character character)
        {
            ulong ownerClientId = character.ownerClientId.Value;
            bool isFake = character.isFake;
            bool isCorrupted = character.isCorrupted.Value;
            bool isChained = character.isChained.Value;

            // Fake / null-role guard: a fake character may carry a null role — the live conditions skip fakes
            // before ever dereferencing role, so mapping must not NRE here. Default to unknown / no-hack.
            FactionType factionType = FactionType.unknown;
            ulong hackedByOmniscienceTarget = POmniscience.HACKED_CHARACTER_DEFAULT;

            if (!isFake && character.role != null)
            {
                factionType = character.role.factionType;
                hackedByOmniscienceTarget = ReadHackTarget(character.role);
            }

            return new CharacterSnapshot(
                ownerClientId,
                isFake,
                isCorrupted,
                isChained,
                factionType,
                hackedByOmniscienceTarget);
        }

        /// <summary>
        /// Reads hackedCharacterClientId from the LIVE POmniscience instance (the trap: it is a plain ulong on a
        /// NetworkBehaviour, never serialized in Role.NetworkSerialize). Defaults when no POmniscience is present.
        /// </summary>
        private static ulong ReadHackTarget(Role role)
        {
            var omniscience = (POmniscience)role.powers.Find(p => p.GetType() == typeof(POmniscience));
            return omniscience == null ? POmniscience.HACKED_CHARACTER_DEFAULT : omniscience.hackedCharacterClientId;
        }
    }
}
