#region

using System;
using Characters;
using Characters.Powers;
using DG.Tweening;
using UnityEngine;

#endregion

namespace Board.UI.PowerBar
{
    /// <summary>
    /// Classe de base abstraite pour les objets de la barre de pouvoirs.
    /// Les classes dérivées doivent implémenter la logique spécifique (UI, 3D, etc.)
    /// </summary>
    public abstract class PowersBarObject : MonoBehaviour
    {
        [HideInInspector] public Power power;
        [HideInInspector] public Character fromCharacter;

        // Cached at build time: a one-shot stolen copy is already configured (isStolenCopy=true) before its bar
        // object is created. Kept so the bar can still tell "this was a stolen copy" AFTER the Power despawns and
        // `power` goes Unity-null — needed to animate it out rather than pop it when despawn beats the useLeft poll.
        [HideInInspector] public bool wasStolenCopy;

        public event Action<Power> onPowerBarObjectClicked;

        protected virtual void Awake()
        {
            InitializeComponents();
        }

        protected virtual void Start()
        {
            SetupInteraction();
        }

        public void SetPower(Power _power, Character _fromCharacter)
        {
            power = _power;
            fromCharacter = _fromCharacter;
            wasStolenCopy = _power && _power.isStolenCopy.Value;
            Init();
        }

        /// <summary>
        /// Initialise les composants spécifiques (UI, 3D, etc.)
        /// </summary>
        protected abstract void InitializeComponents();

        /// <summary>
        /// Configure l'interaction (boutons, clics, etc.)
        /// </summary>
        protected abstract void SetupInteraction();

        /// <summary>
        /// Initialise l'affichage du pouvoir
        /// </summary>
        protected virtual void Init()
        {
            UpdatePowerDisplay();
        }

        /// <summary>
        /// Met à jour l'affichage du pouvoir (nom, description, icône, etc.)
        /// </summary>
        protected abstract void UpdatePowerDisplay();

        /// <summary>
        /// Active ou désactive l'interactivité de l'objet
        /// </summary>
        public abstract void SetInteractable(bool _interactable);

        protected void OnButtonClicked()
        {
            onPowerBarObjectClicked?.Invoke(power);
        }

        [SerializeField] private float animateOutDuration = 0.25f;
        private bool _isAnimatingOut;

        /// <summary>
        /// Plays a shrink-to-zero animation then destroys this bar object. Used when a spent one-shot stolen copy
        /// leaves the bar ("temporaire = perdu") so the disappearance reads gradually instead of popping out.
        /// Idempotent. The tween lives on this UI transform, independent of the (soon-despawned) Power.
        /// </summary>
        public void AnimateOutThenDestroy()
        {
            if (_isAnimatingOut)
            {
                return;
            }
            _isAnimatingOut = true;

            SetInteractable(false);
            transform.DOKill();
            transform.DOScale(Vector3.zero, animateOutDuration)
                .SetEase(Ease.InBack)
                .OnComplete(() => Destroy(gameObject));
        }

        protected virtual void OnDestroy()
        {
            transform.DOKill();
        }
    }
}