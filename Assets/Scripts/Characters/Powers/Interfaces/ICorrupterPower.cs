using System;

namespace Characters.Powers.Interfaces
{
    public interface ICorrupterPower
    {
        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;
    }
}