using System;

namespace Characters
{
    [Serializable]
    public class Character
    {
        public Role role;
        public ulong ownerClientId;
    }
}