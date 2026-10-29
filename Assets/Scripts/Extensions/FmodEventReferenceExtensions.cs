#region

using System.Collections.Generic;
using FMODUnity;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Extensions
{
    public static class FmodEventReferenceExtensions
    {
        public static string GetPath(this EventReference _eventReference)
        {
            string _path;
            RuntimeManager.StudioSystem.lookupPath(_eventReference.Guid, out _path);
            return _path;
        }
        
        public static void NetworkSerialize<T>(this ref EventReference eventReference, BufferSerializer<T> serializer) where T : IReaderWriter
        {
            string eventPath = eventReference.GetPath() ?? string.Empty;
            serializer.SerializeValue(ref eventPath);
            if (serializer.IsReader && !string.IsNullOrEmpty(eventPath))
            {
                eventReference = RuntimeManager.PathToEventReference(eventPath);
            }
        }
        
        public static void TryPlayOneShot(this EventReference _eventReference)
        {
            if (!string.IsNullOrEmpty(_eventReference.GetPath()))
            {
                RuntimeManager.PlayOneShot(_eventReference.GetPath());
            }
            else if (!_eventReference.Guid.IsNull)
            {
                // An empty reference is a sound left unassigned on purpose; a set one that resolves to nothing is missing
                // from the banks.
                ReportMissingEvent(_eventReference.Guid.ToString());
            }
        }

        // Playtest log only: each missing FMOD event is reported once per process (a hover sound would repeat it).
        private static readonly HashSet<string> s_reportedMissing = new();

        /// <summary>Logs <c>audio.event.missing</c> once per <paramref name="_event"/> (a path or a GUID).</summary>
        public static void ReportMissingEvent(string _event, string _where = null)
        {
            if (string.IsNullOrEmpty(_event) || !s_reportedMissing.Add(_event))
            {
                return;
            }
            Diagnostics.Logs.GameLog.Warn("audio", "audio.event.missing", "event", _event, "where", _where);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_reportedMissing.Clear();
    }
}