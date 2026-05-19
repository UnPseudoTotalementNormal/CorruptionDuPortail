using System;

namespace Characters.Powers
{
    [Serializable]
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
