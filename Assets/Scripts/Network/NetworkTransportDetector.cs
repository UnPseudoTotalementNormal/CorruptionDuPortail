using Unity.Netcode;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Détecte automatiquement quel transport réseau est utilisé
    /// </summary>
    public static class NetworkTransportDetector
    {
        public enum TransportType
        {
            UnityTransport,
            FacepunchTransport,
            Unknown
        }

        /// <summary>
        /// Détecte le type de transport actuellement configuré
        /// </summary>
        public static TransportType GetCurrentTransportType()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[NetworkTransportDetector] NetworkManager not found!");
                return TransportType.Unknown;
            }

            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            if (transport == null)
            {
                Debug.LogWarning("[NetworkTransportDetector] No transport configured!");
                return TransportType.Unknown;
            }

            string typeName = transport.GetType().Name;

            if (typeName.Contains("UnityTransport"))
                return TransportType.UnityTransport;
            
            if (typeName.Contains("FacepunchTransport"))
                return TransportType.FacepunchTransport;

            Debug.LogWarning($"[NetworkTransportDetector] Unknown transport type: {typeName}");
            return TransportType.Unknown;
        }

        /// <summary>
        /// Vérifie si Unity Relay est utilisé
        /// </summary>
        public static bool IsUsingUnityRelay()
        {
            return GetCurrentTransportType() == TransportType.UnityTransport;
        }

        /// <summary>
        /// Vérifie si Facepunch/Steam est utilisé
        /// </summary>
        public static bool IsUsingFacepunch()
        {
            return GetCurrentTransportType() == TransportType.FacepunchTransport;
        }
    }
}

