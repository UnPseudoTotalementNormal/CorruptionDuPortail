#region

using System;
using System.Linq;
using Extensions;
using GameLogic;
using TooltipSystem;
using UnityEngine;
using UnityEngine.EventSystems;

#endregion

namespace Board.UI.PowerBar
{
    /// <summary>
    /// Implémentation 3D de PowersBarObject utilisant des objets 3D dans le monde
    /// </summary>
    public class PowerBarObject3D : PowersBarObject, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Transform modelParentTransform;
        private Collider powerCollider;
        private HoverTooltipComponent hoverTooltipComponent;
        
        [SerializeField] private GameObject defaultPower3DModel;

        private Transform powerViusalTransform;
        private Transform powerColliderTransform;
        
        private bool hovering;

        protected override void InitializeComponents()
        {
            powerCollider = GetComponent<Collider>();
            hoverTooltipComponent = GetComponentInChildren<HoverTooltipComponent>();
        }

        protected override void SetupInteraction()
        {
            SetTooltip();
        }
        
        protected override void Init()
        {
            base.Init();
            
            power.onStartUse += StartUsePower;
            power.onStopUse += StopUsePower;

            GameObject _spawnPrefab = defaultPower3DModel;
            if (power.power3DObjectPrefab)
            {
                _spawnPrefab = power.power3DObjectPrefab;
            }
            GameObject _power3DModel = Instantiate(_spawnPrefab, modelParentTransform);
            GameObject _power3DModelCollider = Instantiate(_spawnPrefab, modelParentTransform);
            powerViusalTransform = _power3DModel.transform;
            powerColliderTransform = _power3DModelCollider.transform;
            powerViusalTransform.ResetLocalValues();
            powerColliderTransform.transform.ResetLocalValues();
            
            foreach (var _renderer in powerColliderTransform.GetComponentsInChildren<Renderer>(true).ToList())
            {
                Destroy(_renderer);
            }
            foreach (var _collider in powerViusalTransform.GetComponentsInChildren<Collider>().ToList())
            {
                Destroy(_collider);
            }
        }

        private void SetTooltip()
        {
            if (hoverTooltipComponent != null)
            {
                hoverTooltipComponent.SetTooltipTitle(power.powerName.ToString());
                
                var _description = power.powerDescription.ToString();
                
                if (power.powerComponents.Count == 0)
                {
                    hoverTooltipComponent.SetTooltipDescription(_description);
                    return;
                }
                
                _description += "\n";
                
                for (var _index = 0; _index < power.powerComponents.Count; _index++)
                {
                    var _powerComponent = power.powerComponents[_index];
                    if (_index > 0)
                    {
                        _description += ", ";
                    }
                    _description += $"<link=powercomponent_{power.ownerClientId.Value}_{power.NetworkObjectId}_{_index}>{_powerComponent.componentName.ToString()}</link>";
                }
                
                hoverTooltipComponent.SetTooltipDescription(_description);
            }
        }

        protected override void UpdatePowerDisplay()
        {
            if (power == null) return;
        }

        public override void SetInteractable(bool _interactable)
        {
            if (powerCollider != null)
            {
                powerCollider.enabled = _interactable;
            }
        }

        private void OnMouseDown()
        {
            OnButtonClicked();
        }

        public void OnPointerClick(PointerEventData _eventData)
        {
            OnMouseDown();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            hovering = true;
            if (power.isCurrentlyUsed)
            {
                return;
            }
            int hoverLayer = LayerMask.NameToLayer("Outline_Hover");
            powerViusalTransform.gameObject.SetLayerRecursively(hoverLayer);
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            hovering = false;
            if (power.isCurrentlyUsed)
            {
                return;
            }
            int defaultLayer = 0;
            powerViusalTransform.gameObject.SetLayerRecursively(defaultLayer);
        }
        
        private void StartUsePower()
        {
            int usedLayer = LayerMask.NameToLayer("Outline_Used");
            powerViusalTransform.gameObject.SetLayerRecursively(usedLayer);
        }
        
        private void StopUsePower()
        {
            int defaultLayer = 0;
            powerViusalTransform.gameObject.SetLayerRecursively(defaultLayer);
        }

        private void Update()
        {
            if (power.isCurrentlyUsed || hovering || !power.CanUse())
            {
                return;
            }
            
            int highlightLayer = LayerMask.NameToLayer("Outline_Highlight");
            powerViusalTransform.gameObject.SetLayerRecursively(highlightLayer);
        }
    }
}