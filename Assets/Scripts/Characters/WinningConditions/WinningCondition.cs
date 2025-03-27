namespace Characters.WinningConditions
{
    public abstract class WinningCondition
    {
        public abstract bool CheckCondition();
        public abstract void OnWin();
    }
}