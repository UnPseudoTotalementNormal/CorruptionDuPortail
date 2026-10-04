#region

using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// NET-06 (epic-network-sync-hardening): ordering barrier between NetworkVariable deltas and RPCs.
    ///
    /// NGO 2.12 sends an RPC in the frame it is called, but NetworkVariable deltas only on the NEXT network tick
    /// (<c>NetworkBehaviourUpdater.OnNetworkTick</c>). So a client RPC sent right after a server NetworkVariable write
    /// overtakes that write: the client handler reads the OLD value. Calling <see cref="TryFlush"/> right before such
    /// an RPC enqueues every pending delta first; with reliable sequenced delivery the client then applies the deltas
    /// before running the RPC.
    ///
    /// Uses NGO's own (internal) <c>NetworkBehaviourUpdater.NetworkBehaviourUpdate</c> through reflection: the same
    /// call NGO makes on every tick, just earlier. If a future NGO version renames it, the flush degrades to a no-op
    /// (previous behaviour) and logs one error so the regression is visible.
    /// </summary>
    public static class NetworkVariableFlush
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly PropertyInfo s_updaterProperty =
            typeof(NetworkManager).GetProperty("BehaviourUpdater", Any);

        private static readonly MethodInfo s_updateMethod =
            typeof(NetworkBehaviourUpdater).GetMethod("NetworkBehaviourUpdate", Any, null, new[] { typeof(bool) }, null);

        private static bool s_reportedMissing;

        /// <summary>Server-side: enqueue all pending NetworkVariable deltas now. Returns false when it could not.</summary>
        public static bool TryFlush(NetworkManager _networkManager)
        {
            if (_networkManager == null || !_networkManager.IsServer || !_networkManager.IsListening)
            {
                return false;
            }

            if (s_updaterProperty == null || s_updateMethod == null)
            {
                ReportMissingOnce("NGO internals not found (NetworkManager.BehaviourUpdater / NetworkBehaviourUpdate).");
                return false;
            }

            try
            {
                object _updater = s_updaterProperty.GetValue(_networkManager);
                if (_updater == null)
                {
                    return false;
                }
                s_updateMethod.Invoke(_updater, new object[] { false });
                return true;
            }
            catch (Exception _exception)
            {
                ReportMissingOnce(_exception.GetBaseException().Message);
                return false;
            }
        }

        private static void ReportMissingOnce(string _detail)
        {
            if (s_reportedMissing)
            {
                return;
            }
            s_reportedMissing = true;
            Debug.LogError($"[DESYNC] NetworkVariableFlush unavailable, state-change RPCs may overtake NetworkVariable deltas: {_detail}");
        }
    }
}
