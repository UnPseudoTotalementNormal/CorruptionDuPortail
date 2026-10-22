using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Outlines of the pointable objects (powers, board buttons, the voted cat). Replaces Linework Lite's Free Outline,
/// which inflated each mesh: holes on hard edges, a line around every inner part, a width that changed with the mesh.
///
/// Here the outline is computed on screen. Each style matches a rendering layer ("Outline Hover"…): the renderers
/// carrying it are drawn into a mask, a jump flood turns the mask's silhouette into a distance field, and the
/// composite draws the line, the glow and the inner rim from that distance. One silhouette per object, the same width
/// all around, smooth edges. The game only switches rendering layer bits (Game's <c>OutlineTarget</c>), never the
/// GameObject layer, so physics, culling and the card picker's layers stay untouched.
///
/// Nothing runs while the game marks no object (the global <c>_CdpOutlineCount</c> it keeps up to date).
/// </summary>
[DisallowMultipleRendererFeature("Outline")]
public class OutlineFeature : ScriptableRendererFeature
{
    public const int MaxStyles = 8;
    internal static readonly int ActiveCountId = Shader.PropertyToID("_CdpOutlineCount");

    [Serializable]
    public class Style
    {
        [Tooltip("Rendering layer (Project Settings > Tags and Layers) that gives this style to a renderer.")]
        public string renderingLayer = "Outline Hover";
        [ColorUsage(true, true), Tooltip("Line and glow colour; alpha = line opacity. Past 1 it feeds the bloom.")]
        public Color color = Color.white;
        [Min(0f), Tooltip("Line width in pixels at 1080p (scaled with the resolution).")]
        public float width = 2f;
        [Min(0f), Tooltip("Fall-off of the line's outer edge, in pixels at 1080p.")]
        public float softness = 1f;
        [Min(0f), Tooltip("Halo beyond the line, in pixels at 1080p. 0 = none.")]
        public float glowRadius;
        [Range(0f, 2f)] public float glowIntensity;
        [Min(0f), Tooltip("Light along the inside of the silhouette, in pixels at 1080p. 0 = none.")]
        public float rimWidth;
        [Range(0f, 2f)] public float rimIntensity;
        [Min(0f), Tooltip("Breathing of the outline, in cycles per second. 0 = steady.")]
        public float pulseSpeed;
        [Range(0f, 1f), Tooltip("How much of the opacity the breathing takes away at its low point.")]
        public float pulseAmount;
        [Tooltip("Outline the whole object even where something stands in front of it.")]
        public bool seeThrough;
    }

    [SerializeField, Tooltip("Hidden/CdP/Outline. Serialized so the player build keeps it.")]
    private Shader shader;
    [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;
    [SerializeField] private List<Style> styles = new();

    private Material _material;
    private OutlinePass _pass;

    public override void Create()
    {
        if (shader == null)
        {
            shader = Shader.Find("Hidden/CdP/Outline");
        }
        if (shader != null)
        {
            _material = CoreUtils.CreateEngineMaterial(shader);
        }
        _pass = new OutlinePass { renderPassEvent = injectionPoint };
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType _type = renderingData.cameraData.cameraType;
        if (_type != CameraType.Game && _type != CameraType.SceneView)
        {
            return;
        }
        if (_material == null || styles.Count == 0 || Shader.GetGlobalInt(ActiveCountId) <= 0)
        {
            return;
        }
        _pass.Setup(shader, _material, styles);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private class OutlinePass : ScriptableRenderPass
    {
        private const int MaskPass = 0;
        private const int SeedPass = 1;
        private const int JumpPass = 2;
        private const int CompositePass = 3;

        private static readonly int MaskValueId = Shader.PropertyToID("_OutlineMaskValue");
        private static readonly int UseDepthId = Shader.PropertyToID("_OutlineUseDepth");
        private static readonly int TexelSizeId = Shader.PropertyToID("_OutlineTexelSize");
        private static readonly int JumpStepId = Shader.PropertyToID("_OutlineJumpStep");
        private static readonly int ScaleId = Shader.PropertyToID("_OutlineScale");
        private static readonly int MaskId = Shader.PropertyToID("_OutlineMask");
        private static readonly int ColorsId = Shader.PropertyToID("_OutlineColors");
        private static readonly int ShapesId = Shader.PropertyToID("_OutlineShapes");
        private static readonly int ExtrasId = Shader.PropertyToID("_OutlineExtras");
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private static readonly ShaderTagId[] ShaderTags =
        {
            new("UniversalForward"), new("UniversalForwardOnly"), new("SRPDefaultUnlit"),
        };

        private readonly Vector4[] _colors = new Vector4[MaxStyles];
        private readonly Vector4[] _shapes = new Vector4[MaxStyles];
        private readonly Vector4[] _extras = new Vector4[MaxStyles];
        private readonly List<(uint Mask, float Value, bool SeeThrough)> _layers = new();

        private Shader _shader;
        private Material _material;
        private float _maxReach;

        public void Setup(Shader _overrideShader, Material _outlineMaterial, List<Style> _styles)
        {
            _shader = _overrideShader;
            _material = _outlineMaterial;
            _layers.Clear();
            _maxReach = 1f;
            int _count = Mathf.Min(_styles.Count, MaxStyles);
            for (int _i = 0; _i < _count; _i++)
            {
                Style _style = _styles[_i];
                uint _mask = RenderingLayerMask.GetMask(_style.renderingLayer);
                if (_mask == 0)
                {
                    continue;
                }
                // Mask value = style index + 1 (0 = not outlined), read back as an 8-bit value.
                _layers.Add((_mask, (_i + 1) / 255f, _style.seeThrough));
                _colors[_i] = _style.color;
                _shapes[_i] = new Vector4(_style.width, _style.softness, _style.glowRadius, _style.glowIntensity);
                _extras[_i] = new Vector4(_style.rimWidth, _style.rimIntensity, _style.pulseSpeed, _style.pulseAmount);
                _maxReach = Mathf.Max(_maxReach, _style.width + _style.softness + _style.glowRadius, _style.rimWidth);
            }
        }

        private class MaskData
        {
            internal readonly List<(RendererListHandle List, float Value, bool SeeThrough)> Lists = new();
            internal bool HasDepth;
        }

        private class FullscreenData
        {
            internal Material Material;
            internal int Pass;
            internal TextureHandle Source;
            internal TextureHandle Mask;
            internal Vector4 TexelSize;
            internal float JumpStep;
            internal float Scale;
            internal Vector4[] Colors;
            internal Vector4[] Shapes;
            internal Vector4[] Extras;
            internal MaterialPropertyBlock Properties;
        }

        private readonly MaterialPropertyBlock _properties = new();

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_layers.Count == 0 || _material == null)
            {
                return;
            }
            var _resources = frameData.Get<UniversalResourceData>();
            var _rendering = frameData.Get<UniversalRenderingData>();
            var _camera = frameData.Get<UniversalCameraData>();
            var _lights = frameData.Get<UniversalLightData>();
            if (_resources.isActiveTargetBackBuffer)
            {
                return;
            }

            RenderTextureDescriptor _target = _camera.cameraTargetDescriptor;
            int _width = Mathf.Max(1, _target.width);
            int _height = Mathf.Max(1, _target.height);
            var _texelSize = new Vector4(_width, _height, 1f / _width, 1f / _height);
            float _scale = _height / 1080f;

            var _maskDesc = new TextureDesc(_width, _height)
            {
                colorFormat = GraphicsFormat.R8_UNorm,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_OutlineMask",
            };
            TextureHandle _mask = renderGraph.CreateTexture(_maskDesc);

            // 1. Mask: every marked renderer the camera sees, as its style index.
            using (var _builder = renderGraph.AddRasterRenderPass<MaskData>("Outline Mask", out var _data))
            {
                _builder.SetRenderAttachment(_mask, 0);
                _data.HasDepth = _resources.cameraDepthTexture.IsValid();
                if (_data.HasDepth)
                {
                    _builder.UseTexture(_resources.cameraDepthTexture);
                }
                DrawingSettings _drawing = RenderingUtils.CreateDrawingSettings(
                    new List<ShaderTagId>(ShaderTags), _rendering, _camera, _lights, SortingCriteria.None);
                _drawing.overrideShader = _shader;
                _drawing.overrideShaderPassIndex = MaskPass;
                _drawing.perObjectData = PerObjectData.None;
                foreach (var (_layerMask, _value, _seeThrough) in _layers)
                {
                    var _filtering = new FilteringSettings(RenderQueueRange.all, _camera.camera.cullingMask, _layerMask);
                    var _params = new RendererListParams(_rendering.cullResults, _drawing, _filtering);
                    RendererListHandle _list = renderGraph.CreateRendererList(_params);
                    _builder.UseRendererList(_list);
                    _data.Lists.Add((_list, _value, _seeThrough));
                }
                _builder.AllowPassCulling(false);
                _builder.AllowGlobalStateModification(true);
                _builder.SetRenderFunc((MaskData _pass, RasterGraphContext _context) =>
                {
                    foreach (var (_list, _value, _seeThrough) in _pass.Lists)
                    {
                        _context.cmd.SetGlobalFloat(MaskValueId, _value);
                        _context.cmd.SetGlobalFloat(UseDepthId, _pass.HasDepth && !_seeThrough ? 1f : 0f);
                        _context.cmd.DrawRendererList(_list);
                    }
                    _pass.Lists.Clear();
                });
            }

            // 2. Jump flood from the silhouette's pixels, enough steps to reach the widest style.
            var _seedDesc = new TextureDesc(_width, _height)
            {
                colorFormat = GraphicsFormat.R32G32_SFloat,
                clearBuffer = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_OutlineSeedA",
            };
            TextureHandle _ping = renderGraph.CreateTexture(_seedDesc);
            _seedDesc.name = "_OutlineSeedB";
            TextureHandle _pong = renderGraph.CreateTexture(_seedDesc);

            AddFullscreen(renderGraph, "Outline Seed", _mask, _ping, SeedPass, _texelSize, 0f, _scale, default);
            int _step = Mathf.NextPowerOfTwo(Mathf.CeilToInt(_maxReach * _scale + 1f));
            while (_step >= 1)
            {
                AddFullscreen(renderGraph, "Outline Jump", _ping, _pong, JumpPass, _texelSize, _step, _scale, default);
                (_ping, _pong) = (_pong, _ping);
                _step /= 2;
            }

            // 3. Composite over the camera colour.
            AddFullscreen(renderGraph, "Outline Composite", _ping, _resources.activeColorTexture, CompositePass,
                _texelSize, 0f, _scale, _mask);
        }

        private void AddFullscreen(RenderGraph _graph, string _name, TextureHandle _source, TextureHandle _destination,
            int _pass, Vector4 _texelSize, float _jumpStep, float _scale, TextureHandle _mask)
        {
            using var _builder = _graph.AddRasterRenderPass<FullscreenData>(_name, out var _data);
            _data.Material = _material;
            _data.Pass = _pass;
            _data.Source = _source;
            _data.Mask = _mask;
            _data.TexelSize = _texelSize;
            _data.JumpStep = _jumpStep;
            _data.Scale = _scale;
            _data.Colors = _colors;
            _data.Shapes = _shapes;
            _data.Extras = _extras;
            _data.Properties = _properties;
            _builder.UseTexture(_source);
            if (_mask.IsValid())
            {
                _builder.UseTexture(_mask);
            }
            _builder.SetRenderAttachment(_destination, 0);
            _builder.AllowPassCulling(false);
            _builder.SetRenderFunc((FullscreenData _d, RasterGraphContext _context) =>
            {
                MaterialPropertyBlock _block = _d.Properties;
                _block.Clear();
                _block.SetTexture(BlitTextureId, _d.Source);
                _block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                _block.SetVector(TexelSizeId, _d.TexelSize);
                _block.SetFloat(JumpStepId, _d.JumpStep);
                _block.SetFloat(ScaleId, _d.Scale);
                if (_d.Mask.IsValid())
                {
                    _block.SetTexture(MaskId, _d.Mask);
                    _block.SetVectorArray(ColorsId, _d.Colors);
                    _block.SetVectorArray(ShapesId, _d.Shapes);
                    _block.SetVectorArray(ExtrasId, _d.Extras);
                }
                _context.cmd.DrawProcedural(Matrix4x4.identity, _d.Material, _d.Pass, MeshTopology.Triangles, 3, 1, _block);
            });
        }
    }
}
