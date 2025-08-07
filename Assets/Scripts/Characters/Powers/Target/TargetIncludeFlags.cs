using System;

namespace Characters.Powers.Target
{
    [Flags]
    public enum TargetIncludeFlags : long
    {
        Self      = 1L << 0,
        Anomaly   = 1L << 1, 
        Marginal  = 1L << 2,
        Chosen    = 1L << 3,
        Corrupted = 1L << 4,
        Blessed   = 1L << 5,
        Fake      = 1L << 63
    }
}