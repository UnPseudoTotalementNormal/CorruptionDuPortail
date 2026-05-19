#region

using AudioSystem;
using Extensions;
using FMODUnity;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Gère tous les effets sonores de la carte.
    /// Responsabilité unique : lecture des sons.
    /// </summary>
    public class CardSoundHandler : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Card Sounds")]
        [SerializeField] private EventReference cardFlipSound;
        [SerializeField] private EventReference cardUnflipSound;
        [SerializeField] private EventReference cardHoverSound;
        [SerializeField] private EventReference cardUnhoverSound;
        [SerializeField] private EventReference cardClickSound;

        #endregion

        #region Public Methods

        public void PlayFlipSound()
        {
            cardFlipSound.TryPlayOneShot();
        }

        public void PlayUnflipSound()
        {
            cardUnflipSound.TryPlayOneShot();
        }

        public void PlayHoverSound()
        {
            cardHoverSound.TryPlayOneShot();
        }

        public void PlayUnhoverSound()
        {
            cardUnhoverSound.TryPlayOneShot();
        }

        public void PlayClickSound()
        {
            cardClickSound.TryPlayOneShot();
        }

        #endregion
    }
}

