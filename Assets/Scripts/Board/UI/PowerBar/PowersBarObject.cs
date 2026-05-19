#region

using System;
using Characters;
using Characters.Powers;
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
    }
}