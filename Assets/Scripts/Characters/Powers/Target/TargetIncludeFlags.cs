using System;

namespace Characters.Powers.Target
{
    [Flags]
    public enum TargetIncludeFlags
    {
        Self      = 1 << 0,
        Anomaly   = 1 << 1, 
        Marginal  = 1 << 2,
        Chosen    = 1 << 3,
        Corrupted = 1 << 4,
        Blessed   = 1 << 5,
        Chained   = 1 << 6,
        Healed    = 1 << 7,
        Fake      = 1 << 31
    }
}