#region

using System;
using System.Linq;
using DG.Tweening;
using Extensions;
using FMODUnity;
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

        [Header("Feedback")]
        [SerializeField] private EventReference hoverSound;
        [SerializeField] private EventReference clickSound;
        [SerializeField] private float hoverScale = 1.12f;
        [SerializeField] private float hoverTweenDuration = 0.15f;
        [SerializeField] private float clickPunchIntensity = 0.18f;
        [SerializeField] private float clickPunchDuration = 0.25f;

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
            
            if (power != null)
            {
                power.onStartUse -= StartUsePower;
                power.onStopUse -= StopUsePower;
                
                power.onStartUse += StartUsePower;
                power.onStopUse += StopUsePower;
            }

            if (powerViusalTransform != null)
            {
                Destroy(powerViusalTransform.gameObject);
            }
            if (powerColliderTransform != null)
            {
                Destroy(powerColliderTransform.gameObject);
            }

            GameObject _spawnPrefab = defaultPower3DModel;
            if (power.power3DObjectPrefab)
            {
                _spawnPrefab = power.power3DObjectPrefab;
            }
            GameObject _power3DModel = Instantiate(_spawnPrefab, modelParentTransform);
            GameObject _power3DModelCollider = Instantiate(_spawnPrefab, modelParentTransform);
            powerViusalTransform = _power3DModel.transform;
            powerColliderTransform = _power3DModelCollider.transform;
            // Snap the spawned copies to the parent origin but keep the model prefab's
            // authored localRotation — FBX imports bake an axis-correction rotation on the
            // root, and forcing identity here lays the model on its side.
            ResetLocalPositionAndScale(powerViusalTransform);
            ResetLocalPositionAndScale(powerColliderTransform);
            
            foreach (var _renderer in powerColliderTransform.GetComponentsInChildren<Renderer>(true).ToList())
            {
                Destroy(_renderer);
            }
            foreach (var _collider in powerViusalTransform.GetComponentsInChildren<Collider>().ToList())
            {
                Destroy(_collider);
            }
        }
        
        private static void ResetLocalPositionAndScale(Transform _transform)
        {
            _transform.localPosition = Vector3.zero;
            _transform.localScale = Vector3.one;
        }

        private void OnDestroy()
        {
            if (power != null)
            {
                power.onStartUse -= StartUsePower;
                power.onStopUse -= StopUsePower;
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
            clickSound.TryPlayOneShot();
            PunchClick();
            OnButtonClicked();
        }

        private void ScaleHover(bool _entered)
        {
            if (powerViusalTransform == null)
            {
                return;
            }
            powerViusalTransform.DOKill();
            float _target = _entered ? hoverScale : 1f;
            powerViusalTransform.DOScale(_target, hoverTweenDuration).SetEase(Ease.OutQuint);
        }

        private void PunchClick()
        {
            if (powerViusalTransform == null)
            {
                return;
            }
            powerViusalTransform.DOPunchScale(Vector3.one * clickPunchIntensity, clickPunchDuration, 1, 0.2f);
        }

        public void OnPointerClick(PointerEventData _eventData)
        {
            OnMouseDown();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            hovering = true;
            hoverSound.TryPlayOneShot();
            ScaleHover(true);
            if (power.isCurrentlyUsed)
            {
                return;
            }
            if (powerViusalTransform != null)
            {
                int hoverLayer = LayerMask.NameToLayer("Outline_Hover");
                powerViusalTransform.gameObject.SetLayerRecursively(hoverLayer);
            }
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            hovering = false;
            ScaleHover(false);
            if (power.isCurrentlyUsed)
            {
                return;
            }
            if (powerViusalTransform != null)
            {
                int defaultLayer = 0;
                powerViusalTransform.gameObject.SetLayerRecursively(defaultLayer);
            }
        }
        
        private void StartUsePower()
        {
            if (powerViusalTransform == null) return;
            
            int usedLayer = LayerMask.NameToLayer("Outline_Used");
            powerViusalTransform.gameObject.SetLayerRecursively(usedLayer);
        }
        
        private void StopUsePower()
        {
            if (powerViusalTransform == null) return;
            
            int defaultLayer = 0;
            powerViusalTransform.gameObject.SetLayerRecursively(defaultLayer);
        }

        private void Update()
        {
            if (power == null || powerViusalTransform == null || power.isCurrentlyUsed || hovering || !power.CanUse())
            {
                return;
            }
            
            int highlightLayer = LayerMask.NameToLayer("Outline_Highlight");
            powerViusalTransform.gameObject.SetLayerRecursively(highlightLayer);
        }
    }
}