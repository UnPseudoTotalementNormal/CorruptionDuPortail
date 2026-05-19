#region

using FMODUnity;
using Unity.Netcode;

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
        }
    }
}