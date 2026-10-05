#region

using System.Collections.Generic;
using Unity.Netcode;

#endregion

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// NET-08 (epic-network-sync-hardening): every spawned <see cref="Power"/>, per NetworkManager. A character's power
    /// list (<c>role.powers</c>) is a PROJECTION of this registry filtered by the replicated
    /// <see cref="Power.ownerClientId"/> and ordered by the replicated <see cref="Power.grantOrder"/> — identical on
    /// every peer by construction. It replaces the three event RPCs that used to edit the list by hand
    /// (CheckForPowersRpc / RemovePowerFromCharacterPowerListRpc / OnReparentedClientRpc), which only converged when
    /// spawn, parent-sync, despawn and RPCs happened to arrive in a favourable order.
    /// </summary>
    public static class PowerRegistry
    {
        private static readonly Dictionary<NetworkManager, List<Power>> s_byNetworkManager = new();
        private static int s_nextGrantOrder;

        /// <summary>Server: a monotonically increasing grant stamp (the replicated power-list order).</summary>
        public static int NextGrantOrder() => ++s_nextGrantOrder;

        public static void Register(Power _power)
        {
            if (_power == null || _power.NetworkManager == null)
            {
                return;
            }
            if (!s_byNetworkManager.TryGetValue(_power.NetworkManager, out List<Power> _powers))
            {
                _powers = new List<Power>();
                s_byNetworkManager[_power.NetworkManager] = _powers;
            }
            if (!_powers.Contains(_power))
            {
                _powers.Add(_power);
            }
        }

        public static void Unregister(Power _power)
        {
            if (_power == null)
            {
                return;
            }
            foreach (var _pair in s_byNetworkManager)
            {
                _pair.Value.Remove(_power);
            }
        }

        /// <summary>The live powers owned by <paramref name="_ownerClientId"/> on that NetworkManager, in grant order.</summary>
        public static List<Power> OwnedBy(NetworkManager _networkManager, ulong _ownerClientId)
        {
            var _result = new List<Power>();
            if (_networkManager == null || !s_byNetworkManager.TryGetValue(_networkManager, out List<Power> _powers))
            {
                return _result;
            }

            _powers.RemoveAll(_p => !_p);
            foreach (Power _power in _powers)
            {
                if (_power.IsSpawned && _power.ownerClientId.Value == _ownerClientId)
                {
                    _result.Add(_power);
                }
            }
            _result.Sort((_a, _b) =>
            {
                int _byGrant = _a.grantOrder.Value.CompareTo(_b.grantOrder.Value);
                return _byGrant != 0 ? _byGrant : _a.NetworkObjectId.CompareTo(_b.NetworkObjectId);
            });
            return _result;
        }

        /// <summary>Per-session reset (CompositionRoot.ResetSessionStatics); entries also leave on despawn.</summary>
        public static void ResetSessionStatics() => s_byNetworkManager.Clear();

#if UNITY_EDITOR
        // Domain reload is disabled in this project: drop registries left by a previous Play session.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled() => s_byNetworkManager.Clear();
#endif
    }
}
