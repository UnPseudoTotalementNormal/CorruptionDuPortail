using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace TooltipSystem
{
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager instance;
        
        [SerializeField] private TooltipWindow tooltipPrefab;
        
        [SerializeField] private Transform tooltipCanvas;
        
        private List<TooltipInstanceInfo> tooltipInstances = new();

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            foreach (var _tooltipInstanceInfo in tooltipInstances.ToList())
            {
                if (_tooltipInstanceInfo.isMouseOverLinkedGameObject || _tooltipInstanceInfo.isMouseOverTooltipWindow)
                {
                    _tooltipInstanceInfo.tooltipNoHoverTimer = _tooltipInstanceInfo.tooltipNoHoverTime;
                    continue;
                }

                _tooltipInstanceInfo.tooltipNoHoverTimer -= Time.deltaTime;

                if (_tooltipInstanceInfo.tooltipNoHoverTimer <= 0)
                {
                    CloseTooltip(_tooltipInstanceInfo.tooltipWindow, _tooltipInstanceInfo);
                }
            }
        }

        public TooltipWindow CreateNewTooltipFromGameObject(GameObject _linkedGameObject, string _tooltipTitle, string _tooltipDescription)
        {
            ITooltipTrigger _tooltipTrigger = _linkedGameObject.GetComponent<ITooltipTrigger>();
            
            Assert.IsNotNull(_tooltipTrigger, $"GameObject {_linkedGameObject.name} does not have a component that implements ITooltipTrigger. It is required to use CreateNewTooltipFromGameObject."); 
            TooltipWindow _newTooltip = Instantiate(tooltipPrefab, tooltipCanvas);
            
            _newTooltip.TitleText.text = _tooltipTitle;
            _newTooltip.DescriptionText.text = _tooltipDescription;
            
            LayoutRebuilder.ForceRebuildLayoutImmediate(_newTooltip.GetComponent<RectTransform>());
            
            var _tooltipInstanceInfo = new TooltipInstanceInfo(_linkedGameObject, _newTooltip);
            tooltipInstances.Add(_tooltipInstanceInfo);

            _tooltipTrigger.onMouseEnterTrigger += () => { OnMouseEnterComponent(_tooltipInstanceInfo); };
            _tooltipTrigger.onMouseExitTrigger += () => { OnMouseExitComponent(_tooltipInstanceInfo); };
            _tooltipTrigger.onTooltipForceClose += () => { CloseTooltip(_newTooltip, _tooltipInstanceInfo); };
            
            _newTooltip.onMouseEnterTrigger += () => { _tooltipInstanceInfo.isMouseOverTooltipWindow = true; };
            _newTooltip.onMouseExitTrigger += () => { _tooltipInstanceInfo.isMouseOverTooltipWindow = false; };
            
            return _newTooltip;
        }

        private void OnMouseEnterComponent(TooltipInstanceInfo _tooltipInstanceInfo)
        {
            if (!_tooltipInstanceInfo.tooltipWindow)
            {
                return;
            }
            
            _tooltipInstanceInfo.isMouseOverLinkedGameObject = true;
        }

        private void OnMouseExitComponent(TooltipInstanceInfo _tooltipInstanceInfo)
        {
            if (!_tooltipInstanceInfo.tooltipWindow)
            {
                return;
            }
            
            _tooltipInstanceInfo.isMouseOverLinkedGameObject = false;
        }
        
        public void CloseTooltip(TooltipWindow _tooltip, TooltipInstanceInfo _tooltipInstanceInfo = null)
        {
            if (!_tooltip)
            {
                return;
            }

            tooltipInstances.Remove(_tooltipInstanceInfo ?? tooltipInstances.First(x => x.tooltipWindow == _tooltip));

            Destroy(_tooltip.gameObject);
        }
        
        public bool IsTooltipOpenForGameObject(GameObject _linkedGameObject)
        {
            return tooltipInstances.Any(x => x.linkedGameObject == _linkedGameObject);
        }
    }

    public class TooltipInstanceInfo
    {
        public GameObject linkedGameObject;
        public TooltipWindow tooltipWindow;
        public bool isMouseOverLinkedGameObject = true;
        public bool isMouseOverTooltipWindow = false;

        public float tooltipNoHoverTimer;
        public float tooltipNoHoverTime = 0; // Time before tooltip closes when not hovered
        
        public TooltipInstanceInfo(GameObject _linkedGameObject, TooltipWindow _tooltipWindow)
        {
            linkedGameObject = _linkedGameObject;
            tooltipWindow = _tooltipWindow;
            tooltipNoHoverTimer = tooltipNoHoverTime;
        }
    }
}
