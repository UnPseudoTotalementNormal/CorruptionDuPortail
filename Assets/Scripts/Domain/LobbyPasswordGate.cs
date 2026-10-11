using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Password of a Steam lobby. Unlike UGS, Steam lobbies have no server-side password: anyone may enter the Steam
    /// lobby. The host therefore checks the password the joiner sent in its connection request
    /// (<see cref="ConnectionPayload.LobbyPassword"/>) during NGO connection approval, and refuses with
    /// <see cref="WrongPasswordReason"/>. No password set on the host = every joiner is accepted.
    /// </summary>
    public static class LobbyPasswordGate
    {
        public const string WrongPasswordReason = "Mot de passe incorrect.";

        public static bool Accepts(string required, string given)
        {
            return string.IsNullOrEmpty(required) || string.Equals(required, given ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
