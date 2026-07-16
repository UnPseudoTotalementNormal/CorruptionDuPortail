using UnityEngine;

namespace UI.MessageJournal
{
    // Spike/harness only: opens the journal overlay on Start so the preview scene shows it immediately.
    // Not used in GameScene (there the sacoche and the awakening reveal drive Open()).
    public class MessageJournalSpikeBootstrap : MonoBehaviour
    {
        [SerializeField] private MessageJournalController controller;
        [SerializeField] private bool animateNewest = true;
        // false = preview the night reveal (non-dismissible); true = preview the sacoche browse (click-outside closes).
        [SerializeField] private bool dismissible = false;

        private void Start()
        {
            if (controller != null)
            {
                controller.Open(animateNewest, dismissible);
            }
        }
    }
}
