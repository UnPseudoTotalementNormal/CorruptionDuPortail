using System.Collections.Generic;
using UnityEngine;

namespace FX
{
    /// <summary>What an outlined object tells the player. Each state is drawn by its own style of the renderers'
    /// OutlineFeature (Game.Rendering), matched by rendering layer name.</summary>
    public enum OutlineState
    {
        None,
        /// <summary>Can be used now (a power ready this turn).</summary>
        Available,
        /// <summary>Under the cursor or the reticle.</summary>
        Hover,
        /// <summary>Being used (a power whose target is being picked).</summary>
        Active,
        /// <summary>Chosen by the local player (the cat carrying his vote).</summary>
        Selected,
    }

    /// <summary>
    /// Outlines an object's mesh renderers by switching their rendering layer bits ("Outline Hover"…), which the
    /// renderer feature turns into one clean outline around the whole object. Never touches the GameObject layer, so
    /// raycasts, camera culling and the card picker's layers are left alone.
    /// Added on demand by <see cref="OutlineExtensions.SetOutline"/>; the renderers are listed again on every change of
    /// state, so parts switched on later (cat cosmetics) are outlined from the next change.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutlineTarget : MonoBehaviour
    {
        private static readonly string[] LayerNames =
        {
            null, "Outline Available", "Outline Hover", "Outline Active", "Outline Selected",
        };

        // Objects currently outlined, shared with the renderer feature: it skips all its work at zero.
        private static readonly HashSet<OutlineTarget> Outlined = new();
        private static readonly int ActiveCountId = Shader.PropertyToID("_CdpOutlineCount");
        private static uint[] _bits;
        private static uint _allBits;

        private readonly List<Renderer> _renderers = new();
        private OutlineState _state;

        public OutlineState State => _state;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Outlined.Clear();
            _bits = null;
            _allBits = 0;
            Shader.SetGlobalInt(ActiveCountId, 0);
        }

        public void SetState(OutlineState _newState)
        {
            if (_newState == _state)
            {
                return;
            }
            ResolveBits();
            _state = _newState;
            uint _bit = _bits[(int)_newState];
            GetComponentsInChildren(true, _renderers);
            foreach (Renderer _renderer in _renderers)
            {
                if (_renderer is MeshRenderer || _renderer is SkinnedMeshRenderer)
                {
                    _renderer.renderingLayerMask = (_renderer.renderingLayerMask & ~_allBits) | _bit;
                }
            }
            if (_newState == OutlineState.None)
            {
                Outlined.Remove(this);
            }
            else
            {
                Outlined.Add(this);
            }
            Shader.SetGlobalInt(ActiveCountId, Outlined.Count);
        }

        private void OnDisable()
        {
            SetState(OutlineState.None);
        }

        private static void ResolveBits()
        {
            if (_bits != null)
            {
                return;
            }
            _bits = new uint[LayerNames.Length];
            for (int _i = 1; _i < LayerNames.Length; _i++)
            {
                _bits[_i] = RenderingLayerMask.GetMask(LayerNames[_i]);
                if (_bits[_i] == 0)
                {
                    Debug.LogWarning($"[Outline] Rendering layer '{LayerNames[_i]}' missing (Project Settings > Tags and Layers).");
                }
                _allBits |= _bits[_i];
            }
        }
    }

    public static class OutlineExtensions
    {
        /// <summary>Outlines the object and its children with the given state (None removes the outline).</summary>
        public static void SetOutline(this GameObject _gameObject, OutlineState _state)
        {
            if (_gameObject == null)
            {
                return;
            }
            OutlineTarget _target = _gameObject.GetComponent<OutlineTarget>();
            if (_target == null)
            {
                if (_state == OutlineState.None)
                {
                    return;
                }
                _target = _gameObject.AddComponent<OutlineTarget>();
            }
            _target.SetState(_state);
        }
    }
}
