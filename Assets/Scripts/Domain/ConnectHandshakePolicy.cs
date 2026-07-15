using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>Why a connect wait was declared a failure (surfaced to the join UI for a precise message).</summary>
    public enum ConnectFailReason
    {
        /// <summary>Not a failure.</summary>
        None = 0,

        /// <summary>The NetworkManager was torn down / stopped listening mid-wait (server rejection, transport give-up).</summary>
        SessionEnded,

        /// <summary>No approval / no synchronization ever started within the short approval window — dead or silent host.</summary>
        ApprovalTimeout,

        /// <summary>Synchronization started but never completed within the generous total window — a genuinely stuck load.</summary>
        TotalTimeout,
    }

    /// <summary>Coarse outcome of one <see cref="ConnectHandshakePolicy.Evaluate"/> poll.</summary>
    public enum ConnectWaitOutcome
    {
        /// <summary>Keep polling.</summary>
        Waiting = 0,

        /// <summary>The client is fully connected (synchronized) — stop, success.</summary>
        Connected,

        /// <summary>Give up — stop, failure (see <see cref="ConnectWaitState.Reason"/>).</summary>
        Failed,
    }

    /// <summary>Immutable result of a single connect-wait poll: an outcome plus, when failed, a reason.</summary>
    public readonly struct ConnectWaitState : IEquatable<ConnectWaitState>
    {
        public readonly ConnectWaitOutcome Outcome;
        public readonly ConnectFailReason Reason;

        public ConnectWaitState(ConnectWaitOutcome outcome, ConnectFailReason reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public static ConnectWaitState Connected => new ConnectWaitState(ConnectWaitOutcome.Connected, ConnectFailReason.None);
        public static ConnectWaitState Waiting => new ConnectWaitState(ConnectWaitOutcome.Waiting, ConnectFailReason.None);
        public static ConnectWaitState Failed(ConnectFailReason reason) => new ConnectWaitState(ConnectWaitOutcome.Failed, reason);

        public bool Equals(ConnectWaitState other) => Outcome == other.Outcome && Reason == other.Reason;
        public override bool Equals(object obj) => obj is ConnectWaitState other && Equals(other);
        public override int GetHashCode() => ((int)Outcome * 397) ^ (int)Reason;
        public override string ToString() => Outcome == ConnectWaitOutcome.Failed ? $"Failed({Reason})" : Outcome.ToString();
    }

    /// <summary>
    /// Pure, engine-free decision for the client join wait (investigation join-load-timeout-kick). The one
    /// legacy deadline was gated on FULL NGO scene synchronization (<c>IsConnectedClient</c> flips only at
    /// <c>SynchronizeComplete</c>), so a slow-PC GameScene load past the timeout kicked the joiner mid-load.
    ///
    /// This splits the wait into two phases keyed on whether synchronization has STARTED (the caller sets
    /// <paramref name="syncStarted"/> from <c>NetworkSceneManager.OnSynchronize</c>, which fires client-side
    /// once the server has approved the client and begun syncing it):
    /// - a SHORT <paramref name="approvalDeadlineSeconds"/> that applies ONLY until sync starts — fast-fails a
    ///   dead / silent host that never approves us;
    /// - a GENEROUS <paramref name="totalDeadlineSeconds"/> absolute cap that lets an honest slow load finish
    ///   yet still bounds a genuinely stuck synchronization.
    ///
    /// No clock and no NGO here — the caller measures wall-clock elapsed and reads NGO state; this class only
    /// decides. Mirrors <see cref="LivenessThreshold"/> / <see cref="LivenessPumpPolicy"/> (pure EditMode unit).
    /// </summary>
    public static class ConnectHandshakePolicy
    {
        /// <param name="elapsedSeconds">Wall-clock seconds since the wait began (since <c>StartClient</c>).</param>
        /// <param name="syncStarted">True once the server has started synchronizing us (OnSynchronize fired).</param>
        /// <param name="isConnected">True once fully synchronized (<c>NetworkManager.IsConnectedClient</c>).</param>
        /// <param name="sessionAlive">False if the NetworkManager is null / no longer listening (reject / teardown).</param>
        /// <param name="approvalDeadlineSeconds">Short window; applies only while <paramref name="syncStarted"/> is false.</param>
        /// <param name="totalDeadlineSeconds">Absolute cap regardless of phase. Must be &gt;= the approval deadline.</param>
        /// <returns>Connected (success) / Failed (with reason) / Waiting (keep polling). First match wins.</returns>
        public static ConnectWaitState Evaluate(
            double elapsedSeconds,
            bool syncStarted,
            bool isConnected,
            bool sessionAlive,
            double approvalDeadlineSeconds,
            double totalDeadlineSeconds)
        {
            // Success beats every timeout: a load that just finished must never be reported as a failure.
            if (isConnected)
            {
                return ConnectWaitState.Connected;
            }

            // A rejected join (DisconnectReason) or a torn-down NetworkManager fails immediately, before any timer.
            if (!sessionAlive)
            {
                return ConnectWaitState.Failed(ConnectFailReason.SessionEnded);
            }

            // Sync never started in the short window ⇒ dead / silent host. Once sync starts, this cap no longer applies.
            if (!syncStarted && elapsedSeconds >= approvalDeadlineSeconds)
            {
                return ConnectWaitState.Failed(ConnectFailReason.ApprovalTimeout);
            }

            // Absolute safety cap: synchronization began but never completed ⇒ a genuinely stuck load.
            if (elapsedSeconds >= totalDeadlineSeconds)
            {
                return ConnectWaitState.Failed(ConnectFailReason.TotalTimeout);
            }

            return ConnectWaitState.Waiting;
        }
    }
}
