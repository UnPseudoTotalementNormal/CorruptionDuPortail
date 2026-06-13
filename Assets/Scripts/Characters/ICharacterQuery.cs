using System;
using System.Collections.Generic;

namespace Characters
{
    /// <summary>
    /// Story 9.1 (Epic 9 / D3) — the READ slice of CharacterManager's public surface
    /// (refactor-architecture-despaghetti.md §2.3). Read-only consumers depend on this narrow intent
    /// instead of the whole CharacterManager God Object. Members are lifted VERBATIM from
    /// CharacterManager (the 9.1 usage census) — no signature "improvement". The command surface
    /// (AddNewCharacter / RemoveCharacter / Give* / AskForUpdate*Rpc / Spawn* / Set* / Register*) and
    /// the adapter internals (GetSafeRpcTarget / IsLocalOrSimulated) stay OFF this interface — they are
    /// ICharacterCommand's territory (story 9.2). Lives in the Game assembly (it exposes the engine
    /// Character type), not Domain.
    /// </summary>
    public interface ICharacterQuery
    {
        /// <summary>The character owned by the given client id (null if absent).</summary>
        Character GetCharacter(ulong _characterId, bool _triggerUpdate = true);

        /// <summary>A defensive copy of the current resolved character list.</summary>
        List<Character> GetCharacters(bool _triggerUpdate = true);

        /// <summary>The local client's character (null if absent).</summary>
        Character GetLocalCharacter(bool _triggerUpdate = true);

        /// <summary>The local client id (honours the debug-possessed identity).</summary>
        ulong GetLocalClientId();

        /// <summary>Raised when the resolved character list changes.</summary>
        event Action<List<Character>> onCharactersListUpdated;

        /// <summary>Raised when the local identity changes (debug possession).</summary>
        event Action onLocalIdentityChanged;
    }
}
