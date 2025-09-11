#region

using FMOD;
using FMODUnity;

#endregion

namespace Extensions
{
    public static class FMODGUIDExtensions
    {
        public static string GetPath(this GUID _guid)
        {
            string _path;

            //FMOD.Studio.System sys;
            //FMOD.Studio.System.create(out sys);
            //sys.lookupPath(guid, out path);

            RuntimeManager.StudioSystem.lookupPath(_guid, out _path);

            return _path;
        }
    }
}