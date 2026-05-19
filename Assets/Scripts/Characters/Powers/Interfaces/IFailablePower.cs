using System;

namespace Characters.Powers.Interfaces
{
    public interface IFailablePower
    {
        public event Action onPowerSuccessful;
        public event Action onPowerFailed;
    }
}