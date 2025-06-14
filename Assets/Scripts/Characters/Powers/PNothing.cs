namespace Characters.Powers
{
    public class PNothing : Power
    {
        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }
    }
}
