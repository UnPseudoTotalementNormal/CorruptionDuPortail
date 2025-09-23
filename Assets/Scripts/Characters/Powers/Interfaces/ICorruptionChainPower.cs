namespace Characters.Powers.Interfaces
{
    public interface ICorruptionChainPower : ICorrupterPower
    {
        public int maxCorruptionChain { get; set; }
        public int currentCorruptionChain { get; set; }
    }
}