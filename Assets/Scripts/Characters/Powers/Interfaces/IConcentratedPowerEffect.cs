namespace Characters.Powers.Interfaces
{
    public interface IConcentratedPowerEffect
    {
        public string concentratedEffectDescription { get; set; }
        
        public void OnConcentratedEffectServer();
    }
}