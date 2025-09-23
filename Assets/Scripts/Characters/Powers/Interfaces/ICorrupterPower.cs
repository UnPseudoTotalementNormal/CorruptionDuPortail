using System;

namespace Characters.Powers.Interfaces
{
    public interface ICorrupterPower
    {
        public event Action<Character> onCharacterCorrupted;
        public void InvokeOnCharacterCorrupted(Character _character);
    }
}