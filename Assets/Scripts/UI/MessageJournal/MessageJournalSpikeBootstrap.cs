using UnityEngine;

namespace UI.MessageJournal
{
    // Spike/harness only: opens the journal overlay on Start so the preview scene shows it immediately.
    // Not used in GameScene (there the sacoche and the awakening reveal drive Open()).
    public class MessageJournalSpikeBootstrap : MonoBehaviour
    {
        [SerializeField] private MessageJournalController controller;
        [SerializeField] private bool animateNewest = true;

        private void Start()
        {
            if (controller != null)
            {
                controller.Open(animateNewest);
            }
        }
    }
}
