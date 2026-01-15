using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Controllers
{
    /// <summary>
    /// Base controller logic that can be used with MonoBehaviour or NetworkBehaviour
    /// </summary>
    public class ControllerBase
    {
        public Dictionary<string, bool> activeSources = new();
        public bool currentActiveState = true;
        public event Action<bool> onActiveStateChanged;
        
        public bool GetActiveSource(string source)
        {
            return activeSources.GetValueOrDefault(source, false);
        }
        
        public void SetActiveSource(string source, bool isActive)
        {
            activeSources[source] = isActive;
            bool newActiveState = IsActive();
            if (newActiveState != currentActiveState)
            {
                currentActiveState = newActiveState;
                onActiveStateChanged?.Invoke(currentActiveState);
            }
        }

        public void ToggleActiveSource(string source, bool baseState = true)
        {
            if (activeSources.TryAdd(source, baseState))
            {
                SetActiveSource(source, baseState);
                return;
            }
            SetActiveSource(source, !GetActiveSource(source));
        }
        
        public bool IsActive()
        {
            foreach (KeyValuePair<string, bool> source in activeSources)
            {
                if (!source.Value)
                {
                    return false;
                }
            }
            return true;
        }
    }
    
    public abstract class MonoController : MonoBehaviour
    {
        private readonly ControllerBase _controller = new();
        
        public Dictionary<string, bool> activeSources => _controller.activeSources;
        public bool currentActiveState => _controller.currentActiveState;
        public event Action<bool> onActiveStateChanged
        {
            add => _controller.onActiveStateChanged += value;
            remove => _controller.onActiveStateChanged -= value;
        }
        
        public bool GetActiveSource(string source) => _controller.GetActiveSource(source);
        public void SetActiveSource(string source, bool isActive) => _controller.SetActiveSource(source, isActive);
        public void ToggleActiveSource(string source, bool baseState = true) => _controller.ToggleActiveSource(source, baseState);
        public bool IsActive() => _controller.IsActive();
    }
    
    public abstract class NetworkController : NetworkBehaviour
    {
        private readonly ControllerBase _controller = new();
        
        public Dictionary<string, bool> activeSources => _controller.activeSources;
        public bool currentActiveState => _controller.currentActiveState;
        public event Action<bool> onActiveStateChanged
        {
            add => _controller.onActiveStateChanged += value;
            remove => _controller.onActiveStateChanged -= value;
        }
        
        public bool GetActiveSource(string source) => _controller.GetActiveSource(source);
        public void SetActiveSource(string source, bool isActive) => _controller.SetActiveSource(source, isActive);
        public void ToggleActiveSource(string source, bool baseState = true) => _controller.ToggleActiveSource(source, baseState);
        public bool IsActive() => _controller.IsActive();
    }
}