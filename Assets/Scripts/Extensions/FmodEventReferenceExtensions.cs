#region

using FMODUnity;

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
    }
}